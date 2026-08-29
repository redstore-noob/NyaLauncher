using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Content;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Tools;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Pages;

public partial class VersionManagerPage : UserControl
{
    private bool _synchronizingFolders;
    private bool _synchronizingVersions;
    private bool _synchronizingMemorySliders;
    private bool _synchronizingAdvancedSettings = true;
    private bool _synchronizingIconCombo;
    private CancellationTokenSource? _detailsCancellation;
    private CancellationTokenSource? _instanceVisualCancellation;
    private GameVersionDetails? _currentDetails;
    private int _draftWindowWidth = 854;
    private int _draftWindowHeight = 480;
    private string _draftJavaExecutable = string.Empty;
    private string[] _draftJvmArguments = [];
    private string[] _draftGameArguments = [];

    public VersionManagerPage()
    {
        InitializeComponent();
        InstanceIconCombo.ItemsSource = _iconChoices;
        GameInstanceStore.Changed += OnInstancesChanged;
        GameVersionProfileStore.Changed += OnProfilesChanged;
        ModsList.AddHandler(ContentEntryItem.ModFileChangedEvent, OnModFileChanged);
        SavesList.AddHandler(SaveEntryItem.SaveChangedEvent, OnSaveChanged);
        ReloadFolders();
    }

    public void Activate()
    {
        ReloadFolders();
        var path = LauncherConfig.GameDirectory ??
                   Environment.GetEnvironmentVariable("NYALAUNCHER_MINECRAFT_DIR") ??
                   MinecraftDirectoryLocator.EnsureDefaultDirectory();
        _ = GameInstanceStore.RefreshAsync(path);
    }

    private void ReloadFolders()
    {
        var folders = GameVersionProfileStore.GetFolders().ToList();
        var configured = LauncherConfig.GameDirectory;
        _synchronizingFolders = true;
        try
        {
            FolderSelector.ItemsSource = folders;
            FolderSelector.SelectedItem = FindPath(folders, configured) ??
                                          FindPath(folders, GameInstanceStore.Current.SourcePath);
        }
        finally
        {
            _synchronizingFolders = false;
        }
    }

