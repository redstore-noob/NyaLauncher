using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace NyaLauncher.Avalonia.Pages;

public partial class SettingsPage : UserControl
{
    private bool _synchronizingMemorySettings = true;

    public SettingsPage()
    {
        InitializeComponent();
        ReloadMemorySettings();
        // ObservableCollection 绑定：增删账号时 ItemsControl 自动更新，无需手动刷新。
        AccountList.ItemsSource = AccountStore.Current;
        AccountLoginOverlay.AccountAdded += OnAccountAdded;
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

    private void OnAddAccountClick(object? sender, RoutedEventArgs e)
    {
        AccountLoginOverlay.Show();
    }

    private void OnAccountAdded(object? sender, LaunchAccount account)
    {
        AccountHintText.Text = $"已添加账号：{account.DisplayName}";
    }

    private void OnSetDefaultClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is LaunchAccount account)
        {
            AccountStore.MoveToTop(account);
            AccountHintText.Text = $"已设为默认：{account.DisplayName}";
        }
    }

    private void OnDeleteAccountClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is LaunchAccount account)
        {
            AccountStore.Remove(account);
            AccountHintText.Text = AccountStore.Current.Count == 0
                ? $"已删除账号：{account.DisplayName}（账号列表已清空）"
                : $"已删除账号：{account.DisplayName}";
        }
    }
}
