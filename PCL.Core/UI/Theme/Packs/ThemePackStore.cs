using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Core.App;
using PCL.Core.Logging;
using PCL.Core.Utils;

namespace PCL.Core.UI.Theme.Packs;

/// <summary>
/// 主题包仓库，根目录固定为便携目录下的 <c>PCL/Themes</c>。
/// </summary>
public static class ThemePackStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonCompat.SerializerOptions)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 主题包根目录。
    /// </summary>
    public static string Root => Path.Combine(Paths.Data, "Themes");

    /// <summary>
    /// 由启动器释放、不应当作可安装主题的目录名。
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ThemePackDefaults.TemplateFolderName
    };

    /// <summary>
    /// 确保主题目录存在。
    /// </summary>
    public static void EnsureRoot()
    {
        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// 枚举已安装主题的 id。
    /// </summary>
    public static IReadOnlyList<string> EnumerateIds()
    {
        EnsureRoot();
        return Directory.EnumerateDirectories(Root)
            .Where(static directory => File.Exists(Path.Combine(directory, "theme.json")))
            .Select(Path.GetFileName)
            .Where(static id => ThemePackValidator.IsValidId(id) && !ReservedNames.Contains(id!))
            .Select(static id => id!)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 读取并校验一个主题包。
    /// </summary>
    public static ThemePack? Load(string id)
    {
        if (!ThemePackValidator.IsValidId(id)) return null;

        var directory = Path.Combine(Root, id);
        var manifestPath = Path.Combine(directory, "theme.json");
        if (!File.Exists(manifestPath)) return null;

        ThemePackManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ThemePackManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "ThemePack", $"读取主题清单失败：{manifestPath}");
            return new ThemePack(
                new ThemePackManifest { Id = id, Name = id },
                directory,
                [new ThemePackIssue(ThemePackIssueLevel.Error, $"theme.json 解析失败：{ex.Message}")]);
        }

        if (manifest is null)
        {
            return new ThemePack(
                new ThemePackManifest { Id = id, Name = id },
                directory,
                [new ThemePackIssue(ThemePackIssueLevel.Error, "theme.json 内容为空")]);
        }

        var issues = ThemePackValidator.Validate(manifest, directory).ToList();
        if (!string.IsNullOrWhiteSpace(manifest.Id) &&
            !string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Warning,
                $"清单 id（{manifest.Id}）与目录名（{id}）不一致，以目录名为准"));
        }

        return new ThemePack(manifest, directory, issues);
    }

    /// <summary>
    /// 从任意目录安装主题包。目标 id 默认取清单里的 id。
    /// </summary>
    public static IReadOnlyList<ThemePackIssue> InstallFromFolder(string sourceDirectory, string? targetId = null)
    {
        var issues = new List<ThemePackIssue>();
        if (!Directory.Exists(sourceDirectory))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"源目录不存在：{sourceDirectory}"));
            return issues;
        }

        var manifestPath = Path.Combine(sourceDirectory, "theme.json");
        if (!File.Exists(manifestPath))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, "源目录里没有 theme.json"));
            return issues;
        }

        ThemePackManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ThemePackManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (Exception ex)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"theme.json 解析失败：{ex.Message}"));
            return issues;
        }

        if (manifest is null)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, "theme.json 内容为空"));
            return issues;
        }

        var id = string.IsNullOrWhiteSpace(targetId) ? manifest.Id : targetId;
        if (!ThemePackValidator.IsValidId(id))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"主题 id 非法：{id}"));
            return issues;
        }

        var validation = ThemePackValidator.Validate(manifest, sourceDirectory);
        issues.AddRange(validation);
        if (validation.Any(static issue => issue.Level == ThemePackIssueLevel.Error)) return issues;

        EnsureRoot();
        var destination = Path.Combine(Root, id);
        if (Directory.Exists(destination))
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"目标主题已存在：{id}，请先删除旧版本"));
            return issues;
        }

        try
        {
            CopyDirectory(sourceDirectory, destination);
        }
        catch (Exception ex)
        {
            issues.Add(new ThemePackIssue(ThemePackIssueLevel.Error, $"复制主题失败：{ex.Message}"));
            TryDelete(destination);
        }

        return issues;
    }

    /// <summary>
    /// 删除一个已安装主题。
    /// </summary>
    public static bool Delete(string id)
    {
        if (!ThemePackValidator.IsValidId(id)) return false;
        var directory = Path.Combine(Root, id);
        if (!Directory.Exists(directory)) return false;
        try
        {
            Directory.Delete(directory, true);
            return true;
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "ThemePack", $"删除主题失败：{directory}");
            return false;
        }
    }

    /// <summary>
    /// 读取主题包的清单原始 JSON，用于展示与诊断。
    /// </summary>
    public static JsonNode? ReadRawManifest(string id)
    {
        var path = Path.Combine(Root, id, "theme.json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (Exception)
        {
            // 清理失败不影响主流程
        }
    }
}
