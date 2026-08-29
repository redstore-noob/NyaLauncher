using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Material.Styles.Themes;
using Material.Styles.Themes.Base;

namespace NyaLauncher.Avalonia.Themes;

public class StyleAlter
{
    /// <summary>
    /// 应用主题：中性兜底基底（按明暗模式）+ 家族强调色与背景搭配 + 强调派生键。
    /// <para>
    /// 结构：BasePalette.axaml 提供深/浅两套中性兜底基底；{family}_Accents.axaml
    /// 直接键提供强调色阶梯、Material 次色与语义色（不区分深浅），其 ThemeDictionaries
    /// 必须写出家族专属的暗色 / 浅色背景搭配并按当前模式覆盖基底；
    /// 强调色的衍生键（强调文字、主按钮、拖放预览等）由
    /// <see cref="ApplyDerivedAccentKeys"/> 按「明暗模式 × 家族强调色」派生写入。
    /// 家族文件缺失时自动降级到 HatsuneMiku。
    /// </para>
    /// </summary>
    public static void ApplyTheme(string themeFamily, string themeMode)
    {
        if (string.IsNullOrWhiteSpace(themeFamily))
            return;

        var app = Application.Current;
        if (app == null)
            return;

        var variant = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        var familyUri = new Uri($"avares://NyaLauncher.Avalonia/Themes/{themeFamily}_Accents.axaml");
        try
        {
            ApplyFamily(familyUri, variant, app);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StyleAlter] Failed to load theme family '{themeFamily}': {ex}");

            if (!string.Equals(themeFamily, "HatsuneMiku", StringComparison.OrdinalIgnoreCase))
            {
                var fallbackUri = new Uri("avares://NyaLauncher.Avalonia/Themes/HatsuneMiku_Accents.axaml");
                try
                {
                    ApplyFamily(fallbackUri, variant, app);
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[StyleAlter] Fallback theme also failed: {fallbackEx}");
                    throw;
                }
            }
            else
            {
                throw;
            }
        }
    }

    /// <summary>基底 + 家族强调色合并、派生强调键、同步 Material 的一站式入口。</summary>
    private static void ApplyFamily(Uri familyUri, ThemeVariant variant, Application app)
    {
        // 1. 全局基底色板：按明暗模式把对应变体复制进 Application.Resources
        var baseUri = new Uri("avares://NyaLauncher.Avalonia/Themes/BasePalette.axaml");
        if (AvaloniaXamlLoader.Load(baseUri) is ResourceDictionary baseDict &&
            baseDict.ThemeDictionaries.TryGetValue(variant, out var baseProvider) &&
            baseProvider is ResourceDictionary baseVariant)
        {
            foreach (var entry in baseVariant)
                app.Resources[entry.Key] = entry.Value;
        }

        // 2. 家族主题：直接键（强调色 / 次色 / 语义色，不分深浅）
        //    + ThemeDictionaries（家族专属的暗 / 浅背景搭配，按当前模式覆盖基底）
        var familyDict = AvaloniaXamlLoader.Load(familyUri) as ResourceDictionary;
        if (familyDict is not null)
        {
            foreach (var entry in familyDict)
                app.Resources[entry.Key] = entry.Value;

            if (familyDict.ThemeDictionaries.TryGetValue(variant, out var famProvider) &&
                famProvider is ResourceDictionary famVariant)
            {
                foreach (var entry in famVariant)
                    app.Resources[entry.Key] = entry.Value;
            }
        }

        // 3. 按「明暗模式 × 家族强调色」派生强调相关键
        ApplyDerivedAccentKeys(app, variant, familyDict);

        // 4. 同步 Material 强调色（严格取自家族字典，避免跨家族残留串色）
        SyncMaterialTheme(app, variant, familyDict);
    }

    /// <summary>
    /// 派生强调相关资源键。家族只声明强调色阶梯（模式无关），
    /// 而「强调色上的文字」「主按钮底色」「拖放预览」等消费键的取值需要同时
    /// 考虑明暗底色，因此在此按模式从阶梯推导（暗底取亮端，亮底取深端），
    /// Color 与 Brush 两键同时写入（基底 Brush 的 StaticResource 引用在
    /// XAML 解析期已固化，运行时只改 Color 键不会联动 Brush）。
    /// </summary>
    private static void ApplyDerivedAccentKeys(Application app, ThemeVariant variant, ResourceDictionary? family)
    {
        var dark = variant == ThemeVariant.Dark;
        var accent = ExtractColor(family, "AccentColor") ?? Colors.Teal;

        // —— 强调文字 / 链接文字：暗底取亮端保证可读，亮底取深端 ——
        var accentText = dark
            ? ExtractColor(family, "AccentBrightColor") ?? accent
            : ExtractColor(family, "AccentDeepDarkColor") ?? accent;
        SetColorAndBrush(app, "AccentTextColor", "AccentTextBrush", accentText);
        SetColorAndBrush(app, "LinkTextColor", "LinkTextBrush", accentText);

        // —— 主按钮（组件卡片强调槽位）：暗底用深一档更稳重，亮底用主色 ——
        SetColorAndBrush(app, "ComponentPrimaryBgColor", "ComponentPrimaryBgBrush",
            dark ? ExtractColor(family, "AccentDarkerColor") ?? accent : accent);
        SetColorAndBrush(app, "ComponentPrimaryBorderColor", "ComponentPrimaryBorderBrush",
            dark ? ExtractColor(family, "AccentDarkColor") ?? accent
                 : ExtractColor(family, "AccentLightColor") ?? accent);
        SetColorAndBrush(app, "ComponentPrimaryHoverBgColor", "ComponentPrimaryHoverBgBrush",
            dark ? ExtractColor(family, "AccentDarkColor") ?? accent
                 : ExtractColor(family, "AccentBrightColor") ?? accent);

        // —— 工作区拖拽把手激活色：暗底取深一档，亮底取主色 ——
        SetColorAndBrush(app, "DragHandleActiveColor", "DragHandleActiveBrush",
            dark ? ExtractColor(family, "AccentDarkColor") ?? accent : accent);

        // —— 拖放预览：主色 + 固定透明度（暗/亮底同值，半透明自动融合） ——
        SetColorAndBrush(app, "DropPreviewBgColor", "DropPreviewBgBrush",
            Color.FromUInt32(0x38000000 | (accent.ToUInt32() & 0x00FFFFFF)));
        SetColorAndBrush(app, "DropPreviewBorderColor", "DropPreviewBorderBrush",
            dark ? ExtractColor(family, "AccentBrightColor") ?? accent : accent);
        SetColorAndBrush(app, "SidebarDropPreviewBgColor", "SidebarDropPreviewBgBrush",
            Color.FromUInt32(0x40000000 | (accent.ToUInt32() & 0x00FFFFFF)));
        SetColorAndBrush(app, "SidebarDropPreviewBorderColor", "SidebarDropPreviewBorderBrush",
            ExtractColor(family, "AccentLightColor") ?? accent);

        // —— 标准控件 SystemAccent 阶梯：直接映射家族强调色阶梯 ——
        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorDark1"] =
            ExtractColor(family, "AccentDarkColor") ?? accent;
        app.Resources["SystemAccentColorDark2"] =
            ExtractColor(family, "AccentDarkerColor") ?? accent;
        app.Resources["SystemAccentColorLight1"] =
            ExtractColor(family, "AccentLightColor") ?? accent;
    }

    /// <summary>同时写入 Color 与 Brush 两键（画刷供控件绑定，颜色供代码侧取值）。</summary>
    private static void SetColorAndBrush(Application app, string colorKey, string brushKey, Color color)
    {
        app.Resources[colorKey] = color;
        app.Resources[brushKey] = new SolidColorBrush(color);
    }

    /// <summary>
    /// 把明暗模式 + 主题强调色注入全局 Material 主题宿主，使 Material 控件跟随现有主题家族。
    /// <para>
    /// 重要：只允许通过 <c>CurrentTheme</c> 注入！绝对不要设置 <c>MaterialTheme.BaseTheme</c>
    /// 属性——该属性任何变化都会调度内部私有主题（Teal/Pink 枚举占位）在 100ms 后回写
    /// CurrentTheme，把这里注入的家族强调色覆盖掉。中性画刷的明暗由 Theme.Create 传入的
    /// IBaseTheme 自带并全量刷新，无需 BaseTheme 属性参与。
    /// </para>
    /// </summary>
    /// <param name="source">当前家族明暗变体的原始字典；取色只认它，防止跨家族残留串色。</param>
    private static void SyncMaterialTheme(Application app, ThemeVariant variant, ResourceDictionary? source)
    {
        try
        {
            MaterialTheme? materialTheme = null;
            MaterialThemeBase? themeBase = null;
            foreach (var style in app.Styles)
            {
                if (style is MaterialTheme t && materialTheme is null)
                    materialTheme = t;
                else if (style is MaterialThemeBase b && themeBase is null)
                    themeBase = b;
            }
            themeBase ??= FindMaterialThemeBase(app);
            if (materialTheme is null && themeBase is null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[StyleAlter] Material 主题宿主未找到，Material 控件将使用默认主题（可能显示异常）");
                return;
            }

            IBaseTheme baseTheme = variant == ThemeVariant.Dark ? Theme.Dark : Theme.Light;

            // 强调色严格取自当前家族强调色包：主色 AccentColor；
            // 次色为家族特色色 SecondaryAccentColor（如初音粉），缺失时退到 AccentLightColor / 主色
            var primary = ExtractColor(source, "AccentColor") ?? Colors.Teal;
            var secondary =
                ExtractColor(source, "SecondaryAccentColor") ??
                ExtractColor(source, "AccentLightColor") ??
                primary;

            var theme = Theme.Create(baseTheme, primary, secondary);
            var themeHost = (MaterialThemeBase?)materialTheme ?? themeBase;
            if (themeHost is not null)
            {
                // Material.Avalonia 3.19.0 内部缺陷：MaterialThemeBase 在后台主题更新任务中
                // 直接访问 ThemeDictionaries[Default]，未初始化时抛 KeyNotFoundException
                // （经 UnobservedTaskException 污染崩溃日志）。预先补一个空 Default 字典兜底。
                var themeDictionaries = themeHost.Resources.ThemeDictionaries;
                if (!themeDictionaries.ContainsKey(ThemeVariant.Default))
                    themeDictionaries[ThemeVariant.Default] = new ResourceDictionary();
            }
            if (materialTheme is not null)
                InjectWithStartupGuard(materialTheme, theme);
            if (themeBase is not null && !ReferenceEquals(themeBase, materialTheme))
                themeBase.CurrentTheme = theme;

            System.Diagnostics.Debug.WriteLine(
                $"[StyleAlter] MaterialTheme synced: variant={variant}, primary={primary}, secondary={secondary}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StyleAlter] Sync MaterialTheme failed: {ex}");
        }
    }

    /// <summary>从字典读取颜色资源，类型不符时返回 null（fallback 由调用方处理）。</summary>
    private static Color? ExtractColor(ResourceDictionary? dict, string key)
        => dict?.TryGetValue(key, out var value) == true && value is Color color ? color : null;

    /// <summary>
    /// CurrentTheme 注入守卫。处理启动期竞态：注入若早于首帧，
    /// MaterialThemeBase.OnResourcedAccessed 会在首个控件首次查询主题资源时
    /// 用 XAML 占位主题（Teal/Pink 枚举色）同步回写 CurrentTheme，吞掉注入
    /// ——表现为“重启后 Material 控件回到默认绿”。这里在注入后订阅
    /// CurrentThemeChanged，一旦发现实际值与目标不符立即补注一次；
    /// 守卫触发的补注必然与目标一致而自然止停，OnResourcedAccessed 全程仅
    /// 运行一次，故不会形成循环。
    /// </summary>
    private static IDisposable? _startupGuardSubscription;

    private static void InjectWithStartupGuard(MaterialThemeBase host, ITheme target)
    {
        _startupGuardSubscription?.Dispose();
        host.CurrentTheme = target;

        _startupGuardSubscription = host.CurrentThemeChanged.Subscribe(new GuardObserver(host, target));
    }

    /// <summary>守卫观察者：CurrentTheme 实际值偏离目标（典型为占位主题回写）时补注一次。</summary>
    private sealed class GuardObserver : IObserver<IReadOnlyTheme>
    {
        private readonly MaterialThemeBase _host;
        private readonly ITheme _target;

        public GuardObserver(MaterialThemeBase host, ITheme target)
        {
            _host = host;
            _target = target;
        }

        public void OnNext(IReadOnlyTheme? value)
        {
            bool matches = value is not null &&
                           value.PrimaryMid.Color == _target.PrimaryMid.Color &&
                           value.SecondaryMid.Color == _target.SecondaryMid.Color;
            if (matches)
                return;

            System.Diagnostics.Debug.WriteLine(
                "[StyleAlter] Detected placeholder theme overwrite on startup, re-injecting family colors.");
            _host.CurrentTheme = _target;
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    /// <summary>定位全局 Material 主题宿主；扩展方法不可用时返回 null（调用方按缺失处理）。</summary>
    private static MaterialThemeBase? FindMaterialThemeBase(Application app)
    {
        try
        {
            return app.LocateMaterialTheme<MaterialThemeBase>();
        }
        catch
        {
            return null;
        }
    }
}
