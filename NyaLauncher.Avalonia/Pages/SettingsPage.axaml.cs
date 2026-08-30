using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>Java 列表条目：包装配置项 + 是否为默认（列表首位）。</summary>
public sealed record JavaListEntry(ConfigFileManager.JavaPathItem Item, bool IsDefault)
{
    public string PathText => Item.JavaPath;

    public string VersionText => string.IsNullOrWhiteSpace(Item.JavaVersion)
        ? "版本未知"
        : $"Java {Item.JavaVersion}";
}

/// <summary>游戏目录列表条目：路径 + 是否当前使用 + 是否为平台默认。</summary>
public sealed record GameDirectoryEntry(string Path, bool IsCurrent, bool IsDefault)
{
    public string PathText => Path;

    /// <summary>徽章文字：默认目录优先显示「默认」，当前使用的显示「当前」，其余为「目录」。</summary>
    public string BadgeText => IsDefault ? "默认" : IsCurrent ? "当前" : "目录";

    /// <summary>徽章是否高亮（当前使用或平台默认时亮色）。</summary>
    public bool BadgeHighlight => IsCurrent || IsDefault;
}

public partial class SettingsPage : UserControl
{
    private bool _synchronizingMemorySettings = true;
    private bool _synchronizingIsolationSettings = true;
    private bool _synchronizingGameDirectory = true;
    private bool _synchronizingDownloadSettings = true;

    // ------------------------------------------------------------------
    // 设置页搜索（由 SettingsHubPage 调用）
    // ------------------------------------------------------------------

