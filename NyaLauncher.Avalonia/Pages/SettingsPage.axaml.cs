using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Dialogs;
using Avalonia.Interactivity;
using NyaLauncher.Core;

namespace NyaLauncher.Avalonia.Pages;

public partial class SettingsPage : UserControl
{
    private bool _synchronizingMemorySettings = true;

    /// <summary>用户点击"打开账户管理"时触发，由宿主页面转发给主窗口完成跳转。</summary>
    public event EventHandler? AccountManageRequested;

    public SettingsPage()
    {
        InitializeComponent();
        ReloadMemorySettings();
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
            LoadGlobalLaunchSettings();
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
        MaximumMemoryValueText.Text = FormatMemory((int)MaximumMemorySlider.Value);
        if (automatic)
        {
            var decision = GameMemorySettings.ResolveForLaunch();
            AutomaticMemoryHintText.Text =
                $"当前可用 {FormatMemory(decision.AvailableMemoryMb)}；按当前状态启动预计分配 " +
                $"{FormatMemory(decision.MaximumMemoryMb)}，并为系统保留至少 " +
                $"{FormatMemory(decision.ReservedMemoryMb)}。实际值会在每次启动前重新计算。";
        }
        else
        {
            AutomaticMemoryHintText.Text =
                $"自动调整已关闭；{FormatMemory((int)MaximumMemorySlider.Value)} 是全局上限，" +
                "实例可以单独设置更低的最大内存。";
        }
    }

    private static string FormatMemory(int memoryMb) =>
        memoryMb >= 1024
            ? $"{memoryMb / 1024d:0.##} GiB ({memoryMb} MiB)"
            : $"{memoryMb} MiB";

    private void LoadGlobalLaunchSettings()
    {
        var settings = GlobalLaunchSettingsStore.Load();
        GlobalWindowWidthBox.Text = settings.WindowWidth.ToString();
        GlobalWindowHeightBox.Text = settings.WindowHeight.ToString();
        GlobalJavaExecutableBox.Text = settings.JavaExecutable;
        GlobalJvmArgumentsBox.Text = string.Join(
            Environment.NewLine,
            settings.AdditionalJvmArguments);
        GlobalGameArgumentsBox.Text = string.Join(
            Environment.NewLine,
            settings.AdditionalGameArguments);
        GlobalLaunchSettingsHintText.Text =
            "实例默认跟随全局高级设置；关闭实例内的“跟随全局”后才能单独编辑。";
    }

    private void OnSaveGlobalLaunchSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(GlobalWindowWidthBox.Text, out var width) ||
            !int.TryParse(GlobalWindowHeightBox.Text, out var height) ||
            width < 320 ||
            height < 240)
        {
            GlobalLaunchSettingsHintText.Text = "保存失败：窗口尺寸至少为 320×240。";
            return;
        }

        var settings = new GlobalLaunchSettings(
            width,
            height,
            GlobalJavaExecutableBox.Text ?? string.Empty,
            ReadArgumentLines(GlobalJvmArgumentsBox.Text),
            ReadArgumentLines(GlobalGameArgumentsBox.Text));
        GlobalLaunchSettingsHintText.Text = GlobalLaunchSettingsStore.Save(settings)
            ? "全局高级启动设置已保存，新启动的游戏将使用这些设置。"
            : "全局高级启动设置保存失败。";
    }

    private static string[] ReadArgumentLines(string? text) =>
        (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    private void OnOpenAccountManageClick(object? sender, RoutedEventArgs e)
    {
        AccountManageRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        LauncherText.Text = "NyaLauncher版本号:" + NyaLauncherInfo.MainVersion +"."+ NyaLauncherInfo.SubVersion +"."+ NyaLauncherInfo.FixVersion + NyaLauncherInfo.Suffix;
        
    }
}
