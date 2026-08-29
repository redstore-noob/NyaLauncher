using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 下载选项的返回结果。
/// </summary>
public sealed record DownloadOptions(
    MinecraftVersion Version,
    ModLoaderType LoaderType,
    ModLoaderVersion? LoaderVersion,
    string InstanceName,
    bool SkipFabricApi);

/// <summary>
/// Minecraft 本体下载内容视图：由 <see cref="ModalOverlayHost"/> 承载，
/// 选择 Loader 类型 / 版本 / 自定义实例名后通过 <c>Host.Close(new DownloadOptions(...))</c>
/// 把结果交回调用方。
/// </summary>
public partial class MinecraftDownloadOverlay : UserControl, IModalHostAware
{
    private readonly List<ModLoaderVersion> _loaderVersions = [];
    private CancellationTokenSource? _loadingCts;
    private MinecraftVersion? _version;

    /// <summary>承载本视图的宿主（由 ModalOverlayHost.Show 自动注入）。</summary>
    public ModalOverlayHost? Host { get; set; }

    public MinecraftDownloadOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 宿主展示前调用：设置版本信息并复位表单状态。
    /// </summary>
    public void Setup(MinecraftVersion version)
    {
        _version = version;
        ResetState();
        Header.Subtitle = $"{version.DisplayName}（{version.TypeDisplay}）";
    }

    private void ResetState()
    {
        _loaderVersions.Clear();
        LoaderVersionSection.IsVisible = false;
        LoaderVersionComboBox.ItemsSource = null;
        LoaderVersionHint.Text = string.Empty;
        SkipFabricApiCheckBox.IsVisible = false;
        SkipFabricApiCheckBox.IsChecked = false;
        StatusText.IsVisible = false;
        StatusText.Text = string.Empty;

        if (_version is not null)
        {
            InstanceNameBox.Text = _version.Id;
            InstanceNameHint.Text = $"版本将安装至 versions/{_version.Id}/";
        }

        RadioVanilla.IsChecked = true;
    }

    // ------------------------------------------------------------------
    // Loader 类型切换
    // ------------------------------------------------------------------

    private async void OnLoaderTypeChanged(object? sender, RoutedEventArgs e)
    {
        if (_version is null || !IsLoaded)
            return;

        var loaderType = GetSelectedLoaderType();

        // Fabric API 选项仅在选择 Fabric 时显示
        SkipFabricApiCheckBox.IsVisible = loaderType == ModLoaderType.Fabric;

        if (loaderType == ModLoaderType.Vanilla)
        {
            _loadingCts?.Cancel();
            LoaderVersionSection.IsVisible = false;
            InstanceNameBox.Text = _version.Id;
            InstanceNameHint.Text = $"版本将安装至 versions/{_version.Id}/";
            return;
        }

        LoaderVersionSection.IsVisible = true;
        LoaderVersionComboBox.ItemsSource = null;
        LoaderVersionComboBox.PlaceholderText = "正在加载版本列表…";
        LoaderVersionComboBox.IsEnabled = false;
        LoaderVersionHint.Text = $"正在从 {loaderType} 源获取可用版本…";
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

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _loadingCts?.Cancel();
        Host?.Close();
    }

    private void OnHeaderClose(object? sender, EventArgs e) => OnCancelClick(sender, null);

    private void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_version is null)
            return;

        var loaderType = GetSelectedLoaderType();
        ModLoaderVersion? loaderVersion = null;

        if (loaderType != ModLoaderType.Vanilla)
        {
            if (LoaderVersionComboBox.SelectedIndex < 0 ||
                LoaderVersionComboBox.SelectedIndex >= _loaderVersions.Count)
            {
                ShowStatus("请选择加载器版本。");
                return;
            }

            loaderVersion = _loaderVersions[LoaderVersionComboBox.SelectedIndex];
        }

        var instanceName = string.IsNullOrWhiteSpace(InstanceNameBox.Text)
            ? (loaderVersion is not null
                ? BuildInstanceName(loaderVersion)
                : _version.Id)
            : InstanceNameBox.Text.Trim();

        // 校验实例名：拒绝路径分隔符、特殊字符、"." 与 ".."（防止路径穿越）
        if (!OverlayHelpers.IsValidInstanceName(instanceName, out var nameErr))
        {
            ShowStatus(nameErr ?? "实例名称非法。");
            return;
        }

        _loadingCts?.Cancel();
        Host?.Close(new DownloadOptions(
            _version, loaderType, loaderVersion, instanceName,
            SkipFabricApiCheckBox.IsChecked == true));
    }

    private void ShowStatus(string message) => OverlayHelpers.SetStatus(StatusText, message, isError: true);

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
            loader.Type, loader.LoaderVersion, _version!.Id);
}
