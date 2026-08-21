using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Dialogs;

/// <summary>
/// 下载选项的返回结果。
/// </summary>
public sealed record DownloadOptions(
    ModLoaderType LoaderType,
    ModLoaderVersion? LoaderVersion,
    string InstanceName);

public partial class DownloadOptionsDialog : Window
{
    private readonly MinecraftVersion _version;
    private readonly List<ModLoaderVersion> _loaderVersions = [];
    private CancellationTokenSource? _loadingCts;

    public DownloadOptionsDialog(MinecraftVersion version)
    {
        _version = version;
        InitializeComponent();
        VersionLabel.Text = $"{version.DisplayName}（{version.TypeDisplay}）";

        var defaultName = version.Id;
        InstanceNameBox.Text = defaultName;
        InstanceNameHint.Text = $"版本将安装至 versions/{defaultName}/";
    }

    // ------------------------------------------------------------------
    // Loader 类型切换
    // ------------------------------------------------------------------

    private async void OnLoaderTypeChanged(object? sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        var loaderType = GetSelectedLoaderType();

        if (loaderType == ModLoaderType.Vanilla)
        {
            LoaderVersionSection.IsVisible = false;
            InstanceNameBox.Text = _version.Id;
            InstanceNameHint.Text = $"版本将安装至 versions/{_version.Id}/";
            return;
        }

        LoaderVersionSection.IsVisible = true;
        LoaderVersionComboBox.ItemsSource = null;
        LoaderVersionComboBox.PlaceholderText = "正在加载版本列表…";
        LoaderVersionComboBox.IsEnabled = false;
        LoaderVersionHint.Text = $"正在从 {loaderType} 官方源获取可用版本…";
        StatusText.IsVisible = false;

        _loadingCts?.Cancel();
        _loadingCts = new CancellationTokenSource();
        var ct = _loadingCts.Token;

        try
        {
            var versions = await ModLoaderMetadata.GetVersionsAsync(
                loaderType, _version.Id, ct);

            if (ct.IsCancellationRequested)
                return;

            _loaderVersions.Clear();
            _loaderVersions.AddRange(versions);

            LoaderVersionComboBox.ItemsSource = _loaderVersions
                .Select(v => v.DisplayName)
                .ToList();
            LoaderVersionComboBox.IsEnabled = true;

            if (_loaderVersions.Count > 0)
            {
                // 优先选中稳定版
                var preferred = _loaderVersions.FindIndex(v => v.IsStable);
                LoaderVersionComboBox.SelectedIndex = preferred >= 0 ? preferred : 0;
            }
            else
            {
                LoaderVersionComboBox.PlaceholderText = "无可用版本";
                LoaderVersionHint.Text = $"该 Minecraft 版本暂无可用的 {loaderType} 版本。";
            }
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LoaderVersionComboBox.PlaceholderText = "加载失败";
            LoaderVersionHint.Text = $"获取版本列表失败：{ex.Message}";
            LoaderVersionComboBox.IsEnabled = false;
        }
    }

    private void OnLoaderVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LoaderVersionComboBox.SelectedIndex < 0 ||
            LoaderVersionComboBox.SelectedIndex >= _loaderVersions.Count)
            return;

        var selected = _loaderVersions[LoaderVersionComboBox.SelectedIndex];
        LoaderVersionHint.Text = selected.IsStable
            ? $"推荐版本 · 安装至 versions/{BuildInstanceName(selected)}/"
            : $"非稳定版本 · 安装至 versions/{BuildInstanceName(selected)}/";

        InstanceNameBox.Text = BuildInstanceName(selected);
        InstanceNameHint.Text = $"版本将安装至 versions/{BuildInstanceName(selected)}/";
    }

    // ------------------------------------------------------------------
    // 按钮事件
    // ------------------------------------------------------------------

    private void OnCancelClick(object? sender, RoutedEventArgs e) =>
        Close(null);

    private void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var loaderType = GetSelectedLoaderType();
        ModLoaderVersion? loaderVersion = null;

        if (loaderType != ModLoaderType.Vanilla)
        {
            if (LoaderVersionComboBox.SelectedIndex < 0 ||
                LoaderVersionComboBox.SelectedIndex >= _loaderVersions.Count)
            {
                StatusText.Text = "请选择加载器版本。";
                StatusText.IsVisible = true;
                return;
            }

            loaderVersion = _loaderVersions[LoaderVersionComboBox.SelectedIndex];
        }

        var instanceName = string.IsNullOrWhiteSpace(InstanceNameBox.Text)
            ? (loaderVersion is not null
                ? BuildInstanceName(loaderVersion)
                : _version.Id)
            : InstanceNameBox.Text.Trim();

        // 校验实例名（不允许路径分隔符和特殊字符）
        if (instanceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusText.Text = "实例名称包含非法字符。";
            StatusText.IsVisible = true;
            return;
        }

        Close(new DownloadOptions(loaderType, loaderVersion, instanceName));
    }

    // ------------------------------------------------------------------
    // 辅助方法
    // ------------------------------------------------------------------

    private ModLoaderType GetSelectedLoaderType()
    {
        if (RadioFabric.IsChecked == true) return ModLoaderType.Fabric;
        if (RadioNeoForge.IsChecked == true) return ModLoaderType.NeoForge;
        if (RadioForge.IsChecked == true) return ModLoaderType.Forge;
        return ModLoaderType.Vanilla;
    }

    private string BuildInstanceName(ModLoaderVersion loader) =>
        ModLoaderInstaller.CreateDefaultInstanceName(
            loader.Type, loader.LoaderVersion, _version.Id);
}
