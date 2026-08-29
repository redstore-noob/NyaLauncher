using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Core;
using System;
using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Pages;

public partial class AboutPage : UserControl
{
    /// <summary>入场动画总时长（秒）。</summary>
    private const double EntranceDuration = 0.5;

    /// <summary>淡入时长（秒）。</summary>
    private const double FadeDuration = 0.2;

    private int _linkClickCount;
    private double _eggTime;
    private DispatcherTimer? _clickResetTimer;
    private DispatcherTimer? _easterEggTimer;
    private ScaleTransform? _catScale;
    private readonly ScaleTransform _partyScale;
    private readonly RotateTransform _partyRotation;
    private readonly List<NyaParticle> _particles = [];
    private readonly Random _random = new();

    // 猫猫 rua 交互：点猫猫爆爱心、变大、变表情、加速；rua 满 10 次有 milestone
    private static readonly string[] CatFaces = ["🐱", "😺", "😸", "😹", "😻"];
    private static readonly string[] HeartEmojis = ["💜", "💖", "⭐", "✨", "🐾"];
    private static readonly string[] RainEmojis = ["🎉", "⭐", "✨", "💜", "🐾", "🎵"];
    private TextBlock? _cat;
    private NyaParticle? _catParticle;
    private double _catSquash;
    private int _petCount;
    private double _rainAccumulator;

    // 开发者名单彩蛋：连点卡片 7 次召唤「猫娘」
    private const int NekoTriggerClicks = 7;
    private int _devClickCount;
    private DispatcherTimer? _devResetTimer;

    public AboutPage()
    {
        InitializeComponent();
        // Avalonia 不会为 TransformGroup 内的变换生成 x:Name 字段，这里手动解析。
        var group = (TransformGroup)PartyPanel.RenderTransform!;
        _partyScale = (ScaleTransform)group.Children[0];
        _partyRotation = (RotateTransform)group.Children[1];
        _clickResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _clickResetTimer.Tick += OnClickResetTick;
        _devResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _devResetTimer.Tick += OnDevResetTick;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        LauncherText.Text = NyaLauncherInfo.FormatVersionString();
    }

    /// <summary>
    /// 彩蛋触发：连续点击版本号五次后触发秘密派对，超过两秒未点击则重置计数。
    /// </summary>
    private void LauncherText_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _linkClickCount++;
        _clickResetTimer?.Stop();
        _clickResetTimer?.Start();

        if (_linkClickCount >= 3 && _linkClickCount < 5)
        {
            EasterEggHint.Text = $"还需 {5 - _linkClickCount} 次…";
            EasterEggHint.IsVisible = true;
        }

