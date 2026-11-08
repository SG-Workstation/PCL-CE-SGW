using System;
using System.IO;
using System.Text;
using PCL.Core.Logging;

namespace PCL.Core.UI.Theme.Packs;

/// <summary>
/// 首次启动时向 <c>PCL/Themes</c> 释放默认主题文件与格式说明。只在文件缺失时写入，不覆盖用户内容。
/// </summary>
public static class ThemePackDefaults
{
    private const string Utf8NoBom = "utf-8";

    public const string ReadmeFileName = "README.md";

    public const string TemplateFolderName = "template";

    /// <summary>
    /// 释放默认文件。任何写入失败都只记录日志，不阻断启动。
    /// </summary>
    public static void Seed()
    {
        try
        {
            ThemePackStore.EnsureRoot();
            var readme = Path.Combine(ThemePackStore.Root, ReadmeFileName);
            if (!File.Exists(readme)) Write(readme, ReadmeText);

            var templateFolder = Path.Combine(ThemePackStore.Root, TemplateFolderName);
            Directory.CreateDirectory(templateFolder);
            var template = Path.Combine(templateFolder, "theme.json");
            if (!File.Exists(template)) Write(template, TemplateManifest);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "ThemePack", "释放默认主题文件失败");
        }
    }

    private static void Write(string path, string content)
    {
        var encoding = new UTF8Encoding(false);
        File.WriteAllText(path, content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine), encoding);
    }

    /// <summary>
    /// 主题目录说明文件。
    /// </summary>
    public static string ReadmeText =>
        """
        # 主题包目录

        这是 PCL CE SGW 的主题包目录。启动器首次运行时会在这里释放本说明与 `template/` 示例。

        ## 使用方法

        1. 在此目录下新建一个文件夹，名称即主题 id（只能用小写字母、数字、`.`、`_`、`-`，最长 32 个字符）。
        2. 在文件夹里放一个 `theme.json`（格式见 `template/theme.json`）。
        3. 打开 设置 → 界面 → 自定义主题，重新扫描后选中并应用。

        主题包支持自定义：配色、界面字体、壁纸、背景音乐，以及页面布局（隐藏或调整具名控件的顺序）。
        样式文件（`styles` 字段）使用受限 XAML，只允许一组固定的元素与属性，任何标记扩展都会导致整包拒绝加载。

        ## 背景与音乐

        主题目录里的 `assets/backgrounds/` 存放背景图片或视频（`.png` `.jpg` `.webp` `.gif` `.bmp` `.mp4` `.webm` `.avi` `.mkv` `.mov` `.m4v`）。
        启用该主题时，背景只从该目录随机挑选；目录为空时才回落到 `PCL/Pictures`。

        `assets/music/` 存放背景音乐（`.mp3` `.wav` `.flac` `.ogg` `.m4a` `.aac` `.wma`），会追加到播放列表。
        也可以在 `theme.json` 的 `music.folder` / `music.files` 里显式指定。

        ## 布局覆盖

        `layout.pages` 的键是页面类名（如 `PageSetupUI`、`PageSetupAbout`、`PageHomepage`），值可以是：

        - `hidden`：要隐藏的具名控件（XAML 里的 `x:Name`）；
        - `order`：把列出的具名控件按顺序移到其父容器末尾。只在 `StackPanel`、`WrapPanel` 这类线性容器上改变排列顺序；
          `Grid` 里的位置由附加属性决定，`order` 只会改变绘制层级；
        - `properties`：逐个具名控件覆盖属性，可用 `margin`、`padding`、`width`、`height`、`minWidth`、`minHeight`、
          `maxWidth`、`maxHeight`、`horizontalAlignment`、`verticalAlignment`、`opacity`、`visibility`、`fontSize`、
          `cornerRadius`、`background`、`borderBrush`。

        `layout.window` 可以覆盖主窗口的 `width`、`height`、`minWidth`、`minHeight`。

        页面首次加载时套用，切换主题后会立刻重新套用。

        ## 导出

        设置 → 界面 → 自定义主题里的「导出当前外观」会把当前配色、字体、壁纸与布局打包成一个新的主题包，
        放进本目录，之后可以直接在列表里启用或分享给他人。

        ## 样式文件

        `styles` 里的文件必须是根元素为 `ResourceDictionary` 的 XAML，只允许颜色、画刷、字体、`Thickness`/`CornerRadius`、
        `Style`（仅 `Setter`）与 `ControlTemplate`（只含布局与绘图元素）。`ObjectDataProvider`、`{Binding}`、`{x:Static}`
        等一切可能执行代码的构造都会被拒绝，整包随之拒绝启用。

        ## 目录结构

        ```
        Themes/
        ├─ README.md
        ├─ template/
        │  └─ theme.json
        └─ 你的主题/
           ├─ theme.json             # 必需
           ├─ preview.png            # 可选，列表缩略图
           ├─ assets/
           │  ├─ backgrounds/        # 可选，背景图片/视频
           │  ├─ music/              # 可选，背景音乐
           │  └─ *.ttf               # 可选，字体
           └─ styles/                # 可选，受限 XAML 样式
        ```

        ## 注意

        - 主题只影响外观，不会执行代码；但主题包内容仍来自网络时请自辨来源。
        - 主题包自带许可证（`license` 字段），分发时请一并保留。
        - 删除主题文件夹后重新扫描即可卸载。
        """;

    /// <summary>
    /// 示例清单，内置注释（启动器的解析器允许 <c>//</c> 注释与尾随逗号）。
    /// </summary>
    public static string TemplateManifest =>
        """
        {
          // 清单格式版本，当前固定为 1
          "schema": 1,

          // 主题唯一标识：小写字母、数字、点、下划线、短横线
          "id": "my-theme",

          // 显示名称
          "name": "我的主题",

          "author": "你的名字",
          "version": "1.0.0",
          "description": "一句话描述这个主题",
          "license": "MIT",
          "homepage": "https://example.com",

          // 覆盖界面颜色。键以 ColorObject 开头时按 Color 应用，以 ColorBrush 开头时按 SolidColorBrush 应用。
          // 内置算法会先生成全部颜色，这里的值最后覆盖。
          "colors": {
            "ColorBrushBackground": "#F2F6FF",
            "ColorBrush1": "#2B3A4D",
            "ColorBrush2": "#123F7A",
            "ColorBrush3": "#1E63C8",
            "ColorBrush4": "#4A8BE0"
          },

          // 界面字体：可以用系统已安装的字体族名，也可以指向包内的字体文件
          "font": {
            "family": "Microsoft YaHei UI"
            // "asset": "assets/myfont.ttf"
          },

          // 壁纸：图片或视频；启用主题时背景优先从 assets/backgrounds 读取
          "wallpaper": {
            "asset": "assets/backgrounds/background.png",
            "opacity": 0.85,
            "blur": 6
          },

          // 背景音乐：指定目录或文件列表，会追加到播放列表
          "music": {
            "folder": "assets/music"
          },

          // 页面布局：键为页面类型名；hidden 隐藏控件，order 调整顺序，properties 逐个覆盖控件属性
          "layout": {
            "window": {
              "width": 900,
              "height": 560
            },
            "pages": {
              "PageSetupUI": {
                "hidden": ["CheckShowTriviaHint"],
                "order": ["ComboDarkMode"],
                "properties": {
                  "CardThemePack": { "margin": "0,0,0,20" },
                  "ComboThemePack": { "width": 240, "height": 30 },
                  "CardBackground": { "cornerRadius": "6" }
                }
              }
            }
          },

          // 受限 XAML 样式文件（相对路径），按顺序合并进应用程序资源
          "styles": [
            "styles/controls.xaml"
          ]
        }
        """;
}
