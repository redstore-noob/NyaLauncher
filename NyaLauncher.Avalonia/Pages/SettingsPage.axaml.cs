using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NyaLauncher.Avalonia.Themes;
using NyaLauncher.Core.Config;
using NyaLauncher.Core.Download;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Avalonia.Pages;

public partial class SettingsPage : UserControl
{
    private bool _synchronizingMemorySettings = true;
    private bool _synchronizingIsolationSettings = true;
    private bool _synchronizingGameDirectory = true;
    private bool _synchronizingDownloadSettings = true;
    private bool _initializingThemeSettings = true;
    private readonly Dictionary<string, string> _themeFamilies = new()
    {
        { "HatsuneMiku", "初音未来" },
        { "DeepSeekPurple", "DeepSeek紫" },
        { "ZhiShuBlue", "植树蓝" }
    };
    private readonly Dictionary<string, string> _themeModes = new()
    {
        { "Dark", "暗色" },
        { "Light", "亮色" }
    };

    /// <summary>用户点击"打开账户管理"时触发，由宿主页面转发给主窗口完成跳转。</summary>
    public event EventHandler? AccountManageRequested;

    /// <summary>用户点击"实例管理"时触发。</summary>
    public event EventHandler? InstanceManageRequested;

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
    }

    // ------------------------------------------------------------------
    // 滚动焦点效果：视口中心的卡片略微放大，边缘的卡片缩小
    // ------------------------------------------------------------------

    private Border[]? _cards;

    private void OnSettingsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _cards ??= [CardGame, CardDownload, CardLauncher, CardAccount];

        var scroller = SettingsScroller;
        if (scroller is null) return;

        var viewportCenter = scroller.Offset.Y + scroller.Viewport.Height / 2.0;

        foreach (var card in _cards)
        {
            // 卡片在滚动容器中的垂直中心位置
            var cardTop = card.Bounds.Top;
            var cardCenter = cardTop + card.Bounds.Height / 2.0;
            var distance = Math.Abs(cardCenter - viewportCenter);

            // 距离越近越大，最大 1.0，最小 0.92
            var maxDistance = scroller.Viewport.Height * 0.6;
            var t = Math.Clamp(distance / maxDistance, 0, 1);
            var scale = 1.0 - 0.08 * t;

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
        var global = LauncherConfig.DefaultVersionIsolation;
        VersionSeparateHintText.Text = global == true
            ? "已开启：未单独配置的实例将默认使用版本隔离（独立内容目录）。"
            : "已关闭：未单独配置的实例将使用共享 Minecraft 目录或自动检测结果。";
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
            GameDirectorySelector.ItemsSource = folders;
            GameDirectorySelector.SelectedItem = FindPath(folders, configured) ??
                                                  FindPath(folders, MinecraftDirectoryLocator.GetDefaultDirectory());
            UpdateGameDirectoryHintText();
        }
        finally
        {
            _synchronizingGameDirectory = false;
        }
    }

    private void OnGameDirectorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingGameDirectory || GameDirectorySelector.SelectedItem is not string path)
            return;

        LauncherConfig.SaveGameDirectory(path);
        UpdateGameDirectoryHintText();
    }

    private async void OnAddGameDirectoryClick(object? sender, RoutedEventArgs e)
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

    private void OnRemoveGameDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (GameDirectorySelector.SelectedItem is not string path)
            return;

        var defaultDir = MinecraftDirectoryLocator.GetDefaultDirectory();
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(defaultDir)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            GameDirectoryHintText.Text = "平台默认 Minecraft 目录不可移除。";
            return;
        }

        if (!GameVersionProfileStore.RemoveFolder(path))
        {
            GameDirectoryHintText.Text = "移除失败。";
            return;
        }

        ReloadGameDirectories();
    }

    private void UpdateGameDirectoryHintText()
    {
        var selected = GameDirectorySelector.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selected))
        {
            GameDirectoryHintText.Text = "尚未选择游戏目录。";
            return;
        }

        var count = (GameDirectorySelector.ItemsSource as IReadOnlyList<string>)?.Count ?? 0;
        var defaultDir = MinecraftDirectoryLocator.GetDefaultDirectory();
        var isDefault = string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(defaultDir)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        GameDirectoryHintText.Text = isDefault
            ? $"当前使用平台默认目录（共 {count} 个已添加目录）。"
            : $"当前目录：{selected}（共 {count} 个已添加目录）。";
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
        JavaPathBox.Text = string.IsNullOrWhiteSpace(settings.JavaExecutable)
            ? ""
            : settings.JavaExecutable;
        JavaPathHint.Text = string.IsNullOrWhiteSpace(settings.JavaExecutable)
            ? "将自动检测系统中的 Java。"
            : $"当前指定：{settings.JavaExecutable}";

        JvmArgsBox.Text = settings.AdditionalJvmArguments.Length > 0
            ? string.Join("\n", settings.AdditionalJvmArguments)
            : "";
    }

    private void OnAutoDetectJavaClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var locator = new JavaRuntimeLocator();
            var javaPath = locator.FindJavaExecutable();
            if (!string.IsNullOrWhiteSpace(javaPath))
            {
                JavaPathBox.Text = javaPath;
                JavaPathHint.Text = $"已检测到：{javaPath}";
            }
            else
            {
                JavaPathHint.Text = "未检测到 Java，请手动指定路径或安装 Java。";
            }
        }
        catch (Exception ex)
        {
            JavaPathHint.Text = $"检测失败：{ex.Message}";
        }
    }

    private void OnSaveJavaSettingsClick(object? sender, RoutedEventArgs e)
    {
        var javaPath = JavaPathBox.Text?.Trim() ?? "";
        var jvmArgs = (JvmArgsBox.Text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var current = GlobalLaunchSettingsStore.Load();
        var updated = new GlobalLaunchSettings(
            current.WindowWidth,
            current.WindowHeight,
            javaPath,
            jvmArgs,
            current.AdditionalGameArguments);

        if (GlobalLaunchSettingsStore.Save(updated))
            JavaPathHint.Text = "Java 设置已保存。";
        else
            JavaPathHint.Text = "保存失败。";
    }

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
        ThemeFamilyComboBox.Items.Clear();
        foreach (var kv in _themeFamilies)
        {
            ThemeFamilyComboBox.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key });
        }

        ThemeModeComboBox.Items.Clear();
        foreach (var kv in _themeModes)
        {
            ThemeModeComboBox.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key });
        }

        var currentFamily = ThemeSettings.LoadThemeFamily();
        var currentMode = ThemeSettings.LoadThemeMode();

        for (int i = 0; i < ThemeFamilyComboBox.Items.Count; i++)
        {
            if (ThemeFamilyComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == currentFamily)
            {
                ThemeFamilyComboBox.SelectedIndex = i;
                break;
            }
        }
        for (int i = 0; i < ThemeModeComboBox.Items.Count; i++)
        {
            if (ThemeModeComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == currentMode)
            {
                ThemeModeComboBox.SelectedIndex = i;
                break;
            }
        }
        _initializingThemeSettings = false;
    }

    private void OnThemeFamilyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        if (ThemeFamilyComboBox.SelectedItem is ComboBoxItem item && item.Tag?.ToString() is string family)
        {
            ThemeSettings.SaveThemeFamily(family);
            RestartApplication();
        }
    }

    private void OnThemeModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializingThemeSettings) return;
        if (ThemeModeComboBox.SelectedItem is ComboBoxItem item && item.Tag?.ToString() is string mode)
        {
            ThemeSettings.SaveThemeMode(mode);
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
