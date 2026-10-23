using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;
using PCL.Core.Utils.Exts;

namespace PCL.Core.IO.Net.Http;

public class HostConnectionHandler
{
    public static HostConnectionHandler Instance { get; } = new();
    private const string ModuleName = "HostConnectionHandler";

    private static readonly TimeSpan _CacheDuration = TimeSpan.FromMinutes(10); // 10 分钟缓存(RFC 建议值)
    private const int WaitTasks = 2;

    // 缓存结构: (host, port) -> (IPAddress -> LastSuccessUtc)
    private readonly ConcurrentDictionary<(string host, int port), ConcurrentDictionary<IPAddress, DateTime>>
        _connectionCache = new();

    public async ValueTask<Stream> GetConnectionAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var now = DateTime.UtcNow;

        // 代理地址或者直连 IP 直接返回结果
        if (IPAddress.TryParse(host, out var directIp))
        {
            return await _ConnectToAddressAsync(directIp, port, cancellationToken).ConfigureAwait(false);
        }

        // 使用系统 DNS 解析（不走 DoH）
        var addresses = await System.Net.Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new HttpRequestException($"DNS resolution failed for {host}");
        }

        // 只使用 IPv4：本机没有 IPv6 出口时，AAAA 记录只会带来 10051 / 连接超时。
        // 仅当目标确实只有 IPv6 记录时才退回使用原始结果。
        var ipv4Addresses = addresses.Where(static ip => ip.AddressFamily == AddressFamily.InterNetwork).ToArray();
        if (ipv4Addresses.Length > 0) addresses = ipv4Addresses;

        // 对待连接地址进行排序（最近成功过的地址优先）
        var sortedAddresses = _SortAddresses(host, port, addresses, now);

        // Happy Eyeballs 连接逻辑，优先 IPv6 稍后
        var connectionTasks = new List<Task<NetworkStream>>(WaitTasks);
        var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connectCancellationToken = cancellationSource.Token;

        try
        {
            for (int i = 0; i < sortedAddresses.Length; i++)
            {
                var address = sortedAddresses[i];
                var delayMs = i * (address.AddressFamily.Equals(AddressFamily.InterNetwork) ? 150 : 80); // 偏向使用 v6

                connectionTasks.Add(_DelayedConnectAsync(address, port, delayMs, connectCancellationToken));
            }

            // 等待首个成功连接
            var winner = await connectionTasks
                .ToArray()
                .WhenAnySuccessAsync()
                .ConfigureAwait(false);
            var stream = await winner.ConfigureAwait(false);

            // 更新缓存 + 记录成功
            var remoteIp = ((IPEndPoint)stream.Socket.RemoteEndPoint!).Address;
            _UpdateConnectionCache(host, port, remoteIp, now);
            LogWrapper.Debug(ModuleName, $"Connected to {host} via {remoteIp}");

            // 取消其他连接
            // ReSharper disable once MethodHasAsyncOverload
            cancellationSource.Cancel();
            _ = _CleanupUnusedConnectionsAsync(connectionTasks, winner).ConfigureAwait(false);

            return stream;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 清理所有连接
            // ReSharper disable once MethodHasAsyncOverload
            cancellationSource.Cancel();
            _ = _CleanupUnusedConnectionsAsync(connectionTasks, null).ConfigureAwait(false);

            // 连接失败属于可恢复的日常网络问题（调用方会重试或回退镜像），
            // 只记录日志，不弹错误框：CI 构建里 Error 级别会映射成模态对话框
            LogWrapper.Debug(ex, ModuleName, $"All connection attempts failed for {host}");
            throw new HttpRequestException($"Connection failed for {host}", ex);
        }
        finally
        {
            cancellationSource.Dispose();
        }
    }

    private IPAddress[] _SortAddresses(string host, int port, IPAddress[] addresses, DateTime now)
    {
        var cacheKey = (host, port);
        var addressCache = _connectionCache.GetValueOrDefault(cacheKey);

        var cachedAddresses = new List<IPAddress>();
        var uncachedAddresses = new List<IPAddress>();

        foreach (var ip in addresses)
        {
            if (addressCache is not null &&
                addressCache.TryGetValue(ip, out var successTime) &&
                now - successTime <= _CacheDuration)
            {
                cachedAddresses.Add(ip);
            }
            else
            {
                uncachedAddresses.Add(ip);
            }
        }

        // 缓存地址: 按成功时间倒序 (最近成功的优先)
        cachedAddresses.Sort((a, b) =>
            addressCache![b].CompareTo(addressCache[a]));

        // 非缓存地址: 保持 DNS 返回顺序（已过滤为 IPv4）
        var sortedUncached = uncachedAddresses
            .OrderBy(ip => Array.IndexOf(addresses, ip))
            .ToList();

        return cachedAddresses
            .Concat(sortedUncached)
            .Distinct()
            .Take(WaitTasks)
            .ToArray();
    }

    private void _UpdateConnectionCache(string host, int port, IPAddress ip, DateTime now)
    {
        var cacheKey = (host, port);
        var addressCache = _connectionCache.GetOrAdd(cacheKey, static _ =>
            new ConcurrentDictionary<IPAddress, DateTime>());

        // 移除过期条目 (懒清理)
        foreach (var entry in addressCache)
        {
            if (now - entry.Value > _CacheDuration)
            {
                addressCache.TryRemove(entry.Key, out _);
            }
        }

        // 更新当前IP
        addressCache[ip] = now;
    }

    private static async Task<NetworkStream> _DelayedConnectAsync(IPAddress ip, int port, int delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        return await _ConnectToAddressAsync(ip, port, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<NetworkStream> _ConnectToAddressAsync(IPAddress ip, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            await socket.ConnectAsync(ip, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            socket.Dispose();
            throw new HttpRequestException($"Connection to {ip}:{port} failed", ex);
        }
    }

    private static async Task _CleanupUnusedConnectionsAsync(
        List<Task<NetworkStream>> allTasks,
        Task<NetworkStream>? winnerTask)
    {
        foreach (var task in allTasks.Where(t => t != winnerTask))
        {
            try
            {
                if (task.IsCompletedSuccessfully)
                {
                    var stream = await task.ConfigureAwait(false);
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                else if (task.IsCompleted)
                {
                    _ = task.Exception;
                }
                else
                {
                    _ = task.ContinueWith(static t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            t.Result.Dispose();
                        }
                        else
                        {
                            _ = t.Exception;
                        }
                    }, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                LogWrapper.Warn(ex, ModuleName, "Dispose connection with an error");
            }
        }
    }
}