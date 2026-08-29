using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Controls;
using NyaLauncher.Avalonia.Themes;
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
    private bool _initializingThemeSettings = true;

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
        InitializeThemeSettings();
        RefreshJavaRuntimeList();
        ReloadHotkeys();

        // 离开设置页时中止快捷键录制，避免按键继续被吞
        DetachedFromVisualTree += (_, _) =>
        {
            if (AppHotkeys.IsCapturing)
            {
                AppHotkeys.EndCapture();
                ReloadHotkeys();
            }
        };
    }

    // ------------------------------------------------------------------
    // 滚动焦点效果：视口中心的卡片略微放大，边缘的卡片缩小
    // ------------------------------------------------------------------

    private Border[]? _cards;

    private void OnSettingsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _cards ??= [CardGame, CardHotkeys, CardJava, CardDownload, CardLauncher, CardAccount, CardAi];

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
    // 快捷键设置（打开设置 / 快捷启动）
    // ------------------------------------------------------------------

    private HotkeyAction? _capturingAction;

    private void ReloadHotkeys()
    {
        _capturingAction = null;
        OpenSettingsHotkeyButton.Content = AppHotkeys.Format(AppHotkeys.OpenSettingsGesture);
        OpenSettingsHotkeyHintText.Text =
            "在启动器任意界面按下即可打开设置页。点击右侧按钮录制新组合键。";

        var quickLaunch = AppHotkeys.QuickLaunchGesture;
        QuickLaunchHotkeyButton.Content = quickLaunch is not null
            ? AppHotkeys.Format(quickLaunch)
            : "未设置";
        QuickLaunchClearButton.IsVisible = quickLaunch is not null;
        QuickLaunchHotkeyHintText.Text =
            "以当前选中的实例与账户直接启动游戏（需先在启动页选好版本）。默认未设置。";
    }

    private void OnOpenSettingsHotkeyClick(object? sender, RoutedEventArgs e) =>
        StartHotkeyCapture(HotkeyAction.OpenSettings);

    private void OnQuickLaunchHotkeyClick(object? sender, RoutedEventArgs e) =>
        StartHotkeyCapture(HotkeyAction.QuickLaunch);

    private void OnQuickLaunchClearClick(object? sender, RoutedEventArgs e)
    {
        AppHotkeys.Clear(HotkeyAction.QuickLaunch);
        ReloadHotkeys();
    }

    private void StartHotkeyCapture(HotkeyAction action)
    {
        // 录制中再次点击同一行 = 取消
        if (_capturingAction == action)
        {
            AppHotkeys.EndCapture();
            ReloadHotkeys();
            return;
        }

        _capturingAction = action;
        AppHotkeys.BeginCapture((outcome, gesture) => OnHotkeyCaptureResult(action, outcome, gesture));

        var button = action == HotkeyAction.OpenSettings
            ? OpenSettingsHotkeyButton
            : QuickLaunchHotkeyButton;
        button.Content = "按下组合键…";
        SetHotkeyHint(action, "请按下新组合键（需包含 Ctrl 或 Alt）· Esc 取消 · 再点一次按钮取消");
    }

    private void OnHotkeyCaptureResult(HotkeyAction action, HotkeyCaptureOutcome outcome, KeyGesture? gesture)
    {
        switch (outcome)
        {
            case HotkeyCaptureOutcome.Captured when gesture is not null:
                AppHotkeys.Save(action, gesture);
                ReloadHotkeys();
                break;
            case HotkeyCaptureOutcome.Cancelled:
                ReloadHotkeys();
                break;
            case HotkeyCaptureOutcome.Rejected:
                // 捕获仍在进行，提示后继续等待下一次按键
                SetHotkeyHint(action, "无效组合：需要包含 Ctrl 或 Alt，再试一次（Esc 取消）");
                break;
        }
    }

    private void SetHotkeyHint(HotkeyAction action, string text)
    {
        if (action == HotkeyAction.OpenSettings)
            OpenSettingsHotkeyHintText.Text = text;
        else
            QuickLaunchHotkeyHintText.Text = text;
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

    private void InitializeThemeSettings()
    {
        var currentFamily = ThemeSettings.LoadThemeFamily();
        var currentMode = ThemeSettings.LoadThemeMode();

        // 主题色卡：同步选中态（_initializingThemeSettings 守卫避免触发应用逻辑）
        ThemeCardMiku.IsChecked = currentFamily == "HatsuneMiku";
        ThemeCardDeepSeek.IsChecked = currentFamily == "DeepSeekPurple";
        ThemeCardZhiShu.IsChecked = currentFamily == "ZhiShuBlue";
        ThemeCardMojang.IsChecked = currentFamily == "MojangRed";

        // 明暗分段按钮：同步选中态（System = 跟随系统）
        ThemeModeDarkChip.IsChecked = currentMode == "Dark";
        ThemeModeSystemChip.IsChecked = currentMode == "System";
        ThemeModeLightChip.IsChecked = currentMode == "Light";
        // 「彩虹背景」开关：初始化时同步到 AmbientGradient 全局开关
        AmbientSwitch.IsChecked = ThemeSettings.LoadAmbientGradient();
        AmbientGradient.AmbientGradientEnabled = AmbientSwitch.IsChecked == true;
        // 「星尘特效」开关：同步到 SparkleTrail 全局开关
        SparkleSwitch.IsChecked = ThemeSettings.LoadSparkleTrail();
        SparkleTrail.SparkleTrailEnabled = SparkleSwitch.IsChecked == true;
        _initializingThemeSettings = false;
    }

    private void OnAmbientChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        var enabled = AmbientSwitch.IsChecked == true;
        AmbientGradient.AmbientGradientEnabled = enabled;
        AmbientGradient.RefreshGlobal(); // 立即生效：关则移除渐变层，开则重新注入
        ThemeSettings.SaveAmbientGradient(enabled);
    }

    private void OnSparkleChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        var enabled = SparkleSwitch.IsChecked == true;
        SparkleTrail.SparkleTrailEnabled = enabled;
        SparkleTrail.RefreshGlobal(); // 立即生效：关则移除星星层，开则重新注入
        ThemeSettings.SaveSparkleTrail(enabled);
    }

    private void OnThemeFamilyChecked(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        // IsCheckedChanged 在取消选中时也会触发，只响应选中
        if (sender is RadioButton { Tag: string family, IsChecked: true })
        {
            ThemeSettings.SaveThemeFamily(family);
            ApplyThemeHot(family, ThemeSettings.LoadThemeMode());
        }
    }

    private void OnThemeModeChecked(object? sender, RoutedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        if (sender is RadioButton { Tag: string mode, IsChecked: true })
        {
            ThemeSettings.SaveThemeMode(mode);
            ApplyThemeHot(ThemeSettings.LoadThemeFamily(), mode);
        }
    }

    /// <summary>
    /// 主题热重载（无需重启应用）；异常时回退到传统重启方案。
    /// </summary>
    private static void ApplyThemeHot(string family, string mode)
    {
        try
        {
            NyaLauncher.Avalonia.Themes.ThemeManager.ApplyTheme(family, mode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] 主题热重载失败，回退重启：{ex}");
            RestartApplication();
        }
    }

    private static void RestartApplication()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false
            };

            if (Path.GetFileNameWithoutExtension(exe) is "dotnet" or "dotnet.exe")
            {
                var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(dll))
                    return;
                psi.FileName = exe;
                psi.Arguments = $"\"{dll}\"";
            }
            else
            {
                psi.FileName = exe;
            }

            Process.Start(psi);
            Environment.Exit(0);
        }
        catch
        {
        }
    }
}
