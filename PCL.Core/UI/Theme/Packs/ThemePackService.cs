using System;
using PCL.Core.App;
using PCL.Core.Logging;

namespace PCL.Core.UI.Theme.Packs;

/// <summary>
/// 主题包服务：维护“当前启用的主题包”，并在切换时通知界面重新应用。
/// </summary>
public static class ThemePackService
{
    /// <summary>
    /// 当前启用的主题包，<c>null</c> 表示使用内置配色。
    /// </summary>
    public static ThemePack? Current { get; private set; }

    /// <summary>
    /// 主题包切换后触发，参数为新的主题包（可能为 <c>null</c>）。
    /// </summary>
    public static event Action<ThemePack?>? Changed;

    /// <summary>
    /// 当前配置里选中的主题 id。
    /// </summary>
    public static string ActiveId => Config.Preference.Theme.ThemePackId ?? "";

    /// <summary>
    /// 启动时调用：释放默认文件并加载当前主题。
    /// </summary>
    public static void Initialize()
    {
        ThemePackDefaults.Seed();
        LoadActive();
    }

    /// <summary>
    /// 按配置重新加载当前主题包。
    /// </summary>
    public static ThemePack? LoadActive()
    {
        try
        {
            Current = string.IsNullOrWhiteSpace(ActiveId) ? null : ThemePackStore.Load(ActiveId);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "ThemePack", "加载主题包失败");
            Current = null;
        }

        return Current;
    }

    /// <summary>
    /// 启用指定主题。<paramref name="id"/> 为空表示恢复内置配色。
    /// </summary>
    public static void SetActive(string id)
    {
        Config.Preference.Theme.ThemePackId = id ?? "";
        LoadActive();
        try
        {
            Changed?.Invoke(Current);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "ThemePack", "应用主题包时出错");
        }
    }

    /// <summary>
    /// 重新应用当前主题（例如配色模式切换后）。
    /// </summary>
    public static void Refresh() => SetActive(ActiveId);
}
