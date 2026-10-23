using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using Ae.Dns.Client;
using Ae.Dns.Protocol;
using Ae.Dns.Protocol.Enums;
using Ae.Dns.Protocol.Records;
using PCL.Core.IO.Net.Http;
using PCL.Core.Logging;

namespace PCL.Core.IO.Net.Dns;

public class DnsQuery : IDisposable
{
    private const string ModuleName = "DoH query";
    public static DnsQuery Instance { get; } = new();

    private readonly DnsCachingClient _resolver;
    private readonly HttpClient[] _httpClients;

    private DnsQuery()
    {
        var proxyHandler = new HttpClientHandler()
        {
            Proxy = HttpProxyManager.Instance
        };
        // 使用Ae.Dns创建DoH客户端，支持多个DoH服务器
        // 端点可用性经过实测：doh.pysio.online 证书已过期、cloudflare-dns.com 在部分网络被 reset，
        // 因此改用国内可用的 RFC8484 端点，避免竞速时总是抽到坏端点而拖慢整体解析
        _httpClients =
        [
            new HttpClient(proxyHandler)
            {
                BaseAddress = new Uri("https://doh.pub/")
            },
            new HttpClient(proxyHandler)
            {
                BaseAddress = new Uri("https://dns.alidns.com/")
            },
            new HttpClient(proxyHandler)
            {
                BaseAddress = new Uri("https://doh.360.cn/")
            }
        ];
        _resolver = new DnsCachingClient(
            new WeightedDnsRacerClient(2, _httpClients.Select(static x => new DnsHttpClient(x)).ToArray<IDnsClient>()),
            new MemoryCache("DoH Query Cache"));
    }

    public async Task<DnsMessage?> QueryAsync(string host, DnsQueryType qType, CancellationToken cts = default)
    {
        try
        {
            return await _resolver.Query(DnsQueryFactory.CreateQuery(host, qType), cts);
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogWrapper.Debug(ModuleName, $"Failed to resolve DNS for {host}: {ex.Message}, use system default DNS");
        }

        return null;
    }

    public async Task<IPAddress[]?> QueryForIpAsync(string host, CancellationToken cts = default)
    {
        var queryResponse = await Task.WhenAll(
            [
                QueryAsync(host, DnsQueryType.A, cts),
                QueryAsync(host, DnsQueryType.AAAA, cts)
            ]
        );

        if (queryResponse.All(static x => x is null))
        {
            LogWrapper.Debug(ModuleName, $"Failed to query IP for host {host} using DoH, use system default DNS");
            return await System.Net.Dns.GetHostAddressesAsync(host, cts);
        }

        var addresses = queryResponse.Where(static x => x is not null)
            .SelectMany(static x => x!.Answers)
            .Where(static x => x.Type is DnsQueryType.A or DnsQueryType.AAAA)
            .Select(static x => x.Resource as DnsIpAddressResource)
            .Where(static x => x is not null)
            .Select(static x => x!.IPAddress)
            .Distinct()
            .ToList();

        // A 与 AAAA 是两次独立查询，可能只成功一半（部分 DoH 端点不可用时会这样）。
        // 半份结果不能直接当完整结果用：在没有 IPv6 出口的机器上，只剩 AAAA 会导致所有连接尝试失败。
        // 缺失的地址族用系统 DNS 补齐。
        var hasV4 = addresses.Any(static ip => ip.AddressFamily == AddressFamily.InterNetwork);
        var hasV6 = addresses.Any(static ip => ip.AddressFamily == AddressFamily.InterNetworkV6);
        if (!hasV4 || !hasV6)
        {
            try
            {
                var systemAddresses = await System.Net.Dns.GetHostAddressesAsync(host, cts).ConfigureAwait(false);
                addresses.AddRange(systemAddresses.Where(ip =>
                    ip.AddressFamily == AddressFamily.InterNetwork ? !hasV4 : !hasV6));
                addresses = addresses.Distinct().ToList();
                LogWrapper.Debug(ModuleName, $"DoH 只返回了部分地址族，已用系统 DNS 补齐：{host}");
            }
            catch (Exception ex)
            {
                LogWrapper.Debug(ModuleName, $"Failed to fill missing address family for {host} from system DNS: {ex.Message}");
            }
        }

        return addresses.Count == 0 ? null : addresses.ToArray();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var client in _httpClients)
        {
            client.Dispose();
        }
        _resolver.Dispose(); // 好像这个包的 Dispose 并没有做什么 lol
    }
}