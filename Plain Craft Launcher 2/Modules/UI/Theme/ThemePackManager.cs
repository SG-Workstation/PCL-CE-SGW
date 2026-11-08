using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.UI.Theme;
using PCL.Core.UI.Theme.Packs;

namespace PCL;

/// <summary>
/// 把 <see cref="PCL.Core.UI.Theme.Packs"/> 里的主题包应用到 WPF 资源与页面布局。
/// <para>
/// 主题包目录固定为便携目录下的 <c>PCL/Themes</c>，首次启动时由
/// <see cref="ThemePackDefaults"/> 释放说明文件与示例清单。
/// </para>
/// </summary>
public static class ThemePackManager
{
    private static readonly List<ResourceDictionary> AppliedStyles = [];

    private static FontFamily? _defaultFontFamily;
    private static bool _initialized;
    private static bool _handlerRegistered;
    private static ThemePack? _current;

    /// <summary>
    /// 当前生效的主题包，<c>null</c> 表示内置配色。
    /// </summary>
    public static ThemePack? Current => _current;

    /// <summary>
    /// 当前主题包提供的壁纸绝对路径（用户没有自定义背景时生效）。
    /// </summary>
    public static string? WallpaperPath { get; private set; }

    /// <summary>
    /// 已安装的主题 id 列表。
    /// </summary>
    public static IReadOnlyList<string> InstalledIds => ThemePackStore.EnumerateIds();

    /// <summary>
    /// 主题包根目录，供“打开文件夹”按钮使用。
    /// </summary>
    public static string Root => ThemePackStore.Root;

    /// <summary>
    /// 当前主题提供的背景候选（图片或视频）。为空时回落到 <c>PCL/Pictures</c>。
    /// </summary>
    public static IReadOnlyList<string> BackgroundFiles { get; private set; } = [];

    /// <summary>
    /// 当前主题提供的背景音乐文件。
    /// </summary>
    public static IReadOnlyList<string> MusicFiles { get; private set; } = [];

    /// <summary>
    /// 当前主题的背景目录，没有主题或没有目录时为 <c>null</c>。
    /// </summary>
    public static string? BackgroundFolder { get; private set; }

    /// <summary>
    /// 当前主题的音乐目录，没有时为 <c>null</c>。
    /// </summary>
    public static string? MusicFolder { get; private set; }

    /// <summary>
    /// 取得“打开背景文件夹”按钮应当打开的目录：有主题时是主题内的 <c>assets/backgrounds</c>。
    /// </summary>
    public static string GetBackgroundFolder()
    {
        if (_current is not null)
        {
            var folder = Path.Combine(_current.Directory, "assets", "backgrounds");
            Directory.CreateDirectory(folder);
            return folder;
        }

        return Path.Combine(ModBase.exePath, "PCL", "Pictures");
    }

