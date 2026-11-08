using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;

namespace PCL.Core.UI.Theme.Packs;

/// <summary>
/// 受限 XAML 样式加载器。
/// <para>
/// 主题包里的样式只能使用一组固定的元素与属性，所有标记扩展（<c>{Binding}</c>、<c>{x:Static}</c>、
/// <c>ObjectDataProvider</c> 等）都会被拒绝，避免“下载主题 = 执行任意代码”。
/// 这是一层纵深防御，不是沙箱；主题包仍需可信来源。
/// </para>
/// </summary>
public static class ThemeStyleGuard
{
    private const long MaxFileSize = 512 * 1024;
    private const int MaxElementCount = 20000;

    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "ResourceDictionary", "Style", "Setter", "SetterBaseCollection",
        "SolidColorBrush", "LinearGradientBrush", "RadialGradientBrush", "GradientStop",
        "Color", "FontFamily", "Thickness", "CornerRadius",
        "ControlTemplate", "Border", "Grid", "StackPanel", "WrapPanel", "DockPanel", "Canvas",
        "ContentPresenter", "Rectangle", "Ellipse", "Path", "TextBlock",
        "VisualStateManager", "VisualStateGroup", "VisualState",
        "Storyboard", "DoubleAnimation", "ColorAnimation", "ThicknessAnimation",
        "RotateTransform", "ScaleTransform", "TranslateTransform", "SkewTransform"
    };

    private static readonly HashSet<string> PropertyElements = new(StringComparer.Ordinal)
    {
        "Setters", "Triggers", "Resources", "RowDefinitions", "ColumnDefinitions",
        "Children", "VisualStates", "Background", "BorderBrush", "Content", "Value"
    };

    private static readonly HashSet<string> TypeNamespaces = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "TargetType", "BasedOn", "Property", "Value",
        "Color", "StartPoint", "EndPoint", "Offset", "Opacity",
        "Background", "BorderBrush", "BorderThickness", "CornerRadius",
        "Padding", "Margin", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        "FontSize", "FontWeight", "FontFamily", "FontStyle", "TextAlignment", "TextWrapping",
        "HorizontalAlignment", "VerticalAlignment",
        "HorizontalContentAlignment", "VerticalContentAlignment",
        "Content", "ToolTip", "SnapsToDevicePixels", "UseLayoutRounding",
        "RadiusX", "RadiusY", "StrokeThickness", "Fill", "Stroke", "Data",
        "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
        "Canvas.Left", "Canvas.Top", "DockPanel.Dock", "Panel.ZIndex",
        "IsDefault", "IsCancel", "Focusable", "IsTabStop", "Visibility",
        "Text", "FontStretch", "LineHeight", "ClipToBounds"
    };

    private static readonly Regex MarkupExtensionPattern = new(
        @"^\{\s*(TemplateBinding|DynamicResource|StaticResource|x:Type)\b[^{}]*\}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TypeReferencePattern = new(
        @"^\{\s*x:Type\s+(?<prefix>[A-Za-z_][\w.]*):",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 校验一个样式文件，返回发现的问题。空列表代表通过。
    /// </summary>
    public static IReadOnlyList<ThemePackIssue> ValidateFile(string path)
    {
        var issues = new List<ThemePackIssue>();
        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception ex)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"无法读取样式文件 {path}：{ex.Message}"));
            return issues;
        }

        if (!info.Exists)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件不存在：{path}"));
            return issues;
        }

        if (info.Length > MaxFileSize)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件超过 {MaxFileSize / 1024} KB 上限：{info.Name}"));
            return issues;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(path, LoadOptions.SetLineInfo);
        }
        catch (Exception ex)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件不是合法 XML：{info.Name}（{ex.Message}）"));
            return issues;
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件根元素必须是 ResourceDictionary：{info.Name}"));
            return issues;
        }

        // 收集允许的 clr-namespace 前缀（只允许 PCL 自身的程序集）
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in root.Attributes().Where(static a => a.IsNamespaceDeclaration))
        {
            var prefix = attribute.Name.Namespace == XNamespace.Xmlns ? attribute.Name.LocalName : "";
            if (string.IsNullOrEmpty(prefix)) continue;
            if (attribute.Value.StartsWith("clr-namespace:PCL", StringComparison.Ordinal)) prefixes.Add(prefix);
        }

        var count = 0;
        foreach (var element in root.DescendantsAndSelf())
        {
            if (++count > MaxElementCount)
            {
                issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件元素数量超过 {MaxElementCount} 上限：{info.Name}"));
                return issues;
            }

            var name = element.Name.LocalName;
            if (element.Name.Namespace == XamlNamespace)
            {
                issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件不允许使用 x: 命名空间元素：{name}（第 {((System.Xml.IXmlLineInfo)element).LineNumber} 行）"));
                return issues;
            }

            if (!IsAllowedElementName(name))
            {
                issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件包含不允许的元素 <{name}>（{info.Name} 第 {((System.Xml.IXmlLineInfo)element).LineNumber} 行）"));
                return issues;
            }

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                var attributeName = attribute.Name.LocalName;
                if (attribute.Name.Namespace == XamlNamespace)
                {
                    if (attributeName is "Key") continue;
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件包含不允许的 x: 属性 x:{attributeName}（{info.Name}）"));
                    return issues;
                }

                if (!Attributes.Contains(attributeName))
                {
                    issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件包含不允许的属性 {attributeName}（{info.Name} 第 {((System.Xml.IXmlLineInfo)element).LineNumber} 行）"));
                    return issues;
                }

                var value = attribute.Value;
                if (value.Contains('{'))
                {
                    if (!MarkupExtensionPattern.IsMatch(value))
                    {
                        issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件不允许使用标记扩展 {value}（{info.Name}）"));
                        return issues;
                    }

                    var typeMatch = TypeReferencePattern.Match(value);
                    if (typeMatch.Success && !prefixes.Contains(typeMatch.Groups["prefix"].Value))
                    {
                        issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件引用了未声明的类型前缀 {typeMatch.Groups["prefix"].Value}（{info.Name}）"));
                        return issues;
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// 校验通过后把样式文件加载为 <see cref="ResourceDictionary"/>。
    /// </summary>
    public static bool TryLoad(string path, out ResourceDictionary? dictionary, out IReadOnlyList<ThemePackIssue> issues)
    {
        issues = ValidateFile(path);
        dictionary = null;
        if (issues.Count > 0) return false;

        try
        {
            using var stream = File.OpenRead(path);
            dictionary = XamlReader.Load(stream) as ResourceDictionary;
            if (dictionary is null)
            {
                issues = [new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件没有加载出 ResourceDictionary：{path}")];
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            issues = [new ThemePackIssue(ThemePackIssueLevel.Error, $"样式文件加载失败：{path}（{ex.Message}）")];
            return false;
        }
    }

    private static bool IsAllowedElementName(string name)
    {
        if (Elements.Contains(name)) return true;
        // 属性元素形式，如 Style.Setters、Border.Background
        var dot = name.IndexOf('.');
        return dot > 0 && Elements.Contains(name[..dot]) && PropertyElements.Contains(name[(dot + 1)..]);
    }
}
