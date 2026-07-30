using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NyaLauncher.Avalonia.Animations.Helpers;
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

    private bool _sidebarExpanded;
    private bool _sidebarAnimating;
    private int _currentPageIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        _sidebarColumn = RootGrid.ColumnDefinitions[0];
        _navItems = [NavLaunch, NavDownload, NavSettings];

        NavLaunch.PointerPressed += (_, _) => SwitchToPage(0);
        NavDownload.PointerPressed += (_, _) => SwitchToPage(1);
        NavSettings.PointerPressed += (_, _) => SwitchToPage(2);

        foreach (var nav in _navItems)
        {
            BounceBehavior.AttachHoverScale(nav, 1.03);
            BounceBehavior.AttachClickBounce(nav);
            RippleBehavior.AttachRipple(nav, RippleLayer);
        }

        SidebarToggle.PointerPressed += async (_, _) => await ToggleSidebarAsync();

        ApplySidebarState(_sidebarExpanded, animate: false);
        RippleBehavior.GlobalRippleLayer = RippleLayer;

        // 让整个窗口先成型，再统一附加全局效果，避免首帧抖动
        Dispatcher.UIThread.Post(() => GlobalEffectInitializer.AttachAll(this, RippleLayer), DispatcherPriority.Loaded);

        SwitchToPage(0);
    }

    private void SwitchToPage(int index)
    {
        if (_currentPageIndex == index)
            return;

        for (var i = 0; i < _navItems.Length; i++)
        {
            var nav = _navItems[i];
            nav.Background = i == index ? _navActiveBg : _navInactiveBg;
            nav.Classes.Set("NavSelected", i == index);

            foreach (var tb in nav.GetVisualDescendants().OfType<TextBlock>())
            {
                tb.Foreground = i == index ? _navActiveFg : _navInactiveFg;
            }
        }

        var newPage = index switch
        {
            0 => _launchPage,
            1 => _downloadPage,
            2 => _settingsPage,
            _ => _launchPage
        };

        newPage.Opacity = 0;
        ContentHost.Content = newPage;

        Dispatcher.UIThread.Post(() => newPage.Opacity = 1, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => GlobalEffectInitializer.AttachAll(newPage, RippleLayer), DispatcherPriority.Loaded);

        _currentPageIndex = index;
    }

    private async System.Threading.Tasks.Task ToggleSidebarAsync()
    {
        if (_sidebarAnimating)
            return;

        _sidebarExpanded = !_sidebarExpanded;
        await AnimateSidebarAsync(_sidebarExpanded);
    }

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
            var eased = 1 - Math.Pow(1 - t, 3);
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
