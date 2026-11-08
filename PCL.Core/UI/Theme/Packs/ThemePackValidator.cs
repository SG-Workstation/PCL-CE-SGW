using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace PCL.Core.UI.Theme.Packs;

/// <summary>
/// 主题包校验器。所有路径都必须落在主题目录内部，所有资产都必须属于允许的扩展名。
/// </summary>
public static class ThemePackValidator
{
    /// <summary>
    /// 当前启动器支持的清单格式版本。
    /// </summary>
    public const int SupportedSchema = 1;

    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

    public static readonly string[] VideoExtensions = [".mp4", ".webm", ".avi", ".mkv", ".mov", ".m4v"];

    public static readonly string[] AudioExtensions = [".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma"];

    /// <summary>
    /// 背景可用的媒体扩展名（图片或视频）。
    /// </summary>
    public static readonly string[] BackgroundExtensions = [.. ImageExtensions, .. VideoExtensions];

    public static readonly string[] FontExtensions = [".ttf", ".otf", ".woff", ".woff2"];

    private static readonly Regex IdPattern = new("^[a-z0-9]([a-z0-9._-]{0,31})$", RegexOptions.Compiled);

    /// <summary>
    /// 校验清单与目录是否一致。返回空列表代表完全通过。
    /// </summary>
    public static IReadOnlyList<ThemePackIssue> Validate(ThemePackManifest manifest, string directory)
    {
        var issues = new List<ThemePackIssue>();

        if (!Directory.Exists(directory))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"主题目录不存在：{directory}"));
            return issues;
        }

        if (manifest.Schema <= 0)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"清单的 schema 非法：{manifest.Schema}"));
        }
        else if (manifest.Schema > SupportedSchema)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error,
                $"主题包格式版本 {manifest.Schema} 高于当前启动器支持的 {SupportedSchema}，请更新启动器"));
        }

        if (!IsValidId(manifest.Id))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error,
                "主题 id 非法：只允许小写字母、数字、点、下划线、短横线，且需以字母或数字开头、最长 32 个字符"));
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, "主题名称为空"));
        }

        if (string.IsNullOrWhiteSpace(manifest.License))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning, "主题包没有声明许可证，分发前请补上 license 字段"));
        }

        if (manifest.Colors is not null)
        {
            foreach (var (key, value) in manifest.Colors)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, "colors 中存在空资源键"));
                    continue;
                }

                if (!key.StartsWith("ColorObject", StringComparison.Ordinal) &&
                    !key.StartsWith("ColorBrush", StringComparison.Ordinal))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning,
                        $"colors 中的键 {key} 不是以 ColorObject 或 ColorBrush 开头，将被忽略"));
                    continue;
                }

                if (!TryParseColor(value, out _))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"colors[{key}] 不是合法颜色：{value}"));
                }
            }
        }

        if (manifest.Font is { } font)
        {
            if (string.IsNullOrWhiteSpace(font.Family) && string.IsNullOrWhiteSpace(font.Asset))
            {
                issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning, "font 既没有 family 也没有 asset，将被忽略"));
            }

            if (!string.IsNullOrWhiteSpace(font.Asset))
            {
                CheckAsset(directory, font.Asset, FontExtensions, "font.asset", issues);
            }
        }

        if (manifest.Wallpaper is { } wallpaper)
        {
            if (!string.IsNullOrWhiteSpace(wallpaper.Asset))
            {
                CheckAsset(directory, wallpaper.Asset, BackgroundExtensions, "wallpaper.asset", issues);
            }
            else
            {
                issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning, "wallpaper 没有给出 asset，将被忽略"));
            }

            if (wallpaper.Opacity is < 0 or > 1)
            {
                issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning, "wallpaper.opacity 超出 0~1，将被截断"));
            }
        }

        if (manifest.Music is { } music)
        {
            if (!string.IsNullOrWhiteSpace(music.Folder))
            {
                if (!TryResolve(directory, music.Folder, out var musicFolder))
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"music.folder 越界：{music.Folder}"));
                else if (!Directory.Exists(musicFolder))
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"music.folder 指向的目录不存在：{music.Folder}"));
            }

            foreach (var file in music.Files ?? [])
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning, "music.files 中存在空路径，已忽略"));
                    continue;
                }

                CheckAsset(directory, file, AudioExtensions, "music.files", issues);
            }
        }

        if (manifest.Styles is not null)
        {
            foreach (var style in manifest.Styles)
            {
                if (string.IsNullOrWhiteSpace(style))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning, "styles 中存在空路径，已忽略"));
                    continue;
                }

                if (!TryResolve(directory, style, out var fullPath))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式路径越界：{style}"));
                    continue;
                }

                if (!fullPath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件必须是 .xaml：{style}"));
                    continue;
                }

                issues.AddRange(ThemeStyleGuard.ValidateFile(fullPath));
            }
        }

        if (manifest.Layout?.Pages is { } pages)
        {
            foreach (var (page, layout) in pages)
            {
                if (string.IsNullOrWhiteSpace(page))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, "layout.pages 中存在空页面名"));
                    continue;
                }

                foreach (var name in layout.Hidden ?? [])
                {
                    if (string.IsNullOrWhiteSpace(name))
                        issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning, $"layout.pages[{page}].hidden 中存在空名称，已忽略"));
                }

                if (layout.Properties is not { } properties) continue;
                foreach (var (elementName, values) in properties)
                {
                    if (string.IsNullOrWhiteSpace(elementName))
                    {
                        issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"layout.pages[{page}].properties 中存在空控件名"));
                        continue;
                    }

                    foreach (var (property, value) in values ?? [])
                    {
                        if (!TryValidateLayoutProperty(property, value, out var error))
                        {
                            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error,
                                $"layout.pages[{page}].properties[{elementName}].{property} {error}"));
                        }
                    }
                }
            }
        }

        if (manifest.Layout?.Window is { } window)
        {
            CheckWindowSize(window.Width, "width", issues);
            CheckWindowSize(window.Height, "height", issues);
            CheckWindowSize(window.MinWidth, "minWidth", issues);
            CheckWindowSize(window.MinHeight, "minHeight", issues);
        }

        return issues;
    }

    /// <summary>
    /// 受支持的逐控件布局属性（不区分大小写）。
    /// </summary>
    public static readonly string[] LayoutPropertyNames =
    [
        "margin", "padding", "width", "height", "minwidth", "minheight", "maxwidth", "maxheight",
        "horizontalalignment", "verticalalignment", "opacity", "visibility", "fontsize",
        "cornerradius", "background", "borderbrush"
    ];

    /// <summary>
    /// 校验一个布局属性取值是否合法。
    /// </summary>
    public static bool TryValidateLayoutProperty(string property, string value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(property))
        {
            error = "属性名为空";
            return false;
        }

        var key = property.Trim().ToLowerInvariant();
        if (!LayoutPropertyNames.Contains(key))
        {
            error = $"不是受支持的布局属性（可用：{string.Join(", ", LayoutPropertyNames)}）";
            return false;
        }

        switch (key)
        {
            case "margin":
            case "padding":
                if (!TryConvert<ThicknessConverter, Thickness>(value)) error = $"不是合法的厚度值：{value}";
                break;
            case "cornerradius":
                if (!TryConvert<CornerRadiusConverter, CornerRadius>(value)) error = $"不是合法的圆角值：{value}";
                break;
            case "width":
            case "height":
            case "minwidth":
            case "minheight":
            case "maxwidth":
            case "maxheight":
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size < 0)
                    error = $"不是合法的尺寸：{value}";
                break;
            case "fontsize":
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize) || fontSize <= 0)
                    error = $"不是合法的字号：{value}";
                break;
            case "opacity":
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) ||
                    opacity is < 0 or > 1)
                    error = $"透明度必须在 0~1 之间：{value}";
                break;
            case "horizontalalignment":
                if (!Enum.TryParse<System.Windows.HorizontalAlignment>(value, true, out _))
                    error = $"不是合法的水平对齐：{value}";
                break;
            case "verticalalignment":
                if (!Enum.TryParse<System.Windows.VerticalAlignment>(value, true, out _))
                    error = $"不是合法的垂直对齐：{value}";
                break;
            case "visibility":
                if (!Enum.TryParse<System.Windows.Visibility>(value, true, out _))
                    error = $"不是合法的可见性：{value}";
                break;
            case "background":
            case "borderbrush":
                if (!TryParseColor(value, out _)) error = $"不是合法的颜色：{value}";
                break;
        }

        return error is null;
    }

    private static void CheckWindowSize(double? value, string name, List<ThemePackIssue> issues)
    {
        if (value is null) return;
        if (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value <= 0)
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"layout.window.{name} 必须是正数"));
    }

    private static bool TryConvert<TConverter, TResult>(string value) where TConverter : TypeConverter, new()
    {
        try
        {
            var converted = new TConverter().ConvertFromInvariantString(value);
            return converted is TResult;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 主题 id 是否合法。
    /// </summary>
    public static bool IsValidId(string? id) => !string.IsNullOrEmpty(id) && IdPattern.IsMatch(id);

    /// <summary>
    /// 把相对路径解析为绝对路径，并确保它仍位于主题目录内。
    /// </summary>
    public static bool TryResolve(string root, string relative, out string fullPath)
    {
        fullPath = "";
        try
        {
            if (Path.IsPathRooted(relative)) return false;
            var rootFull = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(rootFull, relative));
            var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate, rootFull, StringComparison.OrdinalIgnoreCase))
                return false;
            fullPath = candidate;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void CheckAsset(string directory, string relative, string[] extensions, string field, List<ThemePackIssue> issues)
    {
        if (!TryResolve(directory, relative, out var fullPath))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"{field} 越界：{relative}"));
            return;
        }

        if (!File.Exists(fullPath))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"{field} 指向的文件不存在：{relative}"));
            return;
        }

        var extension = Path.GetExtension(fullPath);
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error,
                $"{field} 的扩展名不受支持：{extension}（允许：{string.Join(", ", extensions)}）"));
        }
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (Exception)
        {
            // 交给调用方按不合法处理
        }

        return false;
    }

    /// <summary>
    /// 供文档与默认主题使用：把颜色格式化为 <c>#AARRGGBB</c>。
    /// </summary>
    public static string FormatColor(Color color) =>
        string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", color.A, color.R, color.G, color.B);
}
