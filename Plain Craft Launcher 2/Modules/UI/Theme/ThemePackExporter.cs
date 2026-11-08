using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.UI.Theme.Packs;

namespace PCL;

/// <summary>
/// 把当前界面外观（配色、字体、壁纸、布局）导出成一个可再分发的主题包。
/// </summary>
public static class ThemePackExporter
{
    /// <summary>
    /// 导出时采集的资源键。颜色会原样写进清单，保证换主题后外观一致。
    /// </summary>
    private static readonly string[] ExportedColorKeys =
    [
        "ColorBrush1", "ColorBrush2", "ColorBrush3", "ColorBrush4",
        "ColorBrush5", "ColorBrush6", "ColorBrush7", "ColorBrush8",
        "ColorBrushBg0", "ColorBrushBg1", "ColorBrushSemiTransparent",
        "ColorBrushGray1", "ColorBrushGray2", "ColorBrushGray3", "ColorBrushGray4",
        "ColorBrushGray5", "ColorBrushGray6", "ColorBrushGray7", "ColorBrushGray8",
        "ColorBrushBackground", "ColorBrushToolTip", "ColorBrushHalfWhite", "ColorBrushSemiWhite"
    ];

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 导出当前外观，返回主题目录路径；失败时返回 <c>null</c> 并给出错误信息。
    /// </summary>
    public static string? Export(string displayName, out string error)
    {
        error = "";
        try
        {
            var folderId = MakeId(displayName);
            var root = ThemePackStore.Root;
            Directory.CreateDirectory(root);

            var folder = Path.Combine(root, folderId);
            for (var i = 2; Directory.Exists(folder); i++)
                folder = Path.Combine(root, $"{folderId}-{i}");

            var id = Path.GetFileName(folder) ?? folderId;
            Directory.CreateDirectory(folder);

            var manifest = new Dictionary<string, object?>
            {
                ["schema"] = 1,
                ["id"] = id,
                ["name"] = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim(),
                ["author"] = "SG Workstation",
                ["version"] = "1.0.0",
                ["description"] = "由当前界面外观导出",
                ["license"] = "",
                ["colors"] = CollectColors()
            };

            var fontTag = Config.Preference.Font;
            if (!string.IsNullOrWhiteSpace(fontTag))
                manifest["font"] = new Dictionary<string, object?> { ["family"] = fontTag };

            // 背景：图片或视频都放进主题目录的 assets/backgrounds
            var backgroundsFolder = Path.Combine(folder, "assets", "backgrounds");
            if (PageSetupUI.CurrentBackgroundPath is { } background && File.Exists(background) &&
                ThemePackValidator.BackgroundExtensions.Contains(Path.GetExtension(background),
                    StringComparer.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(backgroundsFolder);
                var fileName = "background" + Path.GetExtension(background).ToLowerInvariant();
                File.Copy(background, Path.Combine(backgroundsFolder, fileName), true);
                if (ImageExtensions.Contains(Path.GetExtension(background), StringComparer.OrdinalIgnoreCase))
                {
                    manifest["wallpaper"] = new Dictionary<string, object?>
                    {
                        ["asset"] = "assets/backgrounds/" + fileName,
                        ["opacity"] = Math.Clamp(Config.Preference.Background.WallpaperOpacity / 1000d, 0d, 1d),
                        ["blur"] = Config.Preference.Background.WallpaperBlurRadius
                    };
                }
            }

            // 音乐：把当前主题目录里的音乐一并打包
            if (ThemePackManager.MusicFolder is { } musicFolder && Directory.Exists(musicFolder))
            {
                var targetMusic = Path.Combine(folder, "assets", "music");
                Directory.CreateDirectory(targetMusic);
                var copied = 0;
                foreach (var file in Directory.EnumerateFiles(musicFolder))
                {
                    if (!ThemePackValidator.AudioExtensions.Contains(Path.GetExtension(file),
                            StringComparer.OrdinalIgnoreCase))
                        continue;
                    File.Copy(file, Path.Combine(targetMusic, Path.GetFileName(file)), true);
                    copied++;
                }

                if (copied > 0) manifest["music"] = new Dictionary<string, object?> { ["folder"] = "assets/music" };
            }

            // 若当前启用了主题包，连同它的布局与样式一起带走，导出的外观才一致
            if (ThemePackManager.Current is { } current)
            {
                if (current.Manifest.Layout is { } layout) manifest["layout"] = layout;
                if (current.Manifest.Styles is { Count: > 0 } styles)
                {
                    manifest["styles"] = styles;
                    foreach (var style in styles)
                    {
                        if (!ThemePackValidator.TryResolve(current.Directory, style, out var source) ||
                            !File.Exists(source))
                            continue;
                        var target = Path.Combine(folder, style);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(source, target, true);
                    }
                }
            }

            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            File.WriteAllText(Path.Combine(folder, "theme.json"), json, new UTF8Encoding(false));
            return folder;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// 读取当前生效的颜色资源，写成 <c>#AARRGGBB</c>。
    /// </summary>
    private static Dictionary<string, string> CollectColors()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var resources = Application.Current.Resources;
        foreach (var key in ExportedColorKeys)
        {
            if (resources[key] is SolidColorBrush { Color: var color })
                result[key] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        return result;
    }

    /// <summary>
    /// 把用户输入的名称变成合法主题 id。
    /// </summary>
    private static string MakeId(string? displayName)
    {
        var name = (displayName ?? "").Trim().ToLowerInvariant();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')
                builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var id = builder.ToString().Trim('-', '.', '_');
        if (id.Length == 0) id = "exported-theme";
        if (!char.IsLetterOrDigit(id[0])) id = "theme-" + id;
        if (id.Length > 32) id = id[..32].Trim('-', '.', '_');
        return ThemePackValidator.IsValidId(id) ? id : "exported-theme";
    }
}
