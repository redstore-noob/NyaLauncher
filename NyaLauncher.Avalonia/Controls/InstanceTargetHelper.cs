using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// "下载到实例"下拉框的通用助手：构建实例标签列表（含"（当前）"标记 + 末尾"自定义保存路径"）、
/// 默认选中当前实例、从选中项解析真实实例 id、解析实例内容目录。
/// 供 ModDownloadOverlay / ContentDownloadOverlay 复用。
/// </summary>
internal static class InstanceTargetHelper
{
    public const string CustomPathOption = "自定义保存路径…";

    /// <summary>构建下拉项列表（实例标签 + 自定义保存路径）。</summary>
    public static List<string> BuildItems()
    {
        var snapshot = GameInstanceStore.Current;
        var items = new List<string>();
        foreach (var versionId in snapshot.VersionIds)
        {
            if (string.IsNullOrWhiteSpace(versionId))
                continue;
            // 已安装版本 id 形如 "1.21.8" / "fabric-loader-0.16.14-1.21.8"
            var label = string.Equals(versionId, snapshot.SelectedVersionId, StringComparison.Ordinal)
                ? $"{versionId}（当前）"
                : versionId;
            items.Add(label);
        }
        items.Add(CustomPathOption);
        return items;
    }

    /// <summary>默认选中当前实例；无任何实例时选中最后一项（自定义保存路径）。</summary>
    public static void SelectDefault(ComboBox comboBox, List<string> items)
    {
        if (items.Count == 0)
        {
            comboBox.SelectedIndex = -1;
            return;
        }
        var snapshot = GameInstanceStore.Current;
        if (snapshot.VersionIds.Count == 0)
        {
            comboBox.SelectedIndex = items.Count - 1;
            return;
        }
        comboBox.SelectedIndex = snapshot.SelectedVersionId is null
            ? 0
            : Math.Max(0, snapshot.VersionIds.ToList().IndexOf(snapshot.SelectedVersionId));
    }

    /// <summary>从下拉选中项解析真实实例 id（去掉"（当前）"后缀）；自定义路径/空返回空串。</summary>
    public static string GetSelectedInstanceId(ComboBox comboBox)
    {
        var selection = comboBox.SelectedItem as string;
        if (string.IsNullOrEmpty(selection) || selection == CustomPathOption)
            return string.Empty;
        var versionId = selection.EndsWith("（当前）", StringComparison.Ordinal)
            ? selection[..^4]
            : selection;
        var snapshot = GameInstanceStore.Current;
        return snapshot.VersionIds.FirstOrDefault(id =>
            string.Equals(id, versionId, StringComparison.Ordinal)) ?? versionId;
    }

    /// <summary>解析实例内容目录；实例 id 为空或目录不可用时返回空串。</summary>
    public static string ResolveContentDir(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return string.Empty;
        var snapshot = GameInstanceStore.Current;
        return ContentInstallService.ResolveContentDirectory(
            snapshot.MinecraftDirectory, snapshot.SourcePath, versionId) ?? string.Empty;
    }
}
