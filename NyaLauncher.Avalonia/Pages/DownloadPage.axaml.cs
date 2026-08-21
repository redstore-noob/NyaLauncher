using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Dialogs;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Pages;

public partial class DownloadPage : UserControl
{
    private const int PageSize = 50;
    private readonly GameDownloadService _downloadService;
    private CancellationTokenSource? _searchCts;

    private List<MinecraftVersion>? _allVersions;
    private List<ModrinthProject>? _allMods;
    private List<ModrinthProject>? _allModpacks;
    private List<ModrinthProject>? _allShaders;
    private List<ModrinthProject>? _allResourcepacks;

    private List<MinecraftVersion>? _versionFiltered;
    private List<ModrinthProject>? _modFiltered;
    private List<ModrinthProject>? _modpackFiltered;
    private List<ModrinthProject>? _shaderFiltered;
    private List<ModrinthProject>? _resourcepackFiltered;

    private int _versionPage = 1;
    private int _modPage = 1;
    private int _modpackPage = 1;
    private int _shaderPage = 1;
    private int _resourcepackPage = 1;

    private int _loadingCount;
    private const int TotalLoads = 5;
    private bool _isRefreshing;
    private bool _initialListEffectsQueued;

    /// <summary>当用户点击 Mod 安装按钮时触发，传递 ModrinthProject 给宿主。</summary>
    public event EventHandler<ModrinthProject>? ModInstallRequested;

    /// <summary>XAML 设计器 / Avalonia 反射需要无参构造；运行时由 MainWindow 使用 internal 构造。</summary>
    public DownloadPage()
        : this(new GameDownloadService())
    {
    }

    internal DownloadPage(GameDownloadService downloadService)
    {
        _downloadService = downloadService;
        InitializeComponent();
        _downloadService.Changed += OnDownloadChanged;
        LoadingOverlay.IsVisible = true;
        AttachedToVisualTree += OnAttachedToVisualTree;
        // 使用 AddHandler 确保 DataTemplate 内的 Tapped 事件也能被捕获
        ModList.AddHandler(TappedEvent, OnModListTapped, handledEventsToo: true);
        _ = LoadAllAsync();
        StartSpinnerAnimation();
    }

