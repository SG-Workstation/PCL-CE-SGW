using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net.Http;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.Profile.Authentication.Utils.Models;

namespace PCL.Core.Minecraft.Profile.Authentication.Utils;

public static class XboxUtils
{
    private const string XboxLiveAuthServer = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsAuthorizeServer = "https://xsts.auth.xboxlive.com/xsts/authorize";
    public static async Task<XboxLiveResponse?> AuthenticateAsync<T>(XboxAuthenticate<T> authData, bool isXsts,
        CancellationToken token)
    {
        using var response = await HttpRequest.CreatePost(
                isXsts ? XstsAuthorizeServer : XboxLiveAuthServer
            ).WithJsonContent(authData).SendAsync(cancellationToken: token)
            .ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            var content = await response.AsStringAsync(token).ConfigureAwait(false);
            long? xErr = null;
            string? remoteMessage = null;
            try
            {
                var error = JsonNode.Parse(content);
                if (long.TryParse(error?["XErr"]?.ToString(), out var parsed)) xErr = parsed;
                remoteMessage = error?["Message"]?.ToString();
            }
            catch (JsonException)
            {
                // 响应体不是 JSON，保留原文用于诊断
            }

            var stage = isXsts ? "XSTS" : "Xbox Live";
            var friendly = xErr is null ? null : GetFriendlyXErr(xErr.Value);
            var detail = friendly ??
                         remoteMessage ??
                         $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            var code = xErr is null
                ? $"{(isXsts ? "xsts" : "xbl")}_http_{(int)response.StatusCode}"
                : $"{(isXsts ? "xsts" : "xbl")}_{xErr}";
            throw new IdentityModelAuthenticationException(code,
                $"{stage} 认证失败（{detail}）" +
                (xErr is null ? "" : $" [XErr {xErr}]") +
                (string.IsNullOrWhiteSpace(content) ? "" : $"\r\n响应：{Truncate(content)}"));
        }

        return await response.AsJsonAsync<XboxLiveResponse>(cancellationToken: token);
    }

    private static string? GetFriendlyXErr(long xErr) => xErr switch
    {
        2148916233 => "该 Microsoft 账户还没有 Xbox 账户，请先打开 xbox.com 登录一次以创建档案",
        2148916235 => "该账户所在的国家/地区不支持 Xbox Live",
        2148916236 or 2148916237 or 2148916229 => "该账户需要先完成 Xbox 的成人验证",
        2148916238 => "这是未成年人账户，需要先加入 Microsoft 家庭组",
        2148916227 => "该账户已被 Xbox Live 封禁",
        _ => null
    };

    private static string Truncate(string text) =>
        text.Length <= 500 ? text : text[..500] + "…";
}