    /// <summary>
    /// 应用搜索过滤：仅保留标题或关键词命中的卡片，返回命中卡片数；
    /// 查询为空白时恢复全部卡片并返回 -1（表示非搜索态）。
    /// </summary>
    public int ApplySearchFilter(string? query)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            CardGame.IsVisible = CardJava.IsVisible = CardDownload.IsVisible = CardAccount.IsVisible = true;
            return -1;
        }

        var hits = 0;
        void Match(Border card, string title, params string[] aliases)
        {
            var matched = Hit(title) || aliases.Any(Hit);
            card.IsVisible = matched;
            if (matched)
                hits++;
        }

        Match(CardGame, "游戏设置", "实例", "版本隔离", "隔离", "游戏目录", "内存", "自动内存", "校验文件");
        Match(CardJava, "Java 环境", "java", "jvm", "虚拟机", "路径", "参数", "运行时");
        Match(CardDownload, "下载设置", "下载", "下载源", "镜像", "并发", "线程");
        Match(CardAccount, "账户管理", "账号", "登录", "微软", "离线", "皮肤", "头像");
        return hits;

        bool Hit(string text) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>用户点击"打开账户管理"时触发，由宿主页面转发给主窗口完成跳转。</summary>
    public event EventHandler? AccountManageRequested;

    /// <summary>用户点击"实例管理"时触发。</summary>
    public event EventHandler? InstanceManageRequested;

    /// <summary>用户点击"前往下载中心"时触发，由宿主页面转发给主窗口切换到下载页的 Java 标签。</summary>
    public event EventHandler? JavaRuntimeManageRequested;

    public SettingsPage()
    {
        InitializeComponent();
        ReloadMemorySettings();
        ReloadIsolationSettings();
        ReloadVerifyFilesSettings();
        ReloadGameDirectories();
        ReloadJavaSettings();
        ReloadDownloadSettings();
        RefreshJavaRuntimeList();
    }

    // ------------------------------------------------------------------
    // 滚动焦点效果：视口中心的卡片略微放大，边缘的卡片缩小
    // ------------------------------------------------------------------

    private Border[]? _cards;

    private void OnSettingsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _cards ??= [CardGame, CardJava, CardDownload, CardAccount];

        var scroller = SettingsScroller;
        if (scroller is null) return;

        var viewportCenter = scroller.Offset.Y + scroller.Viewport.Height / 2.0;

        foreach (var card in _cards)
        {
            // 卡片在滚动容器中的垂直中心位置
            var cardTop = card.Bounds.Top;
            var cardCenter = cardTop + card.Bounds.Height / 2.0;
            var distance = Math.Abs(cardCenter - viewportCenter);

            // 距离越近越大：1.0 → 0.97（克制的微缩放，与全局微交互幅度一致）
            var maxDistance = scroller.Viewport.Height * 0.6;
            var t = Math.Clamp(distance / maxDistance, 0, 1);
            var scale = 1.0 - 0.03 * t;

            card.RenderTransform = new ScaleTransform(scale, scale);
        }
    }

    public void ReloadMemorySettings()
    {
        _synchronizingMemorySettings = true;
        try
        {
            var memory = GameMemorySettings.GetSystemMemory();
            var sliderMaximum = GameMemorySettings.GetSliderMaximumMemoryMb();
            MaximumMemorySlider.Maximum = sliderMaximum;
            MaximumMemorySlider.Value = GameMemorySettings.GetManualMaximumMemoryMb();
            AutomaticMemoryCheckBox.IsChecked =
                GameMemorySettings.IsAutomaticAdjustmentEnabled;
            MemoryRangeText.Text =
                $"系统总内存 {FormatMemory(memory.TotalMemoryMb)} · 可选上限 {FormatMemory(sliderMaximum)}";
            UpdateMemoryControls();
        }
        finally
        {
            _synchronizingMemorySettings = false;
        }
    }

    private void OnMaximumMemoryValueChanged(
        object? sender,
        RangeBaseValueChangedEventArgs e)
    {
        var memoryMb = (int)Math.Round(e.NewValue);
        MaximumMemoryValueText.Text = FormatMemory(memoryMb);
        if (_synchronizingMemorySettings)
            return;

        GameMemorySettings.SaveManualMaximumMemoryMb(memoryMb);
        UpdateMemoryControls();
    }

    private void OnAutomaticMemoryChanged(object? sender, RoutedEventArgs e)
    {
        if (_synchronizingMemorySettings)
            return;

        GameMemorySettings.IsAutomaticAdjustmentEnabled =
            AutomaticMemoryCheckBox.IsChecked == true;
        UpdateMemoryControls();
    }

    private void UpdateMemoryControls()
    {
        var automatic = AutomaticMemoryCheckBox.IsChecked == true;
        MaximumMemorySlider.IsEnabled = !automatic;

        var memory = GameMemorySettings.GetSystemMemory();
        var currentValue = (int)MaximumMemorySlider.Value;
        MaximumMemoryValueText.Text = FormatMemory(currentValue);

        if (automatic)
        {
            var decision = GameMemorySettings.ResolveForLaunch();
            MaximumMemoryValueText.Text = FormatMemory(decision.MaximumMemoryMb);
            var usagePct = memory.TotalMemoryMb > 0
                ? decision.MaximumMemoryMb * 100.0 / memory.TotalMemoryMb
                : 0;
            AutomaticMemoryHintText.Text =
                $"可用 {FormatMemory(decision.AvailableMemoryMb)} / 总计 {FormatMemory(memory.TotalMemoryMb)}" +
                $" → 预计分配 {FormatMemory(decision.MaximumMemoryMb)}（{usagePct:0}%）" +
                $"，为系统保留 {FormatMemory(decision.ReservedMemoryMb)}。" +
                $"每次启动前自动重新计算。";
        }
        else
        {
            var usagePct = memory.TotalMemoryMb > 0
                ? currentValue * 100.0 / memory.TotalMemoryMb
                : 0;
            AutomaticMemoryHintText.Text =
                $"手动上限 {FormatMemory(currentValue)}（占总内存 {usagePct:0}%）。" +
                "实例可单独设置更低值。";
        }
    }

    private static string FormatMemory(int memoryMb) =>
        memoryMb >= 1024
            ? $"{memoryMb / 1024d:0.##} GiB ({memoryMb} MiB)"
            : $"{memoryMb} MiB";

    private void ReloadIsolationSettings()
    {
        _synchronizingIsolationSettings = true;
        try
        {
            var global = LauncherConfig.DefaultVersionIsolation;
            VersionSeparate.IsChecked = global == true;
            UpdateIsolationHintText();
        }
        finally
        {
            _synchronizingIsolationSettings = false;
        }
    }

    private void OnVersionSeparateChanged(object? sender, RoutedEventArgs e)
    {
        if (_synchronizingIsolationSettings)
            return;

        LauncherConfig.SaveDefaultVersionIsolation(VersionSeparate.IsChecked == true);
        UpdateIsolationHintText();
    }

    private void UpdateIsolationHintText()
    {
        // 与 GameVersionIsolation.Resolve 的判定优先级保持一致：
        // 实例显式设置 > 自动检测（PCL/HMCL 等）> 全局默认兜底 > 共享目录
        var global = LauncherConfig.DefaultVersionIsolation;
        VersionSeparateHintText.Text = global == true
            ? "已开启：未单独配置的实例默认使用版本隔离；检测到其他启动器（PCL/HMCL 等）的隔离布局时跟随该布局。"
            : "已关闭：未单独配置的实例使用共享目录；检测到其他启动器（PCL/HMCL 等）的隔离布局时跟随该布局。";
    }

    // ------------------------------------------------------------------
    // 启动前文件校验
    // ------------------------------------------------------------------

    private void ReloadVerifyFilesSettings()
    {
        VerifyFilesCheckBox.IsChecked = LauncherConfig.VerifyFilesBeforeLaunch;
    }

    private void OnVerifyFilesChanged(object? sender, RoutedEventArgs e)
    {
        LauncherConfig.SaveVerifyFilesBeforeLaunch(VerifyFilesCheckBox.IsChecked == true);
    }

    // ------------------------------------------------------------------
    // 游戏目录管理
    // ------------------------------------------------------------------

    public void ReloadGameDirectories()
    {
        _synchronizingGameDirectory = true;
        try
        {
            var folders = GameVersionProfileStore.GetFolders().ToList();
            var configured = LauncherConfig.GameDirectory;
            var defaultDir = MinecraftDirectoryLocator.GetDefaultDirectory();
            GameDirectoryList.ItemsSource = folders
                .Select(p => new GameDirectoryEntry(
                    p,
                    IsCurrent: PathsEqual(p, configured),
                    IsDefault: PathsEqual(p, defaultDir)))
                .ToList();
            UpdateGameDirectoryButtons();
            UpdateGameDirectoryHintText();
        }
        finally
        {
            _synchronizingGameDirectory = false;
        }
    }

    private void OnGameDirectorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingGameDirectory)
            return;
        UpdateGameDirectoryButtons();
    }

    private void UpdateGameDirectoryButtons()
    {
        var selected = GameDirectoryList.SelectedItem as GameDirectoryEntry;
        SetCurrentGameDirectoryButton.IsEnabled = selected is { IsCurrent: false };
        RemoveGameDirectoryButton.IsEnabled = selected is { IsDefault: false };
    }

    private void OnSetCurrentGameDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (_synchronizingGameDirectory || GameDirectoryList.SelectedItem is not GameDirectoryEntry entry)
            return;
        LauncherConfig.SaveGameDirectory(entry.Path);
        ReloadGameDirectories();
    }

    private async void OnAddGameDirectoryClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider is null)
                return;

            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "添加 Minecraft 游戏目录",
                AllowMultiple = false
            });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!GameInstanceStore.CanResolveSource(path))
            {
                GameDirectoryHintText.Text = "无法添加：该文件夹不包含有效的 Minecraft 版本或可识别的实例。";
                return;
            }

            if (!GameVersionProfileStore.AddFolder(path))
            {
                GameDirectoryHintText.Text = "添加失败。";
                return;
            }

            LauncherConfig.SaveGameDirectory(path);
            ReloadGameDirectories();
        }
        catch (Exception ex)
        {
            GameDirectoryHintText.Text = $"添加目录失败：{ex.Message}";
        }
    }

    private void OnRemoveGameDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (_synchronizingGameDirectory || GameDirectoryList.SelectedItem is not GameDirectoryEntry entry)
            return;

        var defaultDir = MinecraftDirectoryLocator.GetDefaultDirectory();
        if (PathsEqual(entry.Path, defaultDir))
        {
            GameDirectoryHintText.Text = "平台默认 Minecraft 目录不可移除。";
            return;
        }

        if (!GameVersionProfileStore.RemoveFolder(entry.Path))
        {
            GameDirectoryHintText.Text = "移除失败。";
            return;
        }

        ReloadGameDirectories();
    }

    private void UpdateGameDirectoryHintText()
    {
        var current = LauncherConfig.GameDirectory;
        if (string.IsNullOrWhiteSpace(current))
        {
            GameDirectoryHintText.Text = "尚未设置当前游戏目录。";
            return;
        }

        var count = (GameDirectoryList.ItemsSource as IReadOnlyList<GameDirectoryEntry>)?.Count ?? 0;
        var defaultDir = MinecraftDirectoryLocator.GetDefaultDirectory();
        var isDefault = PathsEqual(current, defaultDir);

        GameDirectoryHintText.Text = isDefault
            ? $"当前使用平台默认目录（共 {count} 个已添加目录）。"
            : $"当前目录：{current}（共 {count} 个已添加目录）。";
    }

    private static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string? FindPath(IReadOnlyList<string> folders, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return folders.FirstOrDefault(f => comparer.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target))));
    }

    // ------------------------------------------------------------------
    // Java 设置
    // ------------------------------------------------------------------

    private void ReloadJavaSettings()
    {
        var settings = GlobalLaunchSettingsStore.Load();
        JvmArgsBox.Text = settings.AdditionalJvmArguments.Length > 0
            ? string.Join("\n", settings.AdditionalJvmArguments)
            : "";
        ReloadJavaList();
    }

    /// <summary>重建"已保存的 Java" ListBox（第一条为默认）。</summary>
    private void ReloadJavaList()
    {
        var paths = LauncherConfig.GetJavaPaths();
        var entries = paths
            .Select((item, index) => new JavaListEntry(item, IsDefault: index == 0))
            .ToList();
        JavaList.ItemsSource = entries;

        if (entries.Count == 0)
        {
            JavaPathHint.Text = "尚未保存 Java 路径，启动时将自动检测。";
        }
        else
        {
            JavaPathHint.Text = entries.Count == 1
                ? $"已保存 1 条：{entries[0].PathText}"
                : $"已保存 {entries.Count} 条，默认：{entries[0].PathText}";
        }
        UpdateJavaSelectionButtons();
    }

    /// <summary>选中变化时刷新右侧按钮可用性。</summary>
    private void OnJavaListSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateJavaSelectionButtons();

    /// <summary>「设为默认 / 删除选中」按钮可用性随选中项变化。</summary>
    private void UpdateJavaSelectionButtons()
    {
        var selected = JavaList.SelectedItem as JavaListEntry;
        RemoveJavaButton.IsEnabled = selected is not null;
        SetDefaultJavaButton.IsEnabled = selected is { IsDefault: false };
    }

    /// <summary>自动检索系统全部 Java 并一次性加入列表（去重由 AddJava 保证）。</summary>
    private void OnAutoDetectJavaClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var locator = new JavaRuntimeLocator();
            var found = locator.FindAllJavaExecutables(
                JavaRuntimeInstaller.GetRuntimeDirectory());
            if (found.Count == 0)
            {
                JavaPathHint.Text = "未检测到任何 Java，请点击「添加 Java…」手动选择 javaw.exe。";
                return;
            }

            var added = 0;
            var detected = new List<string>();
            foreach (var path in found)
            {
                var version = JavaRuntimeLocator.TryDetectMajorVersion(path);
                if (LauncherConfig.AddJava(path, version?.ToString() ?? "unknown"))
                {
                    added++;
                    detected.Add(version is int v ? $"Java {v}" : path);
                }
            }

            JavaPathHint.Text = $"已自动检索并加入 {added} 条 Java：{string.Join("、", detected.Take(5))}" +
                                (detected.Count > 5 ? $" 等 {detected.Count} 项" : "");
            ReloadJavaList();
        }
        catch (Exception ex)
        {
            JavaPathHint.Text = $"检测失败：{ex.Message}";
        }
    }

    /// <summary>弹出文件选择框选择 javaw.exe / java.exe，自动探测版本后加入列表。</summary>
    private async void OnAddJavaPathClick(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
            return;

        IReadOnlyList<IStorageFile> result;
        try
        {
            result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 Java 可执行文件",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Java 可执行文件")
                    {
                        Patterns = OperatingSystem.IsWindows()
                            ? ["javaw.exe", "java.exe"]
                            : ["javaw", "java"]
                    },
                    new FilePickerFileType("所有文件") { Patterns = ["*.*"] }
                ]
            });
        }
        catch (Exception ex)
        {
            JavaPathHint.Text = $"打开文件选择器失败：{ex.Message}";
            return;
        }

        var path = result.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var version = JavaRuntimeLocator.TryDetectMajorVersion(path);
        if (LauncherConfig.AddJava(path, version?.ToString() ?? "unknown"))
        {
            JavaPathHint.Text = version is int v
                ? $"已添加 Java {v}：{path}"
                : $"已添加（未能识别版本）：{path}";
            ReloadJavaList();
        }
        else
        {
            JavaPathHint.Text = "添加 Java 路径失败。";
        }
    }

    /// <summary>把选中项设为默认（列表首位），并清除全局 override 使列表成为唯一权威。</summary>
    private void OnSetPrimarySelectedJavaClick(object? sender, RoutedEventArgs e)
    {
        if (JavaList.SelectedItem is not JavaListEntry { IsDefault: false } entry)
            return;

        var path = entry.PathText;
        if (!LauncherConfig.SetPrimaryJava(path))
        {
            JavaPathHint.Text = "设置默认 Java 失败。";
            return;
        }

        var current = GlobalLaunchSettingsStore.Load();
        _ = GlobalLaunchSettingsStore.Save(current with { JavaExecutable = "" });
        JavaPathHint.Text = $"已将 {path} 设为默认。";
        ReloadJavaList();
    }

    /// <summary>移除选中项。</summary>
    private void OnRemoveSelectedJavaClick(object? sender, RoutedEventArgs e)
    {
        if (JavaList.SelectedItem is not JavaListEntry entry)
            return;

        var path = entry.PathText;
        if (LauncherConfig.RemoveJava(path))
        {
            JavaPathHint.Text = $"已移除：{path}";
            ReloadJavaList();
        }
        else
        {
            JavaPathHint.Text = "移除 Java 路径失败。";
        }
    }

    /// <summary>保存 JVM 启动参数；Java 路径由上方列表即时管理。</summary>
    private void OnSaveJavaSettingsClick(object? sender, RoutedEventArgs e)
    {
        var jvmArgs = (JvmArgsBox.Text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var current = GlobalLaunchSettingsStore.Load();
        var updated = new GlobalLaunchSettings(
            current.WindowWidth,
            current.WindowHeight,
            "", // Java 由上方列表管理；空值 = $auto，回落列表默认
            jvmArgs,
            current.AdditionalGameArguments);

        if (GlobalLaunchSettingsStore.Save(updated))
            JavaPathHint.Text = "JVM 参数已保存。";
        else
            JavaPathHint.Text = "保存失败。";
    }

    // ------------------------------------------------------------------
    // Java 运行时管理（下载功能在下载中心 Java 标签页）
    // ------------------------------------------------------------------

    /// <summary>
    /// 刷新已安装的 Java 运行时列表显示。
    /// </summary>
    private void RefreshJavaRuntimeList()
    {
        try
        {
            var runtimes = JavaRuntimeInstaller.GetInstalledRuntimes();
            JavaRuntimeListText.Text = runtimes.Count == 0
                ? "尚未安装自动下载的 Java 运行时。"
                : $"已安装：{string.Join("、", runtimes.Select(r => r.DisplayName))}";
        }
        catch
        {
            JavaRuntimeListText.Text = "";
        }
    }

    private void OnOpenJavaRuntimeManagerClick(object? sender, RoutedEventArgs e) =>
        JavaRuntimeManageRequested?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------------
    // 下载设置
    // ------------------------------------------------------------------

    private void ReloadDownloadSettings()
    {
        _synchronizingDownloadSettings = true;
        try
        {
            // 下载源
            DownloadSourceComboBox.Items.Clear();
            foreach (var source in DownloadSources.All)
                DownloadSourceComboBox.Items.Add(source.Name);

            var currentSource = DownloadSourceProvider.Active;
            var index = DownloadSources.All.ToList().FindIndex(
                s => string.Equals(s.Name, currentSource.Name, StringComparison.OrdinalIgnoreCase));
            DownloadSourceComboBox.SelectedIndex = index >= 0 ? index : 0;
            UpdateDownloadSourceHint();

            // 并行下载线程数
            ParallelDownloadsSlider.Value = DownloadSettings.ParallelDownloads;
            ParallelDownloadsText.Text = DownloadSettings.ParallelDownloads.ToString();
        }
        finally
        {
            _synchronizingDownloadSettings = false;
        }
    }

    private void OnDownloadSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingDownloadSettings || DownloadSourceComboBox.SelectedItem is not string name)
            return;

        var source = DownloadSources.All
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (source is not null)
            DownloadSettings.SaveActiveSource(source);

        UpdateDownloadSourceHint();
    }

    private void OnParallelDownloadsChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var value = (int)Math.Round(e.NewValue);
        ParallelDownloadsText.Text = value.ToString();
        if (_synchronizingDownloadSettings)
            return;
        DownloadSettings.SaveParallelDownloads(value);
    }

    private void UpdateDownloadSourceHint()
    {
        var active = DownloadSourceProvider.Active;
        var fallback = DownloadSourceProvider.Fallback;
        DownloadSourceHint.Text = fallback is not null
            ? $"当前：{active.Name}，失败时自动回退到 {fallback.Name}。"
            : $"当前：{active.Name}，未设置回退源。";
    }

    private void OnOpenAccountManageClick(object? sender, RoutedEventArgs e)
    {
        AccountManageRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenInstanceManagerClick(object? sender, RoutedEventArgs e)
    {
        InstanceManageRequested?.Invoke(this, EventArgs.Empty);
    }
}