    private async void OnDownloadVersionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MinecraftVersion version })
            return;

        if (_downloadService.Current.IsActive)
        {
            DownloadTaskStatusText.Text =
                $"正在下载 Minecraft {_downloadService.Current.VersionId}，请等待当前任务结束。";
            return;
        }

        DownloadTaskStatusText.Text = $"正在创建 Minecraft {version.Id} 下载任务…";
        await _downloadService.StartAsync(version);
    }

    private void OnDownloadChanged(GameDownloadSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnDownloadChanged(snapshot));
            return;
        }

        DownloadTaskStatusText.Text = snapshot.Phase switch
        {
            GameDownloadPhase.Preparing => $"正在准备 Minecraft {snapshot.VersionId}",
            GameDownloadPhase.Downloading =>
                $"{snapshot.StageName} · {snapshot.Percentage:0.0}% · {FormatSpeed(snapshot.BytesPerSecond)}",
            GameDownloadPhase.Completed => $"Minecraft {snapshot.VersionId} 安装完成",
            GameDownloadPhase.Failed => $"下载失败：{snapshot.Detail}",
            GameDownloadPhase.Cancelled => "下载任务已取消",
            _ => "选择版本后开始下载"
        };
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond <= 0)
            return "正在测速";
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / (1024 * 1024):0.00} MiB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024:0.0} KiB/s";
        return $"{bytesPerSecond:0} B/s";
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_initialListEffectsQueued)
            return;

        _initialListEffectsQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            QueueListItemEffects(VersionList);
            QueueListItemEffects(ModList);
            QueueListItemEffects(ModpackList);
            QueueListItemEffects(ShaderList);
            QueueListItemEffects(ResourcepackList);
        }, DispatcherPriority.Loaded);
    }

    private void StartSpinnerAnimation()
    {
        var spinnerRotate = new RotateTransform();
        LoadingSpinner.RenderTransform = spinnerRotate;

        var timer = new global::Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        var angle = 0.0;
        timer.Tick += (_, _) =>
        {
            angle = (angle + 6) % 360;
            spinnerRotate.Angle = angle;
        };
        timer.Start();
    }

    private async System.Threading.Tasks.Task LoadAllAsync()
    {
        Interlocked.Exchange(ref _loadingCount, 0);

        await System.Threading.Tasks.Task.WhenAll(
            LoadVersionsAsync(),
            LoadCategoryAsync("Mod", v => _allMods = v, ModList, ModCountText, ModrinthSearch.GetModsAsync),
            LoadCategoryAsync("整合包", v => _allModpacks = v, ModpackList, ModpackCountText, ModrinthSearch.GetModpacksAsync),
            LoadCategoryAsync("光影包", v => _allShaders = v, ShaderList, ShaderCountText, ModrinthSearch.GetShadersAsync),
            LoadCategoryAsync("材质包", v => _allResourcepacks = v, ResourcepackList, ResourcepackCountText, ModrinthSearch.GetResourcePacksAsync)
        );

        await Dispatcher.UIThread.InvokeAsync(() => LoadingOverlay.IsVisible = false);
    }

    private async System.Threading.Tasks.Task LoadVersionsAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshButton.IsEnabled = false;
                RefreshButton.Content = "⟳ 刷新中…";
                LoadingDetail.Text = "正在加载 Minecraft 版本列表…";
            });

            var versions = await ManifestGet.GetVersionsAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allVersions = versions;
                _versionPage = 1;
                ApplyFilter();
                PopulateModVersionFilter();
                RefreshButton.IsEnabled = true;
                RefreshButton.Content = "⟳ 刷新";
                SignalLoadComplete();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VersionCountText.Text = $"版本加载失败: {ex.Message}";
                RefreshButton.IsEnabled = true;
                RefreshButton.Content = "⟳ 刷新";
                SignalLoadComplete();
            });
        }
    }

    private async System.Threading.Tasks.Task LoadCategoryAsync(
        string categoryName,
        Action<List<ModrinthProject>> storeFunc,
        ItemsControl listControl,
        TextBlock countText,
        Func<int, System.Threading.Tasks.Task<List<ModrinthProject>>> fetchFunc)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoadingDetail.Text = $"正在加载{categoryName}…";
                countText.Text = $"正在加载{categoryName}…";
            });

            var items = await fetchFunc(50);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                storeFunc(items);
                countText.Text = $"来自 Modrinth · 共 {items.Count} 个{categoryName}";
                listControl.ItemsSource = new ObservableCollection<ModrinthProject>(items);
                QueueListItemEffects(listControl);
                SignalLoadComplete();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                countText.Text = $"{categoryName}加载失败: {ex.Message}";
                SignalLoadComplete();
            });
        }
    }

    private void SignalLoadComplete()
    {
        var completed = Interlocked.Increment(ref _loadingCount);
        if (completed >= TotalLoads)
            LoadingOverlay.IsVisible = false;
    }

    private static void UpdatePageUI(TextBlock pageText, Button prevBtn, Button nextBtn, int page, int totalPages)
    {
        pageText.Text = $"第 {page} 页 / 共 {totalPages} 页";
        prevBtn.IsEnabled = page > 1;
        nextBtn.IsEnabled = page < totalPages;
    }

    /// <summary>
    /// 从版本过滤 ComboBox 获取选中的 MC 版本。
    /// "所有版本" 以及非版本号的哨兵值返回 null。
    /// </summary>
    private string? ResolveSelectedGameVersion()
    {
        var selected = ModVersionFilter?.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selected) || selected == "所有版本")
            return null;
        return selected;
    }

    private void PopulateModVersionFilter()
    {
        if (ModVersionFilter is null) return;

        ModVersionFilter.Items.Clear();
        ModVersionFilter.Items.Add("所有版本");

        // 优先用已安装的版本
        try
        {
            var snapshot = GameInstanceStore.Current;
            if (!string.IsNullOrWhiteSpace(snapshot.MinecraftDirectory))
            {
                var installed = MinecraftDirectoryLocator
                    .GetInstalledVersionIds(snapshot.MinecraftDirectory);
                foreach (var id in installed)
                    ModVersionFilter.Items.Add(id);
            }
        }
        catch { }

        // 如果没有已安装版本，用 Mojang 版本清单的最新几个
        if (ModVersionFilter.Items.Count <= 1 && _allVersions is not null)
        {
            foreach (var v in _allVersions.Take(20))
                ModVersionFilter.Items.Add(v.Id);
        }

        ModVersionFilter.SelectedIndex = 0;
    }

    private void ApplyFilter()
    {
        if (_allVersions is null) return;

        var filterKey = VersionTypeFilter.SelectedIndex switch
        {
            1 => "release",
            2 => "snapshot",
            3 => "old",
            _ => "all"
        };

        var searchText = VersionSearchBox?.Text ?? "";
        _versionFiltered = VersionFilter.Apply(_allVersions, filterKey)
            .Where(v => string.IsNullOrWhiteSpace(searchText) ||
                        v.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _versionPage = 1;
        ShowVersionPage();
    }

    private void ShowVersionPage()
    {
        if (_versionFiltered is null) return;
        var totalPages = Math.Max(1, (_versionFiltered.Count + PageSize - 1) / PageSize);
        _versionPage = Math.Clamp(_versionPage, 1, totalPages);

        VersionList.ItemsSource = new ObservableCollection<MinecraftVersion>(
            _versionFiltered.Skip((_versionPage - 1) * PageSize).Take(PageSize));
        QueueListItemEffects(VersionList);
        VersionCountText.Text = $"共 {_versionFiltered.Count} 个版本 · 第 {_versionPage}/{totalPages} 页";
        UpdatePageUI(VersionPageText, VersionPrevBtn, VersionNextBtn, _versionPage, totalPages);
    }

    private void FilterAndPageModrinth(
        List<ModrinthProject>? allItems, string searchText,
        ref List<ModrinthProject>? filteredStore, ref int page,
        ItemsControl listControl, TextBlock countText,
        TextBlock pageText, Button prevBtn, Button nextBtn,
        string categoryName)
    {
        if (allItems is null) return;

        var query = allItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(searchText))
            query = query.Where(p => p.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                     p.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        filteredStore = query.ToList();
        page = 1;

        var totalPages = Math.Max(1, (filteredStore.Count + PageSize - 1) / PageSize);
        listControl.ItemsSource = new ObservableCollection<ModrinthProject>(filteredStore.Take(PageSize));
        QueueListItemEffects(listControl);
        countText.Text = $"来自 Modrinth · 共 {filteredStore.Count} 个{categoryName} · 第 1/{totalPages} 页";
        UpdatePageUI(pageText, prevBtn, nextBtn, 1, totalPages);
    }

    private async System.Threading.Tasks.Task<List<ModrinthProject>?> SearchModrinthAsync(
        string searchText, string projectType, string categoryName,
        TextBlock countText, string? gameVersion = null)
    {
        if (string.IsNullOrWhiteSpace(searchText) && string.IsNullOrWhiteSpace(gameVersion))
            return null;

        // 取消上一次搜索
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        try
        {
            countText.Text = $"正在搜索{categoryName}…";
            return await ModrinthSearch.SearchAsync(projectType, searchText, gameVersion, 50, ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            countText.Text = $"搜索失败: {ex.Message}";
            return null;
        }
    }

    private void ShowModrinthPage(
        List<ModrinthProject>? filteredStore, ref int page,
        ItemsControl listControl, TextBlock countText,
        TextBlock pageText, Button prevBtn, Button nextBtn,
        string categoryName)
    {
        if (filteredStore is null) return;

        var totalPages = Math.Max(1, (filteredStore.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, totalPages);

        listControl.ItemsSource = new ObservableCollection<ModrinthProject>(
            filteredStore.Skip((page - 1) * PageSize).Take(PageSize));
        QueueListItemEffects(listControl);
        countText.Text = $"来自 Modrinth · 共 {filteredStore.Count} 个{categoryName} · 第 {page}/{totalPages} 页";
        UpdatePageUI(pageText, prevBtn, nextBtn, page, totalPages);
    }

    private void OnVersionSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private async void OnModSearchChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var searchText = ModSearchBox?.Text ?? "";
            var gameVersion = ResolveSelectedGameVersion();
            if (string.IsNullOrWhiteSpace(searchText) && string.IsNullOrWhiteSpace(gameVersion))
            {
                FilterAndPageModrinth(_allMods, "", ref _modFiltered, ref _modPage,
                    ModList, ModCountText, ModPageText, ModPrevBtn, ModNextBtn, "Mod");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "mod", "Mod", ModCountText, gameVersion);
            if (results is not null)
            {
                _modFiltered = results;
                _modPage = 1;
                ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                    ModPageText, ModPrevBtn, ModNextBtn, "Mod");
            }
        }
        catch (Exception ex)
        {
            ModCountText.Text = $"搜索异常: {ex.Message}";
        }
    }

    private async void OnModVersionFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            var searchText = ModSearchBox?.Text ?? "";
            var gameVersion = ResolveSelectedGameVersion();

            if (string.IsNullOrWhiteSpace(gameVersion))
            {
                // 选择了"所有版本"，回退到完整列表
                FilterAndPageModrinth(_allMods, searchText, ref _modFiltered, ref _modPage,
                    ModList, ModCountText, ModPageText, ModPrevBtn, ModNextBtn, "Mod");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "mod", "Mod", ModCountText, gameVersion);
            if (results is not null)
            {
                _modFiltered = results;
                _modPage = 1;
                ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                    ModPageText, ModPrevBtn, ModNextBtn, "Mod");
            }
        }
        catch (Exception ex)
        {
            ModCountText.Text = $"筛选异常: {ex.Message}";
        }
    }

    private async void OnModpackSearchChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var searchText = ModpackSearchBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(searchText))
            {
                FilterAndPageModrinth(_allModpacks, "", ref _modpackFiltered, ref _modpackPage,
                    ModpackList, ModpackCountText, ModpackPageText, ModpackPrevBtn, ModpackNextBtn, "整合包");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "modpack", "整合包", ModpackCountText);
            if (results is not null)
            {
                _modpackFiltered = results;
                _modpackPage = 1;
                ShowModrinthPage(_modpackFiltered, ref _modpackPage, ModpackList, ModpackCountText,
                    ModpackPageText, ModpackPrevBtn, ModpackNextBtn, "整合包");
            }
        }
        catch (Exception ex)
        {
            ModpackCountText.Text = $"搜索异常: {ex.Message}";
        }
    }

    private async void OnShaderSearchChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var searchText = ShaderSearchBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(searchText))
            {
                FilterAndPageModrinth(_allShaders, "", ref _shaderFiltered, ref _shaderPage,
                    ShaderList, ShaderCountText, ShaderPageText, ShaderPrevBtn, ShaderNextBtn, "光影包");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "shader", "光影包", ShaderCountText);
            if (results is not null)
            {
                _shaderFiltered = results;
                _shaderPage = 1;
                ShowModrinthPage(_shaderFiltered, ref _shaderPage, ShaderList, ShaderCountText,
                    ShaderPageText, ShaderPrevBtn, ShaderNextBtn, "光影包");
            }
        }
        catch (Exception ex)
        {
            ShaderCountText.Text = $"搜索异常: {ex.Message}";
        }
    }

    private async void OnResourcepackSearchChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            var searchText = ResourcepackSearchBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(searchText))
            {
                FilterAndPageModrinth(_allResourcepacks, "", ref _resourcepackFiltered, ref _resourcepackPage,
                    ResourcepackList, ResourcepackCountText, ResourcepackPageText, ResourcepackPrevBtn, ResourcepackNextBtn, "材质包");
                return;
            }

            var results = await SearchModrinthAsync(searchText, "resourcepack", "材质包", ResourcepackCountText);
            if (results is not null)
            {
                _resourcepackFiltered = results;
                _resourcepackPage = 1;
                ShowModrinthPage(_resourcepackFiltered, ref _resourcepackPage, ResourcepackList, ResourcepackCountText,
                    ResourcepackPageText, ResourcepackPrevBtn, ResourcepackNextBtn, "材质包");
            }
        }
        catch (Exception ex)
        {
            ResourcepackCountText.Text = $"搜索异常: {ex.Message}";
        }
    }

    private void OnVersionPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_versionFiltered is not null && _versionPage > 1)
        {
            _versionPage--;
            ShowVersionPage();
        }
    }

    private void OnVersionNextClick(object? sender, RoutedEventArgs e)
    {
        if (_versionFiltered is null) return;
        var total = Math.Max(1, (_versionFiltered.Count + PageSize - 1) / PageSize);
        if (_versionPage < total)
        {
            _versionPage++;
            ShowVersionPage();
        }
    }

    private void OnModListTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e.Source is not Control control) return;
            var project = FindDataContext<ModrinthProject>(control);
            if (project is null) return;
            ModInstallRequested?.Invoke(this, project);
        }
        catch (Exception ex)
        {
            ModCountText.Text = $"操作失败：{ex.Message}";
        }
    }

    private void OnModInstallClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button) return;
            var project = button.DataContext as ModrinthProject
                          ?? FindDataContext<ModrinthProject>(button);
            if (project is null) return;
            ModInstallRequested?.Invoke(this, project);
        }
        catch (Exception ex)
        {
            ModCountText.Text = $"操作失败：{ex.Message}";
        }
    }

    private static T? FindDataContext<T>(Control control) where T : class
    {
        var current = control;
        while (current is not null)
        {
            if (current.DataContext is T typed)
                return typed;
            current = current.Parent as Control;
        }
        return null;
    }

    private void OnModPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_modFiltered is not null && _modPage > 1)
        {
            _modPage--;
            ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                ModPageText, ModPrevBtn, ModNextBtn, "Mod");
        }
    }

    private void OnModNextClick(object? sender, RoutedEventArgs e)
    {
        if (_modFiltered is null) return;
        var total = Math.Max(1, (_modFiltered.Count + PageSize - 1) / PageSize);
        if (_modPage < total)
        {
            _modPage++;
            ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                ModPageText, ModPrevBtn, ModNextBtn, "Mod");
        }
    }

    private void OnModpackPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_modpackFiltered is not null && _modpackPage > 1)
        {
            _modpackPage--;
            ShowModrinthPage(_modpackFiltered, ref _modpackPage, ModpackList, ModpackCountText,
                ModpackPageText, ModpackPrevBtn, ModpackNextBtn, "整合包");
        }
    }

    private void OnModpackNextClick(object? sender, RoutedEventArgs e)
    {
        if (_modpackFiltered is null) return;
        var total = Math.Max(1, (_modpackFiltered.Count + PageSize - 1) / PageSize);
        if (_modpackPage < total)
        {
            _modpackPage++;
            ShowModrinthPage(_modpackFiltered, ref _modpackPage, ModpackList, ModpackCountText,
                ModpackPageText, ModpackPrevBtn, ModpackNextBtn, "整合包");
        }
    }

    private void OnShaderPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_shaderFiltered is not null && _shaderPage > 1)
        {
            _shaderPage--;
            ShowModrinthPage(_shaderFiltered, ref _shaderPage, ShaderList, ShaderCountText,
                ShaderPageText, ShaderPrevBtn, ShaderNextBtn, "光影包");
        }
    }

    private void OnShaderNextClick(object? sender, RoutedEventArgs e)
    {
        if (_shaderFiltered is null) return;
        var total = Math.Max(1, (_shaderFiltered.Count + PageSize - 1) / PageSize);
        if (_shaderPage < total)
        {
            _shaderPage++;
            ShowModrinthPage(_shaderFiltered, ref _shaderPage, ShaderList, ShaderCountText,
                ShaderPageText, ShaderPrevBtn, ShaderNextBtn, "光影包");
        }
    }

    private void OnResourcepackPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_resourcepackFiltered is not null && _resourcepackPage > 1)
        {
            _resourcepackPage--;
            ShowModrinthPage(_resourcepackFiltered, ref _resourcepackPage, ResourcepackList, ResourcepackCountText,
                ResourcepackPageText, ResourcepackPrevBtn, ResourcepackNextBtn, "材质包");
        }
    }

    private void OnResourcepackNextClick(object? sender, RoutedEventArgs e)
    {
        if (_resourcepackFiltered is null) return;
        var total = Math.Max(1, (_resourcepackFiltered.Count + PageSize - 1) / PageSize);
        if (_resourcepackPage < total)
        {
            _resourcepackPage++;
            ShowModrinthPage(_resourcepackFiltered, ref _resourcepackPage, ResourcepackList, ResourcepackCountText,
                ResourcepackPageText, ResourcepackPrevBtn, ResourcepackNextBtn, "材质包");
        }
    }

    private void OnVersionFilterChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        LoadingOverlay.IsVisible = true;
        await LoadAllAsync();
        _isRefreshing = false;
    }

    private void QueueListItemEffects(ItemsControl listControl)
    {
        _ = BounceBehavior.AttachListItemEffectsAsync(listControl, 1.03, RippleBehavior.GlobalRippleLayer);
        Dispatcher.UIThread.Post(
            () => _ = BounceBehavior.AttachListItemEffectsAsync(listControl, 1.03, RippleBehavior.GlobalRippleLayer),
            DispatcherPriority.Loaded);
    }
}
