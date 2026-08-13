using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Plugins;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Content;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Tools;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Plugin.Abstractions.Minecraft;

namespace NyaLauncher.Avalonia.Pages;

public partial class VersionManagerPage : UserControl
{
    private PluginManager? _pluginManager;
    private bool _synchronizingFolders;
    private bool _synchronizingVersions;
    private bool _synchronizingMemorySliders;
    private bool _synchronizingAdvancedSettings = true;
    private bool _pluginActionInProgress;
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
        GameInstanceStore.Changed += OnInstancesChanged;
        GameVersionProfileStore.Changed += OnProfilesChanged;
        ModsList.AddHandler(ContentEntryItem.ModFileChangedEvent, OnModFileChanged);
        ReloadFolders();
    }

    internal VersionManagerPage(PluginManager pluginManager)
        : this()
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _pluginManager.Changed += OnPluginCatalogChanged;
        RefreshPluginActions();
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

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
            return;
        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "添加 Minecraft 版本文件夹",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!GameInstanceStore.CanResolveSource(path))
        {
            StatusText.Text = "无法添加该文件夹：未找到标准 Minecraft 版本或可识别的第三方实例元数据。";
            return;
        }

        if (!GameVersionProfileStore.AddFolder(path))
        {
            StatusText.Text = "版本文件夹保存失败。";
            return;
        }

        LauncherConfig.SaveGameDirectory(path);
        ReloadFolders();
        await GameInstanceStore.RefreshAsync(path);
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
            StatusText.Text = "正在扫描版本文件夹…";
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

    private void OnPluginCatalogChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshPluginActions);
            return;
        }

        RefreshPluginActions();
    }

    private void RefreshPluginActions()
    {
        var actions = _pluginManager is null
            ? []
            : _pluginManager.Current.InstanceActions
                .Select(action => new PluginActionListItem(action))
                .ToArray();
        PluginActionsList.ItemsSource = actions;
        PluginActionsList.IsVisible = actions.Length > 0;
        PluginActionsList.IsEnabled = !_pluginActionInProgress;
        PluginActionsEmptyText.IsVisible = actions.Length == 0;
    }

    private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingVersions || VersionList.SelectedItem is not VersionListItem selected)
            return;
        GameInstanceStore.Select(selected.VersionId);
        _ = LoadDetailsAsync(GameInstanceStore.Current, selected.VersionId);
    }

    private async void OnPluginActionClick(object? sender, RoutedEventArgs e)
    {
        if (_pluginActionInProgress ||
            _pluginManager is null ||
            sender is not Button { DataContext: PluginActionListItem item } ||
            !TryCreatePluginInstanceDescriptor(out var snapshot, out var descriptor))
        {
            return;
        }

        var action = item.Action;
        if ((action.IsDestructive || !string.IsNullOrWhiteSpace(action.ConfirmationMessage)) &&
            !await ConfirmPluginActionAsync(item))
        {
            StatusText.Text = $"已取消插件操作“{action.Title}”，实例未作修改。";
            return;
        }

        _pluginActionInProgress = true;
        RefreshPluginActions();
        StatusText.Text = $"正在由 {action.PluginName} 执行“{action.Title}”…";
        try
        {
            var result = await _pluginManager.InvokeInstanceActionAsync(
                action.PluginId,
                action.ExtensionId,
                action.ActionId,
                descriptor);

            // 插件可能改写版本 JSON、资源或目录结构，完成后统一重新扫描。
            var refreshed = await GameInstanceStore.RefreshAsync(snapshot.SourcePath);
            if (refreshed.VersionIds.Contains(
                    descriptor.VersionId,
                    StringComparer.OrdinalIgnoreCase))
            {
                GameInstanceStore.Select(descriptor.VersionId);
                await LoadDetailsAsync(GameInstanceStore.Current, descriptor.VersionId);
            }

            StatusText.Text = result.Success
                ? string.IsNullOrWhiteSpace(result.Message)
                    ? $"插件操作“{action.Title}”已完成。"
                    : result.Message
                : $"插件操作失败：{result.Message ?? "插件未提供错误详情。"}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = $"插件操作“{action.Title}”已取消。";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"插件操作失败：{exception.Message}";
        }
        finally
        {
            _pluginActionInProgress = false;
            RefreshPluginActions();
        }
    }

    private bool TryCreatePluginInstanceDescriptor(
        out GameInstanceSnapshot snapshot,
        out MinecraftInstanceDescriptor descriptor)
    {
        snapshot = GameInstanceStore.Current;
        descriptor = null!;
        var versionId = snapshot.SelectedVersionId;
        if (snapshot.IsLoading ||
            snapshot.ErrorMessage is not null ||
            string.IsNullOrWhiteSpace(versionId) ||
            string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory) ||
            _currentDetails is null ||
            !string.Equals(
                _currentDetails.VersionId,
                versionId,
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "请先选择并等待一个可用实例加载完成。";
            return false;
        }

        try
        {
            // 共享实例以 .minecraft 为游戏目录；隔离/外部实例使用解析后的内容目录。
            var layout = GameVersionIsolation.Resolve(snapshot, versionId);
            var gameDirectory = layout.IsIsolated
                ? layout.ContentDirectory
                : snapshot.MinecraftDirectory;
            descriptor = PluginManager.CreateInstanceDescriptor(
                snapshot,
                versionId,
                gameDirectory);
            return true;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"无法解析当前实例目录：{exception.Message}";
            return false;
        }
    }

    private async Task<bool> ConfirmPluginActionAsync(PluginActionListItem item)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            StatusText.Text = "无法显示插件操作确认窗口。";
            return false;
        }

        var action = item.Action;
        var cancelButton = new Button { Content = "取消", Padding = new Thickness(16, 8) };
        var executeButton = new Button
        {
            Content = action.IsDestructive ? "确认执行" : "继续",
            Padding = new Thickness(16, 8),
            Background = Brush.Parse("#A5525E"),
            Foreground = Brushes.White
        };
        var buttons = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(executeButton);

        var message = string.IsNullOrWhiteSpace(action.ConfirmationMessage)
            ? "此操作会持久修改当前 Minecraft 实例。请确认已了解插件用途，并建议先备份重要数据。"
            : action.ConfirmationMessage;
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 13 };
        body.Children.Add(new TextBlock
        {
            Text = action.IsDestructive ? "确认破坏性插件操作" : "确认插件操作",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#F6F7FF")
        });
        body.Children.Add(new TextBlock
        {
            Text = $"{action.PluginName} · {action.Title}",
            FontSize = 12,
            Foreground = Brush.Parse("#AEB6FF"),
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new Border
        {
            Padding = new Thickness(12),
            Background = Brush.Parse(action.IsDestructive ? "#3B282B" : "#272E49"),
            BorderBrush = Brush.Parse(action.IsDestructive ? "#75434B" : "#46527A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = message,
                FontSize = 11,
                Foreground = Brush.Parse(action.IsDestructive ? "#FFD2CE" : "#DDE2F4"),
                TextWrapping = TextWrapping.Wrap
            }
        });
        body.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "插件实例操作确认",
            Width = 540,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 620,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Brush.Parse("#111522"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body
        };
        cancelButton.Click += (_, _) => dialog.Close(false);
        executeButton.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool?>(owner) == true;
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
        DetailsIconGlyphText.Text = details.InstanceIconGlyph;
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

    private async void OnSaveSettingsClick(object? sender, RoutedEventArgs e)
    {
        var snapshot = GameInstanceStore.Current;
        if (_currentDetails is null)
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

        var requestedVersionId = VersionNameBox.Text?.Trim() ?? string.Empty;
        var versionId = _currentDetails.VersionId;
        if (_currentDetails.IsExternallyManaged &&
            !string.Equals(versionId, requestedVersionId, StringComparison.Ordinal))
        {
            StatusText.Text = "外部启动器实例的物理重命名需要由原启动器完成。";
            return;
        }
        if (!string.Equals(versionId, requestedVersionId, StringComparison.Ordinal))
        {
            StatusText.Text = $"正在将 {versionId} 重命名为 {requestedVersionId}…";
            try
            {
                var oldVersionDirectory = _currentDetails.VersionDirectory;
                versionId = await GameVersionRenameService.RenameAsync(
                    snapshot.MinecraftDirectory,
                    versionId,
                    requestedVersionId);
                var sourcePath = PathUtil.PathsEqual(snapshot.SourcePath, oldVersionDirectory)
                    ? Path.Combine(snapshot.MinecraftDirectory, "versions", versionId)
                    : snapshot.SourcePath;
                await GameInstanceStore.RefreshAsync(sourcePath);
                GameInstanceStore.Select(versionId);
                snapshot = GameInstanceStore.Current;
            }
            catch (Exception exception)
            {
                StatusText.Text = $"版本重命名失败：{exception.Message}";
                return;
            }
        }

        var profile = new GameVersionProfile
        {
            MinecraftDirectory = snapshot.MinecraftDirectory,
            VersionId = versionId,
            MinimumMemoryMb = minimumMemory,
            MaximumMemoryMb = maximumMemory,
            WindowWidth = _draftWindowWidth,
            WindowHeight = _draftWindowHeight,
            IsVersionIsolationEnabled = _currentDetails.IsExternallyManaged
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
    // 右键菜单公共接口（由 InstanceListItem 调用）
    // ------------------------------------------------------------------

    /// <summary>选中指定版本并将焦点移到名称输入框（触发重命名）。</summary>
    public void RequestRename(string versionId)
    {
        GameInstanceStore.Select(versionId);
        _ = LoadDetailsAsync(GameInstanceStore.Current, versionId);
        VersionNameBox.Focus();
        StatusText.Text = "修改上方名称后点击保存即可重命名。";
    }

    /// <summary>删除指定版本（含确认对话框）。自动清理孤立的依赖版本。</summary>
    public async void RequestDelete(string versionId)
    {
        var snapshot = GameInstanceStore.Current;
        if (string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory)) return;

        // 外部实例不允许删除
        if (GameInstanceLayoutResolver.TryResolveExternalInstance(
                snapshot.SourcePath, out var external) &&
            string.Equals(external.InstanceId, versionId, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "外部启动器实例请通过原启动器删除。";
            return;
        }

        var versionDir = Path.Combine(snapshot.MinecraftDirectory, "versions", versionId);
        if (!Directory.Exists(versionDir))
        {
            StatusText.Text = $"版本目录不存在：{versionDir}";
            return;
        }

        // 读取 inheritsFrom，用于后续清理孤立依赖
        var parentId = ReadInheritsFrom(snapshot.MinecraftDirectory, versionId);

        // 确认对话框
        var dialog = new Window
        {
            Title = "删除实例",
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = $"确定删除实例 {versionId}？",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = parentId is not null
                ? $"此操作将永久删除该版本及其依赖版本 {parentId} 的所有文件，且无法恢复。"
                : "此操作将永久删除该版本的所有文件（包括存档、Mod、资源包等），且无法恢复。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Brushes.Gray
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(18, 8) };
        cancelBtn.Click += (_, _) => dialog.Close(false);
        var deleteBtn = new Button
        {
            Content = "删除",
            Padding = new Thickness(18, 8),
            Background = Brushes.Red,
            Foreground = Brushes.White
        };
        deleteBtn.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(deleteBtn);
        stack.Children.Add(buttons);
        dialog.Content = stack;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;
        var result = await dialog.ShowDialog<bool>(owner);
        if (!result) return;

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
                    StatusText.Text = $"已删除实例 {versionId} 及孤立依赖 {parentId}。";
                }
                else
                {
                    StatusText.Text = $"已删除实例 {versionId}。";
                }
            }
            else
            {
                StatusText.Text = $"已删除实例 {versionId}。";
            }

            await GameInstanceStore.RefreshAsync(snapshot.SourcePath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"删除失败：{ex.Message}";
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
            "▦");
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
    private sealed record PluginActionListItem(PluginInstanceActionSnapshot Action)
    {
        public string Glyph => string.IsNullOrWhiteSpace(Action.Glyph) ? "◇" : Action.Glyph;

        public string Title => Action.Title;

        public string PluginLabel => $"{Action.PluginName} · {Action.PluginId}";

        public string Description => string.IsNullOrWhiteSpace(Action.Description)
            ? "插件未提供操作说明。"
            : Action.Description;

        public string RiskLabel => Action.IsDestructive
            ? "破坏性操作"
            : string.IsNullOrWhiteSpace(Action.ConfirmationMessage)
                ? "将修改实例"
                : "需要确认";
    }

}

public sealed record VersionListItem(
    string VersionId,
    string Name,
    string DirectoryMode,
    string? IconPath,
    string IconGlyph);