    /// <summary>
    /// 启动时调用一次。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _defaultFontFamily = Application.Current.Resources["LaunchFontFamily"] as FontFamily;
            ThemePackService.Initialize();
            ThemeService.ColorModeChanged += OnColorModeChanged;
            ThemeService.ColorThemeChanged += OnColorThemeChanged;
            EnsureHandlerRegistered();
            Reload();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[ThemePack] 初始化失败");
        }
    }

    /// <summary>
    /// 注册页面加载钩子。必须在主窗口构造之前调用，否则首屏页面会错过 Loaded 事件。
    /// </summary>
    public static void EnsureHandlerRegistered()
    {
        if (_handlerRegistered) return;
        _handlerRegistered = true;
        EventManager.RegisterClassHandler(typeof(MyPageRight), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnPageLoaded));
        EventManager.RegisterClassHandler(typeof(MyPageLeft), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnPageLoaded));
    }

    /// <summary>
    /// 重新读取配置里的主题并应用。
    /// </summary>
    public static void Reload()
    {
        try
        {
            _current = ThemePackService.LoadActive();
            ApplyAll();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[ThemePack] 应用主题失败");
        }
    }

    /// <summary>
    /// 启用指定主题，空字符串表示恢复内置配色。
    /// </summary>
    public static void Apply(string id)
    {
        try
        {
            ThemePackService.SetActive(id);
            _current = ThemePackService.Current;
            ApplyAll();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[ThemePack] 切换主题失败");
        }
    }

    /// <summary>
    /// 从文件夹安装主题，返回校验问题列表。
    /// </summary>
    public static IReadOnlyList<ThemePackIssue> InstallFromFolder(string folder) =>
        ThemePackStore.InstallFromFolder(folder);

    /// <summary>
    /// 删除已安装主题；如果删除的是当前主题则自动恢复内置配色。
    /// </summary>
    public static bool Delete(string id)
    {
        var deleted = ThemePackStore.Delete(id);
        if (deleted && string.Equals(ThemePackService.ActiveId, id, StringComparison.OrdinalIgnoreCase))
            Apply("");
        return deleted;
    }

    private static void OnColorModeChanged(bool isDarkMode, ColorTheme theme) => ApplyColors();

    private static void OnColorThemeChanged(ColorTheme theme) => ApplyColors();

    private static void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement page) ApplyLayout(page);
    }

    private static void ApplyAll()
    {
        ApplyColors();
        ApplyFont();
        ApplyStyles();
        ResolveAssets();
        ResolveWallpaper();
        ApplyWindow();
        ThemeManager.ThemeRefresh();

        // 布局对已经打开的页面立即生效
        foreach (Window window in Application.Current.Windows) ApplyLayoutRecursive(window);
        PageSetupUI.BackgroundRefresh(false, true);
        ModMusic.MusicListInit(false);

        ModBase.Log(
            $"[ThemePack] 已应用主题：{_current?.Manifest.Id ?? "(内置)"}，颜色覆盖 {_current?.Manifest.Colors?.Count ?? 0} 项，" +
            $"样式 {AppliedStyles.Count} 个，背景 {BackgroundFiles.Count} 个，音乐 {MusicFiles.Count} 首");
    }

    /// <summary>
    /// 解析主题目录内的背景与音乐资源：优先 <c>assets/backgrounds</c>、<c>assets/music</c>，
    /// 其次清单里的 <c>wallpaper</c> 与 <c>music</c> 字段。
    /// </summary>
    private static void ResolveAssets()
    {
        BackgroundFiles = [];
        MusicFiles = [];
        BackgroundFolder = null;
        MusicFolder = null;
        if (_current is null) return;

        var directory = _current.Directory;
        var backgroundDir = Path.Combine(directory, "assets", "backgrounds");
        if (Directory.Exists(backgroundDir))
        {
            BackgroundFolder = backgroundDir;
            BackgroundFiles = EnumerateMedia(backgroundDir, ThemePackValidator.BackgroundExtensions);
        }

        if (BackgroundFiles.Count == 0 && _current.Manifest.Wallpaper is { Asset.Length: > 0 } wallpaper &&
            ThemePackValidator.TryResolve(directory, wallpaper.Asset, out var wallpaperPath) &&
            File.Exists(wallpaperPath))
            BackgroundFiles = [wallpaperPath];

        var musicDir = Path.Combine(directory, "assets", "music");
        if (Directory.Exists(musicDir))
        {
            MusicFolder = musicDir;
            MusicFiles = EnumerateMedia(musicDir, ThemePackValidator.AudioExtensions);
        }

        if (MusicFiles.Count == 0 && _current.Manifest.Music is { } music)
        {
            var files = new List<string>();
            if (!string.IsNullOrWhiteSpace(music.Folder) &&
                ThemePackValidator.TryResolve(directory, music.Folder, out var folder) && Directory.Exists(folder))
            {
                MusicFolder = folder;
                files.AddRange(EnumerateMedia(folder, ThemePackValidator.AudioExtensions));
            }

            foreach (var relative in music.Files ?? [])
            {
                if (ThemePackValidator.TryResolve(directory, relative, out var file) && File.Exists(file)) files.Add(file);
            }

            MusicFiles = files;
        }
    }

    private static List<string> EnumerateMedia(string folder, string[] extensions) =>
        Directory.EnumerateFiles(folder)
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void ApplyColors()
    {
        try
        {
            // 先让内置算法重新生成全部颜色，再叠加主题包里的覆盖值
            ThemeService.ApplyGrayResources();
            ThemeService.ApplyColorResources();

            var colors = _current?.Manifest.Colors;
            if (colors is null) return;

            var resources = Application.Current.Resources;
            foreach (var (key, value) in colors)
            {
                try
                {
                    if (ColorConverter.ConvertFromString(value) is not Color color) continue;

                    if (key.StartsWith("ColorObject", StringComparison.Ordinal))
                    {
                        resources[key] = color;
                    }
                    else if (key.StartsWith("ColorBrush", StringComparison.Ordinal))
                    {
                        var brush = new SolidColorBrush(color);
                        brush.Freeze();
                        resources[key] = brush;
                    }
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, $"[ThemePack] 应用颜色 {key} 失败");
                }
            }

            // 用户手动指定的标题栏 / 底板 / 内容区域颜色优先于主题包
            ThemeService.ApplySurfaceOverrides();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[ThemePack] 刷新配色失败");
        }
    }

    private static void ApplyFont()
    {
        try
        {
            var resources = Application.Current.Resources;
            var font = _current?.Manifest.Font;
            if (font is null)
            {
                if (_defaultFontFamily is not null) resources["LaunchFontFamily"] = _defaultFontFamily;
                return;
            }

            FontFamily? family = null;
            if (!string.IsNullOrWhiteSpace(font.Family))
            {
                family = new FontFamily(font.Family);
            }
            else if (!string.IsNullOrWhiteSpace(font.Asset) &&
                     ThemePackValidator.TryResolve(_current!.Directory, font.Asset, out var assetPath) &&
                     File.Exists(assetPath))
            {
                family = LoadFontFile(assetPath);
            }

            resources["LaunchFontFamily"] = family ?? _defaultFontFamily ?? new FontFamily("Microsoft YaHei UI");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[ThemePack] 应用字体失败");
        }
    }

    private static FontFamily? LoadFontFile(string path)
    {
        try
        {
            var typeface = new GlyphTypeface(new Uri(path));
            var name = typeface.FamilyNames.Values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(name)) return null;
            var baseUri = new Uri(Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar, UriKind.Absolute);
            return new FontFamily(baseUri, "./#" + name);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[ThemePack] 加载字体文件失败：{path}");
            return null;
        }
    }

    private static void ApplyStyles()
    {
        try
        {
            var resources = Application.Current.Resources;
            foreach (var dictionary in AppliedStyles)
            {
                try
                {
                    resources.MergedDictionaries.Remove(dictionary);
                }
                catch (Exception)
                {
                    // 已被移除的字典忽略即可
                }
            }

            AppliedStyles.Clear();

            var styles = _current?.Manifest.Styles;
            if (styles is null) return;

            foreach (var relative in styles)
            {
                if (!ThemePackValidator.TryResolve(_current!.Directory, relative, out var fullPath)) continue;
                if (!ThemeStyleGuard.TryLoad(fullPath, out var dictionary, out var issues) || dictionary is null)
                {
                    foreach (var issue in issues) ModBase.Log($"[ThemePack] 样式被拒绝：{issue}");
                    continue;
                }

                resources.MergedDictionaries.Add(dictionary);
                AppliedStyles.Add(dictionary);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[ThemePack] 应用样式失败");
        }
    }

    private static void ResolveWallpaper()
    {
        WallpaperPath = null;
        var wallpaper = _current?.Manifest.Wallpaper;
        if (wallpaper is null) return;
        if (string.IsNullOrWhiteSpace(wallpaper.Asset)) return;
        if (!ThemePackValidator.TryResolve(_current!.Directory, wallpaper.Asset, out var fullPath)) return;
        if (!File.Exists(fullPath)) return;
        WallpaperPath = fullPath;
    }

    private static void ApplyLayoutRecursive(DependencyObject root)
    {
        if (root is FrameworkElement page and (MyPageRight or MyPageLeft))
            ApplyLayout(page);

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++) ApplyLayoutRecursive(VisualTreeHelper.GetChild(root, i));
    }

    /// <summary>
    /// 应用单个布局属性覆盖。
    /// </summary>
    private static void ApplyLayoutProperty(FrameworkElement element, string property, string value)
    {
        try
        {
            switch (property.Trim().ToLowerInvariant())
            {
                case "margin":
                    element.Margin = (Thickness)new ThicknessConverter().ConvertFromInvariantString(value)!;
                    break;
                case "padding":
                    var padding = (Thickness)new ThicknessConverter().ConvertFromInvariantString(value)!;
                    switch (element)
                    {
                        case Control control: control.Padding = padding; break;
                        case Border border: border.Padding = padding; break;
                    }

                    break;
                case "width":
                    element.Width = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "height":
                    element.Height = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "minwidth":
                    element.MinWidth = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "minheight":
                    element.MinHeight = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "maxwidth":
                    element.MaxWidth = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "maxheight":
                    element.MaxHeight = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "horizontalalignment":
                    element.HorizontalAlignment = Enum.Parse<HorizontalAlignment>(value, true);
                    break;
                case "verticalalignment":
                    element.VerticalAlignment = Enum.Parse<VerticalAlignment>(value, true);
                    break;
                case "opacity":
                    element.Opacity = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "visibility":
                    element.Visibility = Enum.Parse<Visibility>(value, true);
                    break;
                case "fontsize":
                    var fontSize = double.Parse(value, CultureInfo.InvariantCulture);
                    switch (element)
                    {
                        case Control control: control.FontSize = fontSize; break;
                        case TextBlock textBlock: textBlock.FontSize = fontSize; break;
                    }

                    break;
                case "cornerradius":
                    if (element is Border cornerBorder)
                        cornerBorder.CornerRadius = (CornerRadius)new CornerRadiusConverter().ConvertFromInvariantString(value)!;
                    break;
                case "background":
                case "borderbrush":
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)!);
                    brush.Freeze();
                    var isBackground = property.Trim().Equals("background", StringComparison.OrdinalIgnoreCase);
                    switch (element)
                    {
                        case Control control:
                            if (isBackground) control.Background = brush;
                            else control.BorderBrush = brush;
                            break;
                        case Panel panel:
                            if (isBackground) panel.Background = brush;
                            break;
                        case Border border:
                            if (isBackground) border.Background = brush;
                            else border.BorderBrush = brush;
                            break;
                    }

                    break;
                default:
                    ModBase.Log($"[ThemePack] 忽略未知布局属性：{property}");
                    break;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[ThemePack] 应用布局属性失败：{element.Name}.{property}={value}");
        }
    }

    /// <summary>
    /// 应用主窗口尺寸覆盖。
    /// </summary>
    private static void ApplyWindow()
    {
        var window = _current?.Manifest.Layout?.Window;
        if (window is null || ModMain.frmMain is null) return;

        try
        {
            if (window.Width is { } width) ModMain.frmMain.Width = width;
            if (window.Height is { } height) ModMain.frmMain.Height = height;
            if (window.MinWidth is { } minWidth) ModMain.frmMain.MinWidth = minWidth;
            if (window.MinHeight is { } minHeight) ModMain.frmMain.MinHeight = minHeight;
            ModBase.Log($"[ThemePack] 窗口尺寸已覆盖：{ModMain.frmMain.Width}x{ModMain.frmMain.Height}");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[ThemePack] 应用窗口尺寸失败");
        }
    }

    private static void ApplyLayout(FrameworkElement page)
    {
        var pages = _current?.Manifest.Layout?.Pages;
        if (pages is null) return;
        if (!pages.TryGetValue(page.GetType().Name, out var layout)) return;

        try
        {
            foreach (var name in layout.Hidden ?? [])
            {
                var element = FindByName(page, name);
                if (element is not null) element.Visibility = Visibility.Collapsed;
            }

            if (layout.Properties is { } properties)
            {
                var applied = 0;
                foreach (var (name, values) in properties)
                {
                    var element = FindByName(page, name);
                    if (element is null)
                    {
                        ModBase.Log($"[ThemePack] 布局目标不存在：{page.GetType().Name}.{name}");
                        continue;
                    }

                    foreach (var (property, value) in values ?? [])
                    {
                        ApplyLayoutProperty(element, property, value);
                        applied++;
                    }
                }

                ModBase.Log($"[ThemePack] 已对 {page.GetType().Name} 应用 {applied} 项布局属性");
            }

            var order = layout.Order;
            if (order is null || order.Count == 0) return;

            var ordered = new List<FrameworkElement>();
            foreach (var name in order)
            {
                var element = FindByName(page, name);
                if (element is not null && !ordered.Contains(element)) ordered.Add(element);
            }

            foreach (var group in ordered.GroupBy(static element => element.Parent))
            {
                if (group.Key is not Panel panel) continue;
                foreach (var element in group) panel.Children.Remove(element);
                foreach (var element in group) panel.Children.Add(element);
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[ThemePack] 应用页面布局失败：{page.GetType().Name}");
        }
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (root is FrameworkElement self && string.Equals(self.Name, name, StringComparison.Ordinal)) return self;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindByName(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null) return found;
        }

        return null;
    }
}
