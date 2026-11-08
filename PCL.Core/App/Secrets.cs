using System;
using PCL.Core.Utils.Exts;
using PCL.Core.Utils.OS;

namespace PCL.Core.App;

// ReSharper disable InconsistentNaming
public static class Secrets
{
    /// <summary>
    /// 微软 OAuth 的 Client ID
    /// </summary>
    public static string MSOAuthClientId { get; } = ResolveSecret("MS_CLIENT_ID",
#if LOCAL_SECRETS
        LocalSecrets.MSClientId
#else
        ""
#endif
    );

    /// <summary>
    /// CurseForge API 的 Client ID
    /// </summary>
    public static string CurseForgeAPIKey { get; } = ResolveSecret("CURSEFORGE_API_KEY",
#if LOCAL_SECRETS
        LocalSecrets.CurseForgeApiKey
#else
        ""
#endif
    );

    /// <summary>
    /// 解析密钥：优先使用构建注入 / 环境变量，其次使用本机 <c>LocalSecrets.cs</c> 中的硬编码值。
    /// </summary>
    private static string ResolveSecret(string key, string localValue)
    {
        var external = EnvironmentInterop.GetSecret(key, readEnvDebugOnly: true).ReplaceNullOrEmpty();
        return !string.IsNullOrWhiteSpace(external) ? external : localValue.ReplaceNullOrEmpty();
    }

    /// <summary>
    /// 遥测密钥
    /// </summary>
    public static string TelemetryKey { get; } = EnvironmentInterop.GetSecret("TELEMETRY_KEY", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// Natayark ID OAuth 的 Client ID
    /// </summary>
    public static string NatayarkClientId { get; } = EnvironmentInterop.GetSecret("NAID_CLIENT_ID", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// Natayark ID OAuth 的 Client ID
    /// </summary>
    public static string NatayarkClientSecret { get; } = EnvironmentInterop.GetSecret("NAID_CLIENT_SECRET", readEnvDebugOnly: true).ReplaceNullOrEmpty();

    /// <summary>
    /// 联机根服务器
    /// </summary>
    public static string[] LinkServers { get; } = EnvironmentInterop.GetSecret("LINK_SERVER_ROOT", readEnvDebugOnly: true)
        .ReplaceNullOrEmpty()
        .Split("|".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// 当前版本的 Git 提交 SHA
    /// </summary>
    public static string CommitHash { get; } = EnvironmentInterop.GetSecret("GITHUB_SHA", readEnvDebugOnly: true).ReplaceNullOrEmpty();
}