        if (_linkClickCount >= 5)
        {
            _linkClickCount = 0;
            _clickResetTimer?.Stop();
            EasterEggHint.IsVisible = false;
            TriggerEasterEgg();
        }
    }

    private void OnClickResetTick(object? sender, EventArgs e)
    {
        _linkClickCount = 0;
        _clickResetTimer?.Stop();
        EasterEggHint.IsVisible = false;
    }

    /// <summary>
    /// 开发者名单彩蛋：连点卡片 7 次（2 秒内）召唤猫娘登场。
    /// </summary>
    private void DevCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _devClickCount++;
        _devResetTimer?.Stop();
        _devResetTimer?.Start();

        if (_devClickCount < NekoTriggerClicks)
        {
            EasterEggHint.Text = $"连点开发者卡片有惊喜（还需 {NekoTriggerClicks - _devClickCount} 次）";
            EasterEggHint.IsVisible = true;
            return;
        }

        _devClickCount = 0;
        _devResetTimer?.Stop();
        EasterEggHint.IsVisible = false;
        ShowNekoEasterEgg();
    }

    private void OnDevResetTick(object? sender, EventArgs e)
    {
        _devClickCount = 0;
        _devResetTimer?.Stop();
        EasterEggHint.IsVisible = false;
    }

    /// <summary>
    /// 猫娘登场：覆盖层淡入 + 卡片 M3 上浮入场（SlideFadeIn）。
    /// </summary>
    private void ShowNekoEasterEgg()
    {
        NekoOverlay.IsVisible = true;
        NekoOverlay.Opacity = 1;
        _ = AnimationHelper.SlideFadeInAsync(NekoCard, MaterialMotion.MediumTransitionMs, slideOffset: 32);
    }

    /// <summary>点击猫娘覆盖层任意位置关闭。</summary>
    private void NekoOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        NekoOverlay.IsVisible = false;
    }

    /// <summary>
    /// 启动彩蛋：覆盖层淡入、猫猫与消息面板弹性登场，随后粒子碰壁反弹。
    /// </summary>
    private void TriggerEasterEgg()
    {
        _eggTime = 0;
        _petCount = 0;
        _catSquash = 0;
        _rainAccumulator = 0;
        EasterEggOverlay.IsVisible = true;
        EasterEggOverlay.Opacity = 0;
        PartyTitle.Text = "秘密派对！";
        PetCountText.IsVisible = false;

        var w = EasterEggOverlay.Bounds.Width;
        var h = EasterEggOverlay.Bounds.Height;
        if (w <= 0) w = 800;
        if (h <= 0) h = 600;

        NyaCanvas.Children.Clear();
        _particles.Clear();

        // 主角：弹跳猫猫（先以 0 缩放等待弹性入场；可以 rua）
        var cat = new TextBlock
        {
            Text = CatFaces[0],
            FontSize = 40,
            Padding = new Thickness(8),
            IsHitTestVisible = true,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        _catScale = new ScaleTransform(0, 0);
        cat.RenderTransform = _catScale;
        cat.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        cat.PointerPressed += Cat_OnPointerPressed;
        NyaCanvas.Children.Add(cat);
        var catParticle = new NyaParticle
        {
            Element = cat,
            X = w / 2,
            Y = h / 2,
            Vx = 3.5,
            Vy = 2.5,
        };
        _particles.Add(catParticle);
        Canvas.SetLeft(cat, catParticle.X);
        Canvas.SetTop(cat, catParticle.Y);
        _cat = cat;
        _catParticle = catParticle;

        // 配角：nya~ 文字粒子（入场时逐颗浮现）
        string[] nyaTexts = ["nya~", "喵~", "meow~", "🐱", "nya!", "喵！", "=^.^=", "nya~"];
        string[] colors = ["#FF6B9D", "#4ECDC4", "#FFE66D", "#A8E6CF", "#FF8B94", "#B8B8FF", "#FFD3B6", "#C7CEEA"];

        for (int i = 0; i < nyaTexts.Length; i++)
        {
            var nya = new TextBlock
            {
                Text = nyaTexts[i],
                FontSize = 14 + _random.Next(10),
                Foreground = new SolidColorBrush(Color.Parse(colors[i])),
                Opacity = 0,
            };
            NyaCanvas.Children.Add(nya);
            var p = new NyaParticle
            {
                Element = nya,
                X = _random.NextDouble() * w,
                Y = _random.NextDouble() * h,
                Vx = (_random.NextDouble() - 0.5) * 4,
                Vy = (_random.NextDouble() - 0.5) * 4,
            };
            _particles.Add(p);
            Canvas.SetLeft(nya, p.X);
            Canvas.SetTop(nya, p.Y);
        }

        _easterEggTimer?.Stop();
        _easterEggTimer = null;
        _easterEggTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _easterEggTimer.Tick += UpdateEasterEgg;
        _easterEggTimer.Start();
    }

    /// <summary>
    /// 每帧更新：先播放入场动画（淡入 + 弹性放大 + 轻微摇摆），入场结束后驱动粒子反弹、
    /// 彩带雨、寿命粒子衰减与猫猫挤压回弹。
    /// </summary>
    private void UpdateEasterEgg(object? sender, EventArgs e)
    {
        const double dt = 0.016;
        _eggTime += dt;

        var t = Math.Min(1.0, _eggTime / EntranceDuration);
        var entering = _eggTime < EntranceDuration;

        // 覆盖层淡入
        EasterEggOverlay.Opacity = Math.Min(1.0, _eggTime / FadeDuration);

        // 中央消息面板：弹性放大 + 轻微摇摆（逐渐归零）
        var pop = BackEaseOut(t);
        _partyScale.ScaleX = 0.5 + 0.5 * pop;
        _partyScale.ScaleY = 0.5 + 0.5 * pop;
        _partyRotation.Angle = Math.Sin(t * Math.PI * 3) * 3.0 * (1 - t);
        PartyPanel.Opacity = Math.Min(1.0, _eggTime / FadeDuration);

        // 猫猫弹性登场（带过冲）+ 挤压回弹（rua 或撞墙时的形变）
        if (_catScale is not null)
        {
            var baseScale = Math.Max(0, pop);
            _catScale.ScaleX = baseScale * (1 + _catSquash);
            _catScale.ScaleY = baseScale * (1 - _catSquash);
        }

        // 粒子逐颗浮现
        for (int i = 0; i < _particles.Count; i++)
        {
            var delay = 0.05 + i * 0.03;
            if (_particles[i].MaxLife is null)
                _particles[i].Element.Opacity = Math.Clamp((_eggTime - delay) / FadeDuration, 0, 1);
        }

        if (entering) return;

        // 入场结束后：彩带雨按间隔从顶部生成
        _rainAccumulator += dt;
        if (_rainAccumulator >= 0.35 && _particles.Count < 60)
        {
            _rainAccumulator = 0;
            SpawnRain();
        }

        var w = EasterEggOverlay.Bounds.Width;
        var h = EasterEggOverlay.Bounds.Height;
        if (w <= 0) w = 800;
        if (h <= 0) h = 600;

        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.X += p.Vx;
            p.Y += p.Vy;

            var ew = p.Element.Bounds.Width;
            var eh = p.Element.Bounds.Height;
            if (ew <= 0) ew = 40;
            if (eh <= 0) eh = 40;

            var bounced = false;
            if (p.X < 0) { p.X = 0; p.Vx = Math.Abs(p.Vx); bounced = true; }
            if (p.X > w - ew) { p.X = w - ew; p.Vx = -Math.Abs(p.Vx); bounced = true; }
            if (p.Y < 0) { p.Y = 0; p.Vy = Math.Abs(p.Vy); bounced = true; }
            if (p.Y > h - eh) { p.Y = h - eh; p.Vy = -Math.Abs(p.Vy); bounced = true; }
            if (bounced && ReferenceEquals(p, _catParticle))
                _catSquash = Math.Max(_catSquash, 0.22); // 猫猫撞墙也来一下挤压回弹

            // 有寿命的粒子（爱心 / 彩带）：逐渐淡出，寿命归零移除
            if (p.MaxLife is { } max)
            {
                p.Life += dt;
                p.Element.Opacity = Math.Clamp((max - p.Life) / 0.4, 0, 1);
                if (p.Life >= max)
                {
                    RemoveParticleAt(i);
                    continue;
                }
            }

            // 彩带落到地面即消失
            if (p.IsRain && p.Y >= h - eh)
            {
                RemoveParticleAt(i);
                continue;
            }

            Canvas.SetLeft(p.Element, p.X);
            Canvas.SetTop(p.Element, p.Y);
        }

        // 挤压形变衰减（回弹）
        if (_catSquash > 0.001)
            _catSquash *= 0.82;
        else
            _catSquash = 0;
        if (_catScale is not null)
        {
            _catScale.ScaleX = 1 + _catSquash;
            _catScale.ScaleY = 1 - _catSquash;
        }
    }

    /// <summary>rua 猫猫：爆爱心、变表情、变大、加速、挤压回弹；rua 满 10 次触发 milestone。</summary>
    private void Cat_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true; // 不让点击冒泡到覆盖层（否则会关闭彩蛋）
        if (_cat is null || _catParticle is null) return;

        _petCount++;
        _cat.Text = CatFaces[_petCount % CatFaces.Length];
        if (_cat.FontSize < 72) _cat.FontSize += 2;
        _catParticle.Vx = Math.Clamp(_catParticle.Vx * 1.06, -8, 8);
        _catParticle.Vy = Math.Clamp(_catParticle.Vy * 1.06, -8, 8);
        _catSquash = 0.35;

        PetCountText.Text = $"已rua × {_petCount}";
        PetCountText.IsVisible = true;
        SpawnHearts(_catParticle.X, _catParticle.Y, 6);

        // milestone：rua 满 10 次，猫猫被 rua 熟啦
        if (_petCount == 10)
        {
            PartyTitle.Text = "猫猫已经被rua熟啦 ✨";
            SpawnHearts(_catParticle.X, _catParticle.Y, 20);
            _catParticle.Vx = Math.Clamp(_catParticle.Vx * 1.4, -9, 9);
            _catParticle.Vy = Math.Clamp(_catParticle.Vy * 1.4, -9, 9);
        }
    }

    /// <summary>从指定点爆出一簇向上飘散、逐渐淡出的爱心/星星粒子。</summary>
    private void SpawnHearts(double x, double y, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var heart = new TextBlock
            {
                Text = HeartEmojis[_random.Next(HeartEmojis.Length)],
                FontSize = 12 + _random.Next(12),
                IsHitTestVisible = false,
            };
            NyaCanvas.Children.Add(heart);
            var p = new NyaParticle
            {
                Element = heart,
                X = x,
                Y = y,
                Vx = (_random.NextDouble() - 0.5) * 5,
                Vy = -2 - _random.NextDouble() * 3,
                MaxLife = 0.8 + _random.NextDouble() * 0.4,
            };
            _particles.Add(p);
            Canvas.SetLeft(heart, p.X);
            Canvas.SetTop(heart, p.Y);
        }
    }

    /// <summary>从覆盖层顶部随机位置生成一颗下落彩带，落地即消失。</summary>
    private void SpawnRain()
    {
        var w = EasterEggOverlay.Bounds.Width;
        if (w <= 0) w = 800;

        var drop = new TextBlock
        {
            Text = RainEmojis[_random.Next(RainEmojis.Length)],
            FontSize = 12 + _random.Next(14),
            IsHitTestVisible = false,
        };
        NyaCanvas.Children.Add(drop);
        var p = new NyaParticle
        {
            Element = drop,
            X = _random.NextDouble() * (w - 60),
            Y = -50,
            Vx = (_random.NextDouble() - 0.5) * 1.5,
            Vy = 2 + _random.NextDouble() * 2.5,
            IsRain = true,
            MaxLife = 8, // 兜底寿命：即使卡在边缘也最终被回收
        };
        _particles.Add(p);
        Canvas.SetLeft(drop, p.X);
        Canvas.SetTop(drop, p.Y);
    }

    private void RemoveParticleAt(int index)
    {
        var p = _particles[index];
        if (p.Element is TextBlock cat && ReferenceEquals(cat, _cat))
            _cat = null; // 猫猫永不走这里，仅防御
        NyaCanvas.Children.Remove(p.Element);
        _particles.RemoveAt(index);
    }

    /// <summary>
    /// 点击覆盖层空白处关闭彩蛋（猫猫点击已被标记 Handled，不会走到这里）。
    /// </summary>
    private void EasterEggOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _easterEggTimer?.Stop();
        _easterEggTimer = null;
        EasterEggOverlay.IsVisible = false;
        EasterEggOverlay.Opacity = 1;
        NyaCanvas.Children.Clear();
        _particles.Clear();
        _cat = null;
        _catParticle = null;
    }

    /// <summary>
    /// 带轻微过冲的退场缓动（BackEaseOut），用于弹性放大效果。
    /// </summary>
    private static double BackEaseOut(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        var x = t - 1;
        return 1 + c3 * x * x * x + c1 * x * x;
    }

    private sealed class NyaParticle
    {
        public Control Element { get; init; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; set; }
        public double Vy { get; set; }
        /// <summary>寿命（秒）；null = 永生（猫猫 / nya~ 文字）。</summary>
        public double? MaxLife { get; init; }
        public double Life { get; set; }
        /// <summary>彩带雨：落地即消失。</summary>
        public bool IsRain { get; init; }
    }
}
