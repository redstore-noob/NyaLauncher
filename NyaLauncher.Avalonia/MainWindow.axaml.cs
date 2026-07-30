using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NyaLauncher.Avalonia.Helpers;
using NyaLauncher.Avalonia.Pages;

namespace NyaLauncher.Avalonia;

public partial class MainWindow : Window
{
    private readonly UserControl _launchPage = new LaunchPage();
    private readonly UserControl _downloadPage = new DownloadPage();
    private readonly UserControl _settingsPage = new SettingsPage();

    private readonly Border[] _navItems;
    private ColumnDefinition? _sidebarColumn;

    private readonly SolidColorBrush _navActiveFg = new(Color.Parse("#CCCCDD"));
    private readonly SolidColorBrush _navInactiveFg = new(Color.Parse("#6B6B80"));
    private readonly SolidColorBrush _navActiveBg = new(Color.Parse("#2D2D44"));
    private readonly SolidColorBrush _navInactiveBg = new(Color.Parse("#00000000"));

    private bool _sidebarExpanded = false;

    public MainWindow()
    {
        InitializeComponent();

        _sidebarColumn = RootGrid.ColumnDefinitions[0];

        _navItems = [NavLaunch, NavDownload, NavSettings];

        NavLaunch.PointerPressed += (_, _) => SwitchToPage(0);
        NavDownload.PointerPressed += (_, _) => SwitchToPage(1);
        NavSettings.PointerPressed += (_, _) => SwitchToPage(2);

        // 给导航项（Border）手动附加悬停 + 点击效果
        // （AttachAll 只认 Button/ComboBox 等，不认 Border）
        foreach (var nav in _navItems)
        {
            BounceBehavior.AttachHoverScale(nav, 1.03);
            BounceBehavior.AttachClickBounce(nav);
            RippleBehavior.AttachRipple(nav, RippleLayer);
        }

        SidebarToggle.PointerPressed += async (_, _) => await ToggleSidebarAsync();

        // 初始化侧边栏收起状态
        ApplySidebarState(_sidebarExpanded, animate: false);

        // 注册全局波纹层供子页面使用
        RippleBehavior.GlobalRippleLayer = RippleLayer;

        // 全局动效：自动遍历窗口内的所有交互控件
        GlobalEffectInitializer.AttachAll(this, RippleLayer);

        // 默认显示启动页
        SwitchToPage(0);
    }

    private void SwitchToPage(int index)
    {
        // 更新导航选中状态
        for (var i = 0; i < _navItems.Length; i++)
        {
            var nav = _navItems[i];
            nav.Background = i == index ? _navActiveBg : _navInactiveBg;
            nav.Classes.Set("NavSelected", i == index);

            // 更新所有文字颜色
            foreach (var tb in nav.GetVisualDescendants().OfType<TextBlock>())
            {
                tb.Foreground = i == index ? _navActiveFg : _navInactiveFg;
            }
        }

        // 切换页面（带渐入动画）
        var newPage = index switch
        {
            0 => _launchPage,
            1 => _downloadPage,
            2 => _settingsPage,
            _ => _launchPage
        };

        newPage.Opacity = 0;
        ContentArea.Children.Clear();
        ContentArea.Children.Add(newPage);

        // 等页面完全加载（视觉树就绪）后附加动效
        // 注意：UserControl 的 ContentPresenter 在刚 Add 时尚未创建，
        // 直接 GetVisualChildren 会返回空，所以必须等 Loaded
        newPage.Loaded += OnPageLoaded;

        // 延迟一帧触发渐入
        Dispatcher.UIThread.InvokeAsync(() => { newPage.Opacity = 1; },
            DispatcherPriority.Background);

        void OnPageLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            newPage.Loaded -= OnPageLoaded;
            GlobalEffectInitializer.AttachAll(newPage, RippleLayer);
        }
    }

    private async System.Threading.Tasks.Task ToggleSidebarAsync()
    {
        if (_sidebarAnimating)
            return;

        _sidebarExpanded = !_sidebarExpanded;
        await AnimateSidebarAsync(_sidebarExpanded);
    }

    private bool _sidebarAnimating = false;

    private void ApplySidebarState(bool expanded, bool animate)
    {
        if (_sidebarColumn != null)
        {
            _sidebarColumn.Width = new GridLength(expanded ? 240 : 72);
        }

        SidebarToggleIcon.Text = expanded ? "◀" : "▶";
        SidebarToggleText.Text = expanded ? "收起" : "展开";
        SidebarToggleText.Opacity = expanded ? 1.0 : 0.0;

        foreach (var tb in new[] { NavLaunchText, NavDownloadText, NavSettingsText })
        {
            tb.Opacity = expanded ? 1.0 : 0.0;
            tb.RenderTransform = new TranslateTransform { X = expanded ? 0 : -8 };
        }
    }

    private async System.Threading.Tasks.Task AnimateSidebarAsync(bool expanded)
    {
        if (_sidebarAnimating || _sidebarColumn == null)
            return;

        _sidebarAnimating = true;

        var from = _sidebarColumn.Width.Value;
        var to = expanded ? 240.0 : 72.0;
        var durationMs = 220;
        var frames = Math.Max(1, durationMs / 16);

        for (var i = 1; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3); // CubicOut easing
            var width = from + (to - from) * eased;
            _sidebarColumn.Width = new GridLength(width);

            var textOpacity = expanded ? eased : 1 - eased;
            foreach (var tb in new[] { NavLaunchText, NavDownloadText, NavSettingsText })
            {
                tb.Opacity = textOpacity;
                if (tb.RenderTransform is TranslateTransform translate)
                {
                    translate.X = expanded ? -8 + 8 * eased : -8 * eased;
                }
                else
                {
                    tb.RenderTransform = new TranslateTransform { X = expanded ? -8 + 8 * eased : -8 * eased };
                }
            }

            SidebarToggleText.Opacity = expanded ? eased : 1 - eased;
            if (SidebarToggleText.RenderTransform is TranslateTransform toggleTranslate)
            {
                toggleTranslate.X = expanded ? -8 + 8 * eased : -8 * eased;
            }

            await System.Threading.Tasks.Task.Delay(16);
        }

        ApplySidebarState(expanded, animate: false);
        _sidebarAnimating = false;
    }
}

