using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Models;

namespace NyaLauncher.Avalonia.Pages;

public partial class DownloadPage : UserControl
{
    private const int PageSize = 50;

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

    public DownloadPage()
    {
        InitializeComponent();
        LoadingOverlay.IsVisible = true;
        AttachedToVisualTree += OnAttachedToVisualTree;
        _ = LoadAllAsync();
        StartSpinnerAnimation();
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
        TextBlock countText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return null;

        try
        {
            countText.Text = $"正在搜索{categoryName}…";
            return await ModrinthSearch.SearchAsync(projectType, searchText);
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
        var searchText = ModSearchBox?.Text ?? "";
        if (string.IsNullOrWhiteSpace(searchText))
        {
            FilterAndPageModrinth(_allMods, "", ref _modFiltered, ref _modPage,
                ModList, ModCountText, ModPageText, ModPrevBtn, ModNextBtn, "Mod");
            return;
        }

        var results = await SearchModrinthAsync(searchText, "mod", "Mod", ModCountText);
        if (results is not null)
        {
            _modFiltered = results;
            _modPage = 1;
            ShowModrinthPage(_modFiltered, ref _modPage, ModList, ModCountText,
                ModPageText, ModPrevBtn, ModNextBtn, "Mod");
        }
    }

    private async void OnModpackSearchChanged(object? sender, TextChangedEventArgs e)
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

    private async void OnShaderSearchChanged(object? sender, TextChangedEventArgs e)
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

    private async void OnResourcepackSearchChanged(object? sender, TextChangedEventArgs e)
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
