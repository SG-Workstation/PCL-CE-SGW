using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PCL.Core.UI.Theme.Packs;

/// <summary>
/// 主题包清单，对应主题目录下的 <c>theme.json</c>。
/// </summary>
public sealed record ThemePackManifest
{
    /// <summary>
    /// 清单格式版本，当前为 <c>1</c>。
    /// </summary>
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;

    /// <summary>
    /// 主题唯一标识，只允许小写字母、数字、点、下划线、短横线，最长 32 个字符。
    /// </summary>
    [JsonPropertyName("id")] public string Id { get; init; } = "";

    /// <summary>
    /// 显示名称。
    /// </summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    [JsonPropertyName("author")] public string? Author { get; init; }

    [JsonPropertyName("version")] public string? Version { get; init; }

    [JsonPropertyName("description")] public string? Description { get; init; }

    [JsonPropertyName("license")] public string? License { get; init; }

    [JsonPropertyName("homepage")] public string? Homepage { get; init; }

    [JsonPropertyName("minLauncher")] public string? MinLauncher { get; init; }

    /// <summary>
    /// 资源键到颜色字符串的映射。键以 <c>ColorObject</c> 开头时按 <see cref="System.Windows.Media.Color"/> 应用，
    /// 其余情况按 <see cref="System.Windows.Media.SolidColorBrush"/> 应用。
    /// </summary>
    [JsonPropertyName("colors")] public Dictionary<string, string>? Colors { get; init; }

    [JsonPropertyName("font")] public ThemePackFont? Font { get; init; }

    [JsonPropertyName("wallpaper")] public ThemePackWallpaper? Wallpaper { get; init; }

    [JsonPropertyName("music")] public ThemePackMusic? Music { get; init; }

    [JsonPropertyName("layout")] public ThemePackLayout? Layout { get; init; }

    /// <summary>
    /// 受限 XAML 样式文件的相对路径，按顺序合并到应用程序资源。
    /// </summary>
    [JsonPropertyName("styles")] public List<string>? Styles { get; init; }
}

/// <summary>
/// 主题字体。可以给出已安装字体族名，或给出字体文件相对路径。
/// </summary>
public sealed record ThemePackFont
{
    [JsonPropertyName("family")] public string? Family { get; init; }

    [JsonPropertyName("asset")] public string? Asset { get; init; }

    /// <summary>
    /// 相对当前字号的增量，作为后续排版细调的保留字段。
    /// </summary>
    [JsonPropertyName("sizeDelta")] public double? SizeDelta { get; init; }
}

/// <summary>
/// 主题壁纸，仅在用户没有放置自定义背景时生效。
/// </summary>
public sealed record ThemePackWallpaper
{
    [JsonPropertyName("asset")] public string Asset { get; init; } = "";

    [JsonPropertyName("opacity")] public double Opacity { get; init; } = 1;

    [JsonPropertyName("blur")] public double Blur { get; init; }
}

/// <summary>
/// 主题背景音乐。可以指定主题目录下的文件夹，也可以逐个列出文件。
/// </summary>
public sealed record ThemePackMusic
{
    [JsonPropertyName("folder")] public string? Folder { get; init; }

    [JsonPropertyName("files")] public List<string>? Files { get; init; }
}

/// <summary>
/// 页面布局覆盖：按页面类型名指定要隐藏或重新排序的具名元素。
/// </summary>
public sealed record ThemePackLayout
{
    [JsonPropertyName("pages")] public Dictionary<string, ThemePackPageLayout>? Pages { get; init; }

    /// <summary>
    /// 主窗口尺寸覆盖，留空的项保持用户当前尺寸。
    /// </summary>
    [JsonPropertyName("window")] public ThemePackWindow? Window { get; init; }
}

/// <summary>
/// 主窗口尺寸覆盖。
/// </summary>
public sealed record ThemePackWindow
{
    [JsonPropertyName("width")] public double? Width { get; init; }

    [JsonPropertyName("height")] public double? Height { get; init; }

    [JsonPropertyName("minWidth")] public double? MinWidth { get; init; }

    [JsonPropertyName("minHeight")] public double? MinHeight { get; init; }
}

public sealed record ThemePackPageLayout
{
    [JsonPropertyName("hidden")] public List<string>? Hidden { get; init; }

    [JsonPropertyName("order")] public List<string>? Order { get; init; }

    /// <summary>
    /// 逐个具名控件的属性覆盖，属性名不区分大小写，取值见 <see cref="ThemePackValidator.LayoutPropertyNames"/>。
    /// </summary>
    [JsonPropertyName("properties")] public Dictionary<string, Dictionary<string, string>>? Properties { get; init; }
}

public enum ThemePackIssueLevel
{
    Warning,
    Error
}

public sealed record ThemePackIssue(ThemePackIssueLevel Level, string Message)
{
    public override string ToString() => $"[{Level}] {Message}";
}

/// <summary>
/// 已加载的主题包。
/// </summary>
/// <param name="Manifest">清单内容。</param>
/// <param name="Directory">主题包所在目录的绝对路径。</param>
/// <param name="Issues">校验过程中记录的问题。</param>
public sealed record ThemePack(
    ThemePackManifest Manifest,
    string Directory,
    IReadOnlyList<ThemePackIssue> Issues)
{
    /// <summary>
    /// 是否没有阻断性错误，可以启用。
    /// </summary>
    public bool IsUsable
    {
        get
        {
            foreach (var issue in Issues)
            {
                if (issue.Level == ThemePackIssueLevel.Error) return false;
            }

            return true;
        }
    }
}