    private async void OnFolderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingFolders || FolderSelector.SelectedItem is not string path)
            return;
        LauncherConfig.SaveGameDirectory(path);
        await GameInstanceStore.RefreshAsync(path);
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        var path = FolderSelector.SelectedItem as string ?? LauncherConfig.GameDirectory;
        if (string.IsNullOrWhiteSpace(path))
            return;
        await GameInstanceStore.RefreshAsync(path);
    }

    private void OnInstancesChanged(GameInstanceSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnInstancesChanged(snapshot));
            return;
        }

        if (snapshot.IsLoading)
        {
            _instanceVisualCancellation?.Cancel();
            _detailsCancellation?.Cancel();
            ShowEmptyDetails();
            StatusText.Text = string.Empty;
            return;
        }
        if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
        {
            _instanceVisualCancellation?.Cancel();
            VersionList.ItemsSource = null;
            ShowEmptyDetails();
            StatusText.Text = $"文件夹扫描失败：{snapshot.ErrorMessage}";
            return;
        }

        var folders = FolderSelector.ItemsSource?.Cast<string>().ToList() ?? [];
        var matchingFolder = FindPath(folders, snapshot.SourcePath);
        if (matchingFolder is not null)
        {
            _synchronizingFolders = true;
            FolderSelector.SelectedItem = matchingFolder;
            _synchronizingFolders = false;
        }

        var entries = snapshot.VersionIds
            .Select(id => CreateListItem(snapshot, id))
            .ToArray();
        _synchronizingVersions = true;
        try
        {
            VersionList.ItemsSource = entries;
            VersionList.SelectedItem = entries.FirstOrDefault(entry => string.Equals(
                entry.VersionId,
                snapshot.SelectedVersionId,
                StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _synchronizingVersions = false;
        }

        InstanceCountText.Text = $"{entries.Length} 个实例";
        StatusText.Text = entries.Length == 0
            ? "该文件夹中没有完整的实例版本。"
            : $"已读取 {entries.Length} 个实例 · {snapshot.MinecraftDirectory}";
        if (VersionList.SelectedItem is VersionListItem selected)
            _ = LoadDetailsAsync(snapshot, selected.VersionId);
        else
            ShowEmptyDetails();
        _ = EnrichInstanceVisualsAsync(snapshot, entries);
    }

    private void OnProfilesChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnProfilesChanged);
            return;
        }

        var snapshot = GameInstanceStore.Current;
        if (!snapshot.IsLoading && snapshot.ErrorMessage is null)
            OnInstancesChanged(snapshot);
    }

    private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingVersions || VersionList.SelectedItem is not VersionListItem selected)
            return;
        GameInstanceStore.Select(selected.VersionId);
        _ = LoadDetailsAsync(GameInstanceStore.Current, selected.VersionId);
    }

    private async Task LoadDetailsAsync(GameInstanceSnapshot snapshot, string versionId)
    {
        _detailsCancellation?.Cancel();
        _detailsCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _detailsCancellation = cancellation;
        StatusText.Text = $"正在读取 {versionId} 的版本详情…";
        try
        {
            var details = await GameVersionDetailsService.LoadAsync(
                snapshot,
                versionId,
                cancellation.Token);
            if (!ReferenceEquals(_detailsCancellation, cancellation) ||
                !IsCurrentSelection(snapshot, versionId))
                return;
            _currentDetails = details;
            DisplayDetails(snapshot, details);
            StatusText.Text = $"已选择 {details.VersionId} · {details.LoaderName}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_detailsCancellation, cancellation))
            {
                ShowEmptyDetails();
                StatusText.Text = $"版本详情读取失败：{exception.Message}";
            }
        }
    }

    private void DisplayDetails(GameInstanceSnapshot snapshot, GameVersionDetails details)
    {
        var profile = GameVersionProfileStore.Get(snapshot.MinecraftDirectory, details.VersionId);
        EmptyDetailsView.IsVisible = false;
        DetailsView.IsVisible = true;
        DetailsTitleText.Text = details.VersionId;
        var loaderDisplay = details.IsVanilla
            ? "原版"
            : $"{details.LoaderName} {details.LoaderVersion}";
        DetailsSubtitleText.Text =
            $"Minecraft {details.BaseGameVersion} · {loaderDisplay} · " +
                                   (details.IsIsolated ? "版本隔离" : "共享目录");
        // 实例回退字形："material:Kind" 渲染为 Material 图标，其余回退文字（原字号 23）
        DetailsIconGlyph.Content = FeatureIconFactory.CreateGlyph(details.InstanceIconGlyph, 23);
        DetailsIconImage.SourceUrl = details.InstanceIconPath;
        VersionIdText.Text = details.VersionId;
        BaseGameVersionText.Text = details.BaseGameVersion;
        VersionTypeText.Text = details.VersionType;
        LoaderText.Text = details.LoaderName;
        LoaderVersionText.Text = details.LoaderVersion;
        IsolationText.Text = details.IsIsolated ? "已开启" : "未开启";
        LayoutProviderText.Text = $"{details.LayoutProvider} · {details.LayoutEvidence}";
        ContentDirectoryText.Text = details.ContentDirectory;
        ReleaseTimeText.Text = details.ReleaseTime;
        JavaRequirementText.Text = details.JavaRequirement;
        MainClassText.Text = details.MainClass;

        VersionNameBox.Text = details.VersionId;
        VersionNameBox.IsEnabled = !details.IsExternallyManaged;
        VersionIsolationCheckBox.IsChecked = details.IsIsolated;
        VersionIsolationCheckBox.IsEnabled = !details.IsExternallyManaged;
        ConfigureMemorySliders(profile);
        ConfigureAdvancedSettings(profile);

        ModsSummaryText.Text = $"{details.Mods.Count} 个模组 · {loaderDisplay}";
        ModLoaderHint.IsVisible = details.IsVanilla;
        ModsList.ItemsSource = details.Mods;
        ResourcePacksSummaryText.Text = $"{details.ResourcePacks.Count} 个资源包";
        ResourcePacksList.ItemsSource = details.ResourcePacks;
        ShadersTab.IsVisible = details.HasShaderDirectory;
        ShadersSummaryText.Text = $"{details.Shaders.Count} 个光影包";
        ShadersList.ItemsSource = details.Shaders;
        SavesSummaryText.Text = $"{details.Saves.Count} 个游戏存档";
        SavesList.ItemsSource = details.Saves;
        SyncIconComboSelection(snapshot, details.VersionId);
    }

    private void ShowEmptyDetails()
    {
        _currentDetails = null;
        EmptyDetailsView.IsVisible = true;
        DetailsView.IsVisible = false;
    }

    private async void OnModFileChanged(object? sender, RoutedEventArgs e)
    {
        var snapshot = GameInstanceStore.Current;
        var versionId = snapshot.SelectedVersionId;
        if (snapshot.IsLoading ||
            snapshot.ErrorMessage is not null ||
            string.IsNullOrWhiteSpace(versionId) ||
            string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory))
        {
            return;
        }
        await LoadDetailsAsync(snapshot, versionId);
    }

    /// <summary>存档操作（导出/删除/备份）完成后：显示状态并刷新列表。</summary>
    private async void OnSaveChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is SaveEntryItem item && item.PendingOperationStatus is { } status)
            StatusText.Text = status;

        var snapshot = GameInstanceStore.Current;
        var versionId = _currentDetails?.VersionId
                        ?? snapshot.SelectedVersionId;
        if (snapshot.IsLoading ||
            snapshot.ErrorMessage is not null ||
            string.IsNullOrWhiteSpace(versionId) ||
            string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory))
        {
            return;
        }
        await LoadDetailsAsync(snapshot, versionId);
    }

    private async void OnSaveSettingsClick(object? sender, RoutedEventArgs e)
    {
        var snapshot = GameInstanceStore.Current;
        // 捕获到局部变量：await 期间 RefreshAsync 的 loading 事件会把 _currentDetails 置 null，
        // 继续引用字段会 NullReferenceException（async void 未捕获异常会直接崩溃）。
        var details = _currentDetails;
        if (details is null)
        {
            return;
        }
        var followGlobalAdvancedSettings =
            FollowGlobalAdvancedSettingsCheckBox.IsChecked == true;
        if (!followGlobalAdvancedSettings && !CaptureAdvancedDraft())
        {
            StatusText.Text = "请检查窗口尺寸，它们必须是有效整数且至少为 320×240。";
            return;
        }
        var minimumMemory = (int)MinimumMemorySlider.Value;
        var maximumMemory = (int)MaximumMemorySlider.Value;

        var versionId = details.VersionId;

        var profile = new GameVersionProfile
        {
            MinecraftDirectory = snapshot.MinecraftDirectory,
            VersionId = versionId,
            MinimumMemoryMb = minimumMemory,
            MaximumMemoryMb = maximumMemory,
            WindowWidth = _draftWindowWidth,
            WindowHeight = _draftWindowHeight,
            IsVersionIsolationEnabled = details.IsExternallyManaged
                ? null
                : VersionIsolationCheckBox.IsChecked == true,
            UseIndependentMemorySettings = IndependentMemoryCheckBox.IsChecked == true,
            FollowGlobalAdvancedSettings = followGlobalAdvancedSettings,
            JavaExecutable = _draftJavaExecutable,
            AdditionalJvmArguments = _draftJvmArguments,
            AdditionalGameArguments = _draftGameArguments
        };
        if (!GameVersionProfileStore.Save(profile))
        {
            StatusText.Text = "保存失败：最小内存至少为 256 MiB，最大内存不能小于最小内存，窗口至少为 320×240。";
            return;
        }

        DetailsTitleText.Text = profile.VersionId;
        StatusText.Text = $"已保存 {profile.VersionId} 的实例设置。";
        await GameInstanceStore.RefreshAsync(snapshot.SourcePath);
        GameInstanceStore.Select(profile.VersionId);
        await LoadDetailsAsync(GameInstanceStore.Current, profile.VersionId);
    }

    private void OnOpenGameFolderClick(object? sender, RoutedEventArgs e)
    {
        var path = GameInstanceStore.Current.MinecraftDirectory;
        OpenFolder(path, create: false);
    }

    private void OnOpenVersionFolderClick(object? sender, RoutedEventArgs e)
    {
        if (TryResolveCurrentDirectories(out var versionDirectory, out _))
            OpenFolder(versionDirectory, create: false);
    }

    private void OnOpenModsFolderClick(object? sender, RoutedEventArgs e)
    {
        if (TryResolveCurrentDirectories(out _, out var contentDirectory))
            OpenFolder(Path.Combine(contentDirectory, "mods"), create: true);
    }

    private void OnOpenSavesFolderClick(object? sender, RoutedEventArgs e)
    {
        if (TryResolveCurrentDirectories(out _, out var contentDirectory))
            OpenFolder(Path.Combine(contentDirectory, "saves"), create: true);
    }

    private void OpenFolder(string? path, bool create)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (create)
                Directory.CreateDirectory(path);
            if (!Directory.Exists(path))
            {
                StatusText.Text = $"文件夹不存在：{path}";
                return;
            }

            var startInfo = OperatingSystem.IsWindows()
                ? CreateShellStartInfo("explorer.exe", path)
                : OperatingSystem.IsMacOS()
                ? CreateShellStartInfo("open", path)
                : CreateShellStartInfo("xdg-open", path);
            Process.Start(startInfo);
            StatusText.Text = $"已打开：{path}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"打开文件夹失败：{exception.Message}";
        }
    }

    // ------------------------------------------------------------------
    // 「编辑实例」标签页：重命名 / 导出（占位） / 删除
    // ------------------------------------------------------------------

    /// <summary>按「编辑实例」标签页中的名称输入框重命名当前实例。</summary>
    private async void OnRenameInstanceClick(object? sender, RoutedEventArgs e)
    {
        var snapshot = GameInstanceStore.Current;
        // 捕获局部变量：await 期间刷新事件可能把 _currentDetails 置 null
        var details = _currentDetails;
        if (details is null)
            return;

        var versionId = details.VersionId;
        var requestedVersionId = VersionNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requestedVersionId))
        {
            NyaAlert.Warning("实例名称不能为空。");
            return;
        }
        if (string.Equals(versionId, requestedVersionId, StringComparison.Ordinal))
        {
            StatusText.Text = "名称未发生变化。";
            return;
        }
        if (details.IsExternallyManaged)
        {
            NyaAlert.Warning("外部启动器实例的物理重命名需要由原启动器完成。");
            return;
        }

        StatusText.Text = $"正在将 {versionId} 重命名为 {requestedVersionId}…";
        try
        {
            var newVersionId = await GameVersionRenameService.RenameAsync(
                snapshot.MinecraftDirectory,
                versionId,
                requestedVersionId);
            var sourcePath = PathUtil.PathsEqual(snapshot.SourcePath, details.VersionDirectory)
                ? Path.Combine(snapshot.MinecraftDirectory, "versions", newVersionId)
                : snapshot.SourcePath;
            await GameInstanceStore.RefreshAsync(sourcePath);
            GameInstanceStore.Select(newVersionId);
            NyaAlert.Success($"已将 {versionId} 重命名为 {newVersionId}。");
        }
        catch (Exception exception)
        {
            NyaAlert.Error($"实例重命名失败：{exception.Message}");
        }
    }

    /// <summary>删除当前选中的实例（内部弹出确认对话框）。</summary>
    private void OnDeleteInstanceClick(object? sender, RoutedEventArgs e)
    {
        var versionId = GameInstanceStore.Current.SelectedVersionId;
        if (string.IsNullOrWhiteSpace(versionId))
        {
            StatusText.Text = "当前没有选中的实例。";
            return;
        }
        RequestDelete(versionId);
    }

    /// <summary>图标选择下拉框选中项发生变化。</summary>
    private async void OnInstanceIconComboChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingIconCombo)
            return;

        var versionId = _currentDetails?.VersionId;
        var snapshot = GameInstanceStore.Current;
        if (string.IsNullOrWhiteSpace(versionId) ||
            string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory))
            return;

        if (InstanceIconCombo.SelectedItem is not IconChoice choice)
            return;

        if (choice == CustomIconEntry)
        {
            await SetCustomIconAsync(snapshot, versionId);
        }
        else
        {
            // 内置图标：将偏好持久化为 "gameicon:{key}"；null 表示跟随加载器自动
            var overrideValue = choice.Key is null ? null : $"gameicon:{choice.Key}";
            GameVersionProfileStore.SaveInstanceIconOverride(
                snapshot.MinecraftDirectory, versionId, overrideValue);
            if (choice.Key is null)
                NyaAlert.Info($"{versionId} 将跟随加载器自动选择图标。");
            else
                NyaAlert.Success($"{versionId} 已使用内置图标：{choice.Label}。");
            if (choice.Key is null)
                CustomInstanceIconStore.Remove(snapshot.MinecraftDirectory, versionId);
            ApplyIconOverrideToDetails(choice);
            RefreshInstancesView();
            SyncIconComboSelection(snapshot, versionId);
        }
    }

    /// <summary>把新选的内置图标即时应用到当前详情标题左侧大图标。</summary>
    private void ApplyIconOverrideToDetails(IconChoice choice)
    {
        if (choice.Key is null)
            return;
        if (DetailsIconImage is not null)
        {
            DetailsIconImage.SourceUrl = $"gameicon:{choice.Key}";
            DetailsIconImage.InvalidateVisual();
        }
    }

    /// <summary>「恢复默认」按钮：清除图标偏好并刷新。</summary>
    private void OnClearInstanceIconClick(object? sender, RoutedEventArgs e)
    {
        var versionId = _currentDetails?.VersionId;
        var snapshot = GameInstanceStore.Current;
        if (string.IsNullOrWhiteSpace(versionId) ||
            string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory))
            return;

        GameVersionProfileStore.SaveInstanceIconOverride(
            snapshot.MinecraftDirectory, versionId, null);
        CustomInstanceIconStore.Remove(snapshot.MinecraftDirectory, versionId);
        NyaAlert.Info($"{versionId} 已恢复为跟随加载器自动图标。");
        if (DetailsIconImage is not null)
            DetailsIconImage.SourceUrl = null;
        RefreshInstancesView();
        SyncIconComboSelection(snapshot, versionId);
    }

    /// <summary>为指定版本打开文件选择器并设置自定义图标。</summary>
    private async Task SetCustomIconAsync(GameInstanceSnapshot snapshot, string versionId)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"选择 {versionId} 的自定义图标",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"]
                }
            ]
        });
        if (files.Count == 0)
        {
            // 取消选择：恢复下拉框到实际图标来源
            SyncIconComboSelection(snapshot, versionId);
            return;
        }
        if (files[0].TryGetLocalPath() is not { } localPath)
        {
            SyncIconComboSelection(snapshot, versionId);
            return;
        }

        var saved = CustomInstanceIconStore.Set(snapshot.MinecraftDirectory, versionId, localPath);
        if (saved is null)
        {
            NyaAlert.Error("图标设置失败：仅支持 png/jpg/webp/bmp/gif，且不超过 8MB。");
            return;
        }

        // 自定义图标偏好用 "custom" 标记优先读取
        GameVersionProfileStore.SaveInstanceIconOverride(
            snapshot.MinecraftDirectory, versionId, "custom");
        NyaAlert.Success($"已设置 {versionId} 的自定义图标。");
        RefreshInstancesView();
        SyncIconComboSelection(snapshot, versionId);
    }

    /// <summary>依据当前实例的图标偏好，同步下拉框选中项。</summary>
    private void SyncIconComboSelection(GameInstanceSnapshot snapshot, string versionId)
    {
        _synchronizingIconCombo = true;
        try
        {
            var overrideValue = GameVersionProfileStore.GetInstanceIconOverride(
                snapshot.MinecraftDirectory, versionId);
            IconChoice? selected = null;
            if (string.Equals(overrideValue, "custom", StringComparison.Ordinal))
            {
                selected = CustomIconEntry;
            }
            else if (overrideValue is { Length: > 0 } &&
                     overrideValue.StartsWith("gameicon:", StringComparison.Ordinal))
            {
                var key = overrideValue["gameicon:".Length..];
                selected = _iconChoices.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
            }
            InstanceIconCombo.SelectedItem = selected ?? AutoIconEntry;
        }
        finally
        {
            _synchronizingIconCombo = false;
        }
    }

    /// <summary>重建实例列表并异步补全图标（设置/清除图标后调用）。</summary>
    private void RefreshInstancesView()
    {
        var snapshot = GameInstanceStore.Current;
        if (snapshot.IsLoading || snapshot.ErrorMessage is not null)
            return;
        OnInstancesChanged(snapshot);
    }

    /// <summary>图标下拉框选项；Key 为内置 GameIcons 键（null 表示跟随加载器自动）。</summary>
    private sealed record IconChoice(string? Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly IconChoice AutoIconEntry = new(null, "跟随加载器自动");
    private static readonly IconChoice CustomIconEntry = new("custom", "自定义图标…");
    private static readonly IReadOnlyList<IconChoice> _iconChoices =
    [
        AutoIconEntry,
        new IconChoice("vanilla", "原版（草方块）"),
        new IconChoice("forge", "Forge"),
        new IconChoice("neoforge", "NeoForge"),
        new IconChoice("fabric", "Fabric"),
        new IconChoice("command_block", "通用（命令方块）"),
        CustomIconEntry
    ];

    /// <summary>删除指定版本（含确认对话框）。自动清理孤立的依赖版本。</summary>
    private async void RequestDelete(string versionId)
    {
        var snapshot = GameInstanceStore.Current;
        if (string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory)) return;

        // 外部实例不允许删除
        if (GameInstanceLayoutResolver.TryResolveExternalInstance(
                snapshot.SourcePath, out var external) &&
            string.Equals(external.InstanceId, versionId, StringComparison.OrdinalIgnoreCase))
        {
            NyaAlert.Warning("外部启动器实例请通过原启动器删除。");
            return;
        }

        var versionDir = Path.Combine(snapshot.MinecraftDirectory, "versions", versionId);
        if (!Directory.Exists(versionDir))
        {
            NyaAlert.Error($"版本目录不存在：{versionDir}");
            return;
        }

        // 读取 inheritsFrom，用于后续清理孤立依赖
        var parentId = ReadInheritsFrom(snapshot.MinecraftDirectory, versionId);

        // 确认对话框（NyaPrompt 遮罩提示框）
        var message = parentId is not null
            ? $"此操作将永久删除实例 {versionId} 及其依赖版本 {parentId} 的所有文件，且无法恢复。"
            : $"此操作将永久删除实例 {versionId} 的所有文件（包括存档、Mod、资源包等），且无法恢复。";
        var confirmed = await NyaPrompt.ConfirmAsync(
            "删除实例",
            message,
            confirm: "删除",
            cancel: "取消",
            NyaNoticeSeverity.Error);
        if (!confirmed) return;

        try
        {
            Directory.Delete(versionDir, recursive: true);

            // 清理孤立的依赖版本：如果原版只被这一个子版本引用，也一并删除
            if (parentId is not null)
            {
                var parentDir = Path.Combine(snapshot.MinecraftDirectory, "versions", parentId);
                if (Directory.Exists(parentDir) && IsOrphanedDependency(snapshot.MinecraftDirectory, parentId))
                {
                    Directory.Delete(parentDir, recursive: true);
                    NyaAlert.Success($"已删除实例 {versionId} 及孤立依赖 {parentId}。");
                }
                else
                {
                    NyaAlert.Success($"已删除实例 {versionId}。");
                }
            }
            else
            {
                NyaAlert.Success($"已删除实例 {versionId}。");
            }

            await GameInstanceStore.RefreshAsync(snapshot.SourcePath);
        }
        catch (Exception ex)
        {
            NyaAlert.Error($"删除失败：{ex.Message}");
        }
    }

    /// <summary>读取版本 JSON 中的 inheritsFrom 字段。</summary>
    private static string? ReadInheritsFrom(string minecraftDirectory, string versionId)
    {
        try
        {
            var jsonPath = Path.Combine(minecraftDirectory, "versions", versionId, $"{versionId}.json");
            if (!File.Exists(jsonPath)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(jsonPath));
            return doc.RootElement.TryGetProperty("inheritsFrom", out var prop)
                ? prop.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检查指定版本是否为孤立依赖：没有任何其他版本通过 inheritsFrom 引用它。
    /// </summary>
    private static bool IsOrphanedDependency(string minecraftDirectory, string candidateId)
    {
        var versionsDir = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDir)) return true;

        foreach (var dir in Directory.EnumerateDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            if (string.Equals(id, candidateId, StringComparison.OrdinalIgnoreCase))
                continue;

            var parentId = ReadInheritsFrom(minecraftDirectory, id ?? string.Empty);
            if (string.Equals(parentId, candidateId, StringComparison.OrdinalIgnoreCase))
                return false; // 还有其他版本引用它，不是孤立的
        }
        return true;
    }

    private static ProcessStartInfo CreateShellStartInfo(string fileName, string argument)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
        startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private void ConfigureMemorySliders(GameVersionProfile profile)
    {
        _synchronizingMemorySliders = true;
        try
        {
            var systemMaximum = GameMemorySettings.GetSliderMaximumMemoryMb();
            MinimumMemorySlider.Maximum = systemMaximum;
            MaximumMemorySlider.Maximum = systemMaximum;
            var maximum = Math.Clamp(profile.MaximumMemoryMb, 512, systemMaximum);
            var minimum = Math.Clamp(profile.MinimumMemoryMb, 256, maximum);
            MaximumMemorySlider.Value = maximum;
            MinimumMemorySlider.Value = minimum;
            IndependentMemoryCheckBox.IsChecked = profile.UseIndependentMemorySettings;
            UpdateInstanceMemoryText();
        }
        finally
        {
            _synchronizingMemorySliders = false;
        }
    }

    private void OnInstanceMemoryValueChanged(
        object? sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (!_synchronizingMemorySliders)
        {
            _synchronizingMemorySliders = true;
            try
            {
                if (ReferenceEquals(sender, MinimumMemorySlider) &&
                    MinimumMemorySlider.Value > MaximumMemorySlider.Value)
                {
                    MaximumMemorySlider.Value = MinimumMemorySlider.Value;
                }
                else if (ReferenceEquals(sender, MaximumMemorySlider) &&
                         MaximumMemorySlider.Value < MinimumMemorySlider.Value)
                {
                    MinimumMemorySlider.Value = MaximumMemorySlider.Value;
                }
            }
            finally
            {
                _synchronizingMemorySliders = false;
            }
        }

        UpdateInstanceMemoryText();
    }

    private void OnIndependentMemoryChanged(object? sender, RoutedEventArgs e)
    {
        if (_synchronizingMemorySliders)
            return;
        UpdateInstanceMemoryText();
    }

    private void UpdateInstanceMemoryText()
    {
        if (IndependentMemoryCheckBox is null ||
            MinimumMemorySlider is null ||
            MaximumMemorySlider is null ||
            MinimumMemoryValueText is null ||
            MaximumMemoryValueText is null ||
            InstanceMemoryPolicyText is null)
            return;

        var minimum = (int)MinimumMemorySlider.Value;
        var maximum = (int)MaximumMemorySlider.Value;
        var independent = IndependentMemoryCheckBox.IsChecked == true;
        MinimumMemorySlider.IsEnabled = independent;
        MaximumMemorySlider.IsEnabled = independent;
        MinimumMemoryValueText.Text = FormatMemory(minimum);
        MaximumMemoryValueText.Text = FormatMemory(maximum);
        var decision = GameMemorySettings.ResolveForLaunch(independent ? maximum : null);
        InstanceMemoryPolicyText.Text = !independent
            ? decision.IsAutomatic
                ? $"独立调整已关闭；本实例完全使用全局自动策略，按当前状态估算最大 {FormatMemory(decision.MaximumMemoryMb)}。"
                : $"独立调整已关闭；本实例使用全局手动设置 {FormatMemory(decision.MaximumMemoryMb)}。"
            : decision.IsAutomatic
                ? $"独立调整已开启；按当前可用内存估算，本实例最大使用 {FormatMemory(decision.MaximumMemoryMb)}，启动时会重新计算。"
                : $"独立调整已开启；全局手动上限为 {FormatMemory(GameMemorySettings.GetManualMaximumMemoryMb())}，本实例实际最大使用 {FormatMemory(decision.MaximumMemoryMb)}。";
    }

    private static string FormatMemory(int memoryMb) =>
        memoryMb >= 1024
            ? $"{memoryMb / 1024d:0.##} GiB ({memoryMb} MiB)"
            : $"{memoryMb} MiB";

    private void ConfigureAdvancedSettings(GameVersionProfile profile)
    {
        _draftWindowWidth = profile.WindowWidth;
        _draftWindowHeight = profile.WindowHeight;
        _draftJavaExecutable = profile.JavaExecutable;
        _draftJvmArguments = profile.AdditionalJvmArguments;
        _draftGameArguments = profile.AdditionalGameArguments;
        _synchronizingAdvancedSettings = true;
        try
        {
            FollowGlobalAdvancedSettingsCheckBox.IsChecked =
                profile.FollowGlobalAdvancedSettings;
            ApplyAdvancedSettings(profile.FollowGlobalAdvancedSettings);
        }
        finally
        {
            _synchronizingAdvancedSettings = false;
        }
    }

    private void OnFollowGlobalAdvancedSettingsChanged(object? sender, RoutedEventArgs e)
    {
        if (_synchronizingAdvancedSettings)
            return;

        var followGlobal = FollowGlobalAdvancedSettingsCheckBox.IsChecked == true;
        if (followGlobal)
            _ = CaptureAdvancedDraft();
        ApplyAdvancedSettings(followGlobal);
    }

    private void ApplyAdvancedSettings(bool followGlobal)
    {
        var windowWidth = _draftWindowWidth;
        var windowHeight = _draftWindowHeight;
        var javaExecutable = _draftJavaExecutable;
        var jvmArguments = _draftJvmArguments;
        var gameArguments = _draftGameArguments;
        if (followGlobal)
        {
            var global = GlobalLaunchSettingsStore.Load();
            windowWidth = global.WindowWidth;
            windowHeight = global.WindowHeight;
            javaExecutable = global.JavaExecutable;
            jvmArguments = global.AdditionalJvmArguments;
            gameArguments = global.AdditionalGameArguments;
        }

        WindowWidthBox.Text = windowWidth.ToString();
        WindowHeightBox.Text = windowHeight.ToString();
        JavaExecutableBox.Text = javaExecutable;
        JvmArgumentsBox.Text = string.Join(Environment.NewLine, jvmArguments);
        GameArgumentsBox.Text = string.Join(Environment.NewLine, gameArguments);
        WindowWidthBox.IsEnabled = !followGlobal;
        WindowHeightBox.IsEnabled = !followGlobal;
        JavaExecutableBox.IsEnabled = !followGlobal;
        JvmArgumentsBox.IsEnabled = !followGlobal;
        GameArgumentsBox.IsEnabled = !followGlobal;
    }

    private bool CaptureAdvancedDraft()
    {
        if (!TryReadInt(WindowWidthBox, out var width) ||
            !TryReadInt(WindowHeightBox, out var height) ||
            width < 320 ||
            height < 240)
        {
            return false;
        }

        _draftWindowWidth = width;
        _draftWindowHeight = height;
        _draftJavaExecutable = JavaExecutableBox.Text ?? string.Empty;
        _draftJvmArguments = ReadLines(JvmArgumentsBox.Text);
        _draftGameArguments = ReadLines(GameArgumentsBox.Text);
        return true;
    }

    private bool TryResolveCurrentDirectories(
        out string versionDirectory,
        out string contentDirectory)
    {
        versionDirectory = string.Empty;
        contentDirectory = string.Empty;
        var snapshot = GameInstanceStore.Current;
        var versionId = snapshot.SelectedVersionId;
        if (snapshot.IsLoading ||
            snapshot.ErrorMessage is not null ||
            string.IsNullOrWhiteSpace(versionId) ||
            string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory))
        {
            StatusText.Text = "当前没有可用的实例目录。";
            return false;
        }

        if (GameInstanceLayoutResolver.TryResolveExternalInstance(
                snapshot.SourcePath,
                out var external) &&
            string.Equals(external.InstanceId, versionId, StringComparison.OrdinalIgnoreCase))
        {
            versionDirectory = external.InstanceDirectory;
            contentDirectory = external.ContentDirectory;
            return true;
        }

        var layout = GameVersionIsolation.Resolve(snapshot, versionId);
        versionDirectory = Path.Combine(snapshot.MinecraftDirectory, "versions", versionId);
        contentDirectory = layout.ContentDirectory;
        return true;
    }

    private static bool IsCurrentSelection(GameInstanceSnapshot snapshot, string versionId)
    {
        var current = GameInstanceStore.Current;
        return !current.IsLoading &&
               current.ErrorMessage is null &&
               PathUtil.PathsEqual(current.SourcePath, snapshot.SourcePath) &&
               PathUtil.PathsEqual(current.MinecraftDirectory, snapshot.MinecraftDirectory) &&
               string.Equals(
                   current.SelectedVersionId,
                   versionId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static VersionListItem CreateListItem(GameInstanceSnapshot snapshot, string versionId)
    {
        var layout = GameVersionIsolation.Resolve(snapshot, versionId);
        return new VersionListItem(
            versionId,
            versionId,
            layout.IsIsolated
                ? $"版本隔离 · {layout.Provider} · {Path.GetFileName(layout.ContentDirectory)}"
                : $"共享 Minecraft 游戏目录 · {layout.Provider}",
            null,
            "material:Apps");
    }

    private async Task EnrichInstanceVisualsAsync(
        GameInstanceSnapshot snapshot,
        IReadOnlyList<VersionListItem> entries)
    {
        _instanceVisualCancellation?.Cancel();
        _instanceVisualCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _instanceVisualCancellation = cancellation;
        try
        {
            var enriched = await Task.Run(() => entries.Select(entry =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var visual = GameContentMetadataService.ResolveInstanceVisual(snapshot, entry.VersionId);
                return entry with
                {
                    IconPath = visual.IconPath,
                    IconGlyph = visual.FallbackGlyph
                };
            }).ToArray(), cancellation.Token);
            if (cancellation.IsCancellationRequested || !IsSameInstanceSet(snapshot))
                return;

            _synchronizingVersions = true;
            try
            {
                VersionList.ItemsSource = enriched;
                VersionList.SelectedItem = enriched.FirstOrDefault(entry => string.Equals(
                    entry.VersionId,
                    GameInstanceStore.Current.SelectedVersionId,
                    StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _synchronizingVersions = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsSameInstanceSet(GameInstanceSnapshot snapshot)
    {
        var current = GameInstanceStore.Current;
        return !current.IsLoading &&
               current.ErrorMessage is null &&
               PathUtil.PathsEqual(current.SourcePath, snapshot.SourcePath) &&
               PathUtil.PathsEqual(current.MinecraftDirectory, snapshot.MinecraftDirectory) &&
               current.VersionIds.SequenceEqual(snapshot.VersionIds, StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindPath(IEnumerable<string> paths, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;
        return paths.FirstOrDefault(path => PathUtil.PathsEqual(path, target));
    }

    private static bool TryReadInt(TextBox textBox, out int value) =>
        int.TryParse(textBox.Text?.Trim(), out value);

    private static string[] ReadLines(string? text) =>
        (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record VersionListItem(
    string VersionId,
    string Name,
    string DirectoryMode,
    string? IconPath,
    string IconGlyph);
