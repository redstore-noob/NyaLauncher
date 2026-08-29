using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NyaLauncher.Avalonia.Animations.Helpers;
using NyaLauncher.Avalonia.Framework;
using NyaLauncher.Plugin.Abstractions.Components;

namespace NyaLauncher.Avalonia.Controls;

public enum PolygonComponentVisualState
{
    Normal,
    Hovered,
    DragPreview,
    LibraryPreview
}

/// <summary>
/// Avalonia host for the UI-framework-neutral polygon component contract.
/// Geometry, clipping, element layout, state revisions and asynchronous action
/// execution are owned by the launcher rather than by individual plugins.
/// </summary>
public sealed class PolygonComponentView : UserControl
{
    private readonly PolygonComponentDefinition _definition;
    private readonly IPolygonComponentInstance? _instance;
    private readonly ComponentStateSnapshotter _stateSnapshotter;
    private readonly bool _interactive;
    private readonly Grid _root;
    private readonly global::Avalonia.Controls.Shapes.Path _surface;
    private readonly Canvas _elementCanvas;
    private readonly global::Avalonia.Media.DropShadowEffect _shadow = new()
    {
        Color = Colors.Black,
        BlurRadius = 10,
        OffsetX = 0,
        OffsetY = 2,
        Opacity = 0.30
    };
    private readonly Dictionary<string, ElementVisual> _elementVisuals =
        new(StringComparer.OrdinalIgnoreCase);

    // 文本输入元素的当前值读取器：让按钮/表面动作也能携带输入值
    private readonly Dictionary<string, Func<string>> _inputValueReaders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _runningActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingStateGate = new();
    private long _appliedRevision = long.MinValue;
    private ComponentStateSnapshot _appliedState = ComponentStateSnapshot.Empty;
    private ComponentStateSnapshot? _pendingState;
    private bool _stateDispatchScheduled;
    private PolygonComponentVisualState _visualState;
    private bool _subscribedToState;

    public Control DragHandle { get; }

    public PolygonComponentDefinition Definition => _definition;

    public PolygonComponentView(
        PolygonComponentRegistration registration,
        IPolygonComponentInstance? instance = null,
        PolygonComponentVisualState visualState = PolygonComponentVisualState.Normal,
        bool interactive = true)
    {
        ArgumentNullException.ThrowIfNull(registration);

        _definition = PolygonComponentValidator.ValidateAndSnapshot(
            registration.Definition);
        _instance = instance;
        _interactive = interactive;
        _visualState = visualState;

        Width = _definition.PreferredSize.Width;
        Height = _definition.PreferredSize.Height;
        Focusable = interactive && !string.IsNullOrWhiteSpace(_definition.SurfaceActionId);

        _surface = new global::Avalonia.Controls.Shapes.Path
        {
            Stretch = Stretch.None,
            IsHitTestVisible = false,
            StrokeJoin = PenLineJoin.Round
        };
        _elementCanvas = new Canvas
        {
            Background = Brushes.Transparent
        };
        _root = new Grid
        {
            ClipToBounds = true,
            Children =
            {
                _surface,
                _elementCanvas
            }
        };
        Content = _root;
        // Material elevation：阴影挂在视图自身（不裁剪），随视觉状态抬升
        Effect = _shadow;

        foreach (var element in _definition.Elements.OrderBy(element => element.ZIndex))
            AddElement(element);

        _stateSnapshotter = new ComponentStateSnapshotter(
            _elementVisuals.Keys,
            _definition.Actions.Select(action => action.Id));

        DragHandle = CreateDragHandle();
        _elementCanvas.Children.Add(DragHandle);
        DragHandle.ZIndex = int.MaxValue;

        AutomationProperties.SetAutomationId(this, $"PolygonComponent_{_definition.Id}");
        AutomationProperties.SetName(this, _definition.Title);
        ToolTip.SetTip(this, _definition.Description);

        SizeChanged += (_, _) => UpdateGeometryAndLayout();
        AttachedToVisualTree += (_, _) =>
        {
            AttachRuntime();
            // 组件放置动效：挂载时轻微弹起，优雅"落下"（不影响 Opacity 状态）
            _ = AnimationHelper.BounceAsync(this, 1.04, 300);
        };
        DetachedFromVisualTree += (_, _) => DetachRuntime();
        Tapped += OnTapped;
        KeyDown += OnKeyDown;

        ApplyVisualState();
        ApplyState(ReadCurrentState());
        Dispatcher.UIThread.Post(UpdateGeometryAndLayout, DispatcherPriority.Loaded);
    }

    public void SetVisualState(PolygonComponentVisualState state)
    {
        if (_visualState == state)
            return;

        _visualState = state;
        ApplyVisualState();
    }

    public bool ContainsPoint(Point localPoint)
    {
        var width = Bounds.Width > 0 ? Bounds.Width : Width;
        var height = Bounds.Height > 0 ? Bounds.Height : Height;
        if (width <= 0 || height <= 0 ||
            localPoint.X < 0 || localPoint.Y < 0 ||
            localPoint.X > width || localPoint.Y > height)
        {
            return false;
        }

        return _definition.Shape.Contains(new ComponentPoint(
            localPoint.X / width,
            localPoint.Y / height));
    }

    private void AttachRuntime()
    {
        if (_instance is null)
        {
            RefreshAppliedState();
            return;
        }
        if (_subscribedToState)
            return;

        try
        {
            _instance.StateChanged += OnInstanceStateChanged;
            _subscribedToState = true;
        }
        catch
        {
            ToolTip.SetTip(this, "组件状态订阅失败。");
            RefreshAppliedState();
            return;
        }

        ApplyState(ReadCurrentState());
        // Detach releases native image resources. A view can later be attached
        // again with the same revision, so replay the retained full snapshot.
        RefreshAppliedState();
    }

    private void DetachRuntime()
    {
        if (_instance is not null && _subscribedToState)
        {
            try
            {
                _instance.StateChanged -= OnInstanceStateChanged;
            }
            catch
            {
                // Plugin event accessors must not break Avalonia tree teardown.
            }

            _subscribedToState = false;
        }

        lock (_pendingStateGate)
            _pendingState = null;

        foreach (var visual in _elementVisuals.Values)
        {
            try
            {
                visual.Cleanup?.Invoke();
            }
            catch
            {
                // Native image teardown must not break Avalonia tree teardown.
            }
        }

    }

    private void OnInstanceStateChanged(object? sender, ComponentStateChangedEventArgs e)
    {
        ComponentStateSnapshot state;
        try
        {
            state = _stateSnapshotter.Snapshot(e.State);
        }
        catch
        {
            // A malformed or concurrently mutated plugin snapshot is ignored.
            return;
        }
        if (state.Revision <= Volatile.Read(ref _appliedRevision))
            return;
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyState(state);
            return;
        }

        lock (_pendingStateGate)
        {
            if (_pendingState is null || state.Revision > _pendingState.Revision)
                _pendingState = state;
            if (_stateDispatchScheduled)
                return;

            _stateDispatchScheduled = true;
        }

        Dispatcher.UIThread.Post(ApplyPendingState);
    }

    private void ApplyPendingState()
    {
        ComponentStateSnapshot? state;
        lock (_pendingStateGate)
        {
            state = _pendingState;
            _pendingState = null;
            _stateDispatchScheduled = false;
        }

        if (state is not null)
            ApplyState(state);
    }

    private void ApplyState(ComponentStateSnapshot state)
    {
        if (state.Revision <= _appliedRevision)
            return;

        _appliedRevision = state.Revision;
        _appliedState = state;
        RefreshAppliedState();
    }

    private void RefreshAppliedState()
    {
        var elements = _appliedState.Elements ?? ComponentStateSnapshot.Empty.Elements;
        foreach (var (elementId, visual) in _elementVisuals)
        {
            elements.TryGetValue(elementId, out var elementState);
            visual.Update(elementState);
        }
    }

    private void AddElement(ComponentElementDefinition element)
    {
        ElementVisual visual = element switch
        {
            TextElementDefinition text => CreateTextElement(text),
            ProgressElementDefinition progress => CreateProgressElement(progress),
            TextInputElementDefinition input => CreateTextInputElement(input),
            ToggleElementDefinition toggle => CreateToggleElement(toggle),
            SliderElementDefinition slider => CreateSliderElement(slider),
            ImageElementDefinition image => CreateImageElement(image),
            ButtonElementDefinition button => CreateButtonElement(button),
            DropdownElementDefinition dropdown => CreateDropdownElement(dropdown),
            _ => throw new NotSupportedException(
                $"不支持的多边形组件元素：{element.GetType().Name}")
        };

        visual.Control.ZIndex = element.ZIndex;
        _elementCanvas.Children.Add(visual.Control);
        _elementVisuals[element.Id] = visual;
    }

    private ElementVisual CreateTextElement(TextElementDefinition definition)
    {
        var block = new TextBlock
        {
            TextWrapping = definition.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = definition.Wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = definition.FontSize
        };
        ApplyTextRole(block, definition.Role);
        // 图标前景色与文本角色一致：Caption 用次要色，其余用主要色
        var iconForegroundKey = SlotResourceKey(definition.Role == ComponentTextRole.Caption
            ? "TextSecondary"
            : "TextPrimary");
        // 外层宿主：文本以 "material:" 开头时渲染为 Material 图标，其余走 Minecraft 文本标记
        var host = new ContentControl
        {
            VerticalContentAlignment = VerticalAlignment.Center
        };
        SetAutomationName(host, definition);

        return new ElementVisual(host, definition, state =>
        {
            var text = state?.Text ?? definition.Text;
            if (!string.IsNullOrWhiteSpace(text) &&
                text.StartsWith(FeatureIconFactory.MaterialPrefix, StringComparison.OrdinalIgnoreCase))
            {
                host.Content = FeatureIconFactory.CreateGlyph(
                    text,
                    definition.FontSize,
                    iconForegroundKey);
            }
            else
            {
                MinecraftTextMarkup.Apply(block, text);
                host.Content = block;
            }

            host.IsVisible = state?.IsVisible ?? definition.IsVisible;
            host.Opacity = state?.IsEnabled == false ? 0.45 : 1;
        });
    }

    private ElementVisual CreateProgressElement(ProgressElementDefinition definition)
    {
        var displayedValue = definition.Value;
        var label = new TextBlock
        {
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        label[!TextBlock.ForegroundProperty] = SlotBinding("TextSecondary");
        var valueText = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        valueText[!TextBlock.ForegroundProperty] = SlotBinding("TextPrimary");
        Grid.SetColumn(valueText, 1);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { label, valueText }
        };
        var progress = new ProgressBar
        {
            Minimum = definition.Minimum,
            Maximum = definition.Maximum,
            Height = 7
        };
        progress[!ProgressBar.ForegroundProperty] = SlotBinding("Accent");
        progress[!ProgressBar.BackgroundProperty] = SlotBinding("ProgressTrack");
        // 数值变化经渲染线程平滑过渡（M3 emphasized-decelerate），替代瞬时跳变
        progress.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = RangeBase.ValueProperty,
                Duration = TimeSpan.FromMilliseconds(MaterialMotion.MediumTransitionMs),
                Easing = MaterialMotion.EmphasizedDecelerateEasing
            }
        };
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 4,
            Children = { header, progress }
        };
        Grid.SetRow(progress, 1);
        SetAutomationName(panel, definition);

        return new ElementVisual(panel, definition, state =>
        {
            if (state?.ProgressValue is double candidate)
            {
                if (double.IsFinite(candidate))
                {
                    displayedValue = Math.Clamp(
                        candidate,
                        definition.Minimum,
                        definition.Maximum);
                }
            }
            else
            {
                displayedValue = definition.Value;
            }

            label.Text = state?.Text ?? definition.Label;
            progress.Value = displayedValue;
            progress.IsIndeterminate = state?.IsIndeterminate ?? definition.IsIndeterminate;
            panel.IsVisible = state?.IsVisible ?? definition.IsVisible;
            progress.IsEnabled = state?.IsEnabled ?? true;
            valueText.Text = definition.ShowPercentage && !progress.IsIndeterminate
                ? $"{(displayedValue - definition.Minimum) /
                     (definition.Maximum - definition.Minimum):P0}"
                : string.Empty;
        });
    }

    private ElementVisual CreateTextInputElement(TextInputElementDefinition definition)
    {
        var input = new TextBox
        {
            Text = definition.Value,
            PlaceholderText = definition.Placeholder,
            MaxLength = definition.MaximumLength,
            AcceptsReturn = definition.IsMultiline,
            TextWrapping = definition.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalContentAlignment = definition.IsMultiline
                ? VerticalAlignment.Top
                : VerticalAlignment.Center,
            Padding = new Thickness(8, 5),
            FontSize = 11,
            CornerRadius = new CornerRadius(7)
        };
        input[!TextBox.BackgroundProperty] = SlotBinding("ProgressTrack");
        input[!TextBox.ForegroundProperty] = SlotBinding("TextPrimary");
        input[!TextBox.BorderBrushProperty] = SlotBinding("Border");
        SetAutomationName(input, definition);
        _inputValueReaders[definition.Id] = () => input.Text ?? string.Empty;

        var externalValue = definition.Value;
        var locallyEdited = false;
        var applyingState = false;
        input.TextChanged += (_, _) =>
        {
            if (!applyingState)
                locallyEdited = true;
        };
        if (_interactive)
        {
            input.KeyDown += async (_, e) =>
            {
                var submit = e.Key == Key.Enter &&
                    (!definition.IsMultiline || e.KeyModifiers.HasFlag(KeyModifiers.Control));
                if (!submit || !input.IsEnabled)
                    return;

                e.Handled = true;
                await InvokeElementValueActionAsync(
                    definition.ActionId,
                    definition.Id,
                    input.Text ?? string.Empty).ConfigureAwait(true);
            };
        }

        return new ElementVisual(input, definition, state =>
        {
            var candidate = state?.Value ?? definition.Value;
            if (candidate.Length > definition.MaximumLength)
                candidate = candidate[..definition.MaximumLength];

            // A full state snapshot may be republished for unrelated elements.
            // Preserve local editing until the plugin actually changes this value.
            if (!locallyEdited || !string.Equals(candidate, externalValue, StringComparison.Ordinal))
            {
                if (!string.Equals(input.Text, candidate, StringComparison.Ordinal))
                {
                    applyingState = true;
                    input.Text = candidate;
                    applyingState = false;
                }

                locallyEdited = false;
            }

            externalValue = candidate;
            input.IsVisible = state?.IsVisible ?? definition.IsVisible;
            input.IsEnabled = _interactive && _instance is not null &&
                              (state?.IsEnabled ?? true) &&
                              !_runningActions.Contains(definition.ActionId);
        });
    }

    private ElementVisual CreateToggleElement(ToggleElementDefinition definition)
    {
        var toggle = new CheckBox
        {
            Content = definition.Label,
            IsChecked = definition.IsChecked,
            FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        toggle[!CheckBox.ForegroundProperty] = SlotBinding("TextPrimary");
        SetAutomationName(toggle, definition);

        var externalValue = definition.IsChecked;
        var locallyEdited = false;
        long editVersion = 0;
        if (_interactive)
        {
            toggle.Click += async (_, _) =>
            {
                if (!toggle.IsEnabled)
                    return;

                locallyEdited = true;
                var submittedVersion = ++editVersion;
                var value = toggle.IsChecked == true;
                await InvokeElementValueActionAsync(
                    definition.ActionId,
                    definition.Id,
                    value ? "true" : "false").ConfigureAwait(true);

                // A command only becomes visual state after the plugin confirms it.
                if (locallyEdited && submittedVersion == editVersion)
                {
                    locallyEdited = false;
                    toggle.IsChecked = externalValue;
                }
            };
        }

        return new ElementVisual(toggle, definition, state =>
        {
            var candidate = state?.IsChecked ?? definition.IsChecked;
            if (!locallyEdited || candidate != externalValue)
            {
                toggle.IsChecked = candidate;
                locallyEdited = false;
            }

            externalValue = candidate;
            toggle.Content = state?.Text ?? definition.Label;
            toggle.IsVisible = state?.IsVisible ?? definition.IsVisible;
            toggle.IsEnabled = _interactive && _instance is not null &&
                               (state?.IsEnabled ?? true) &&
                               !_runningActions.Contains(definition.ActionId);
        });
    }

    private ElementVisual CreateSliderElement(SliderElementDefinition definition)
    {
        var label = new TextBlock
        {
            Text = definition.Label,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        label[!TextBlock.ForegroundProperty] = SlotBinding("TextSecondary");
        var valueText = new TextBlock
        {
            Text = FormatSliderLabel(definition.Value),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        valueText[!TextBlock.ForegroundProperty] = SlotBinding("TextPrimary");
        Grid.SetColumn(valueText, 1);
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { label, valueText }
        };
        var slider = new Slider
        {
            Minimum = definition.Minimum,
            Maximum = definition.Maximum,
            Value = definition.Value,
            TickFrequency = definition.Step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 2,
            Children = { header, slider }
        };
        Grid.SetRow(slider, 1);
        SetAutomationName(panel, definition);

        var externalValue = definition.Value;
        var locallyEdited = false;
        var applyingState = false;
        long editVersion = 0;
        CancellationTokenSource? pendingSubmit = null;
        slider.ValueChanged += (_, _) =>
        {
            valueText.Text = FormatSliderLabel(slider.Value);
            if (applyingState || !_interactive)
                return;

            locallyEdited = true;
            var submittedVersion = ++editVersion;
            var submittedValue = slider.Value;
            pendingSubmit?.Cancel();
            pendingSubmit = new CancellationTokenSource();
            _ = SubmitSliderValueAsync(pendingSubmit, submittedVersion, submittedValue);
        };

        async Task SubmitSliderValueAsync(
            CancellationTokenSource submission,
            long submittedVersion,
            double submittedValue)
        {
            try
            {
                await Task.Delay(150, submission.Token).ConfigureAwait(true);
                await InvokeElementValueActionAsync(
                    definition.ActionId,
                    definition.Id,
                    SerializeComponentNumber(submittedValue)).ConfigureAwait(true);

                // Revert an unconfirmed optimistic value after command completion.
                if (locallyEdited && submittedVersion == editVersion)
                {
                    locallyEdited = false;
                    applyingState = true;
                    slider.Value = externalValue;
                    applyingState = false;
                    valueText.Text = FormatSliderLabel(slider.Value);
                }
            }
            catch (OperationCanceledException)
            {
                // A newer slider value or visual-tree detach superseded this one.
            }
            finally
            {
                if (ReferenceEquals(pendingSubmit, submission))
                    pendingSubmit = null;
                submission.Dispose();
            }
        }

        return new ElementVisual(panel, definition, state =>
        {
            var candidate = ParseSliderValue(state?.Value, definition);
            if (!locallyEdited || !candidate.Equals(externalValue))
            {
                if (locallyEdited && !candidate.Equals(externalValue))
                {
                    // An authoritative plugin update supersedes a queued local value.
                    editVersion++;
                    pendingSubmit?.Cancel();
                }

                if (!slider.Value.Equals(candidate))
                {
                    applyingState = true;
                    slider.Value = candidate;
                    applyingState = false;
                }

                locallyEdited = false;
            }

            externalValue = candidate;
            label.Text = state?.Text ?? definition.Label;
            valueText.Text = FormatSliderLabel(slider.Value);
            panel.IsVisible = state?.IsVisible ?? definition.IsVisible;
            slider.IsEnabled = _interactive && _instance is not null &&
                               (state?.IsEnabled ?? true) &&
                               !_runningActions.Contains(definition.ActionId);
        }, () =>
        {
            var submission = pendingSubmit;
            pendingSubmit = null;
            submission?.Cancel();
        });
    }

    private ElementVisual CreateImageElement(ImageElementDefinition definition)
    {
        // 图片加载失败时的回退字形："material:Kind" 渲染为 Material 图标，其余回退文字
        var fallback = new ContentControl
        {
            Content = FeatureIconFactory.CreateGlyph(
                definition.FallbackText, 18, SlotResourceKey("TextSecondary"))
        };
        var image = new Image
        {
            Stretch = definition.Stretch switch
            {
                ComponentImageStretch.None => Stretch.None,
                ComponentImageStretch.Fill => Stretch.Fill,
                ComponentImageStretch.Uniform => Stretch.Uniform,
                _ => Stretch.UniformToFill
            },
            IsVisible = false
        };
        RenderOptions.SetBitmapInterpolationMode(
            image,
            definition.Pixelated
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.HighQuality);

        var border = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(definition.CornerRadius),
            ClipToBounds = true,
            Child = new Grid { Children = { fallback, image } }
        };
        SetAutomationName(border, definition);

        Bitmap? currentBitmap = null;
        CancellationTokenSource? currentLoad = null;
        var currentSource = string.Empty;
        long? currentToken = null;

        async Task LoadAsync(string source, CancellationToken cancellationToken)
        {
            try
            {
                var bytes = await ComponentImageLoader.LoadBytesAsync(source, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = new MemoryStream(bytes, writable: false);
                    var bitmap = new Bitmap(stream);
                    IImage displayed = bitmap;
                    if (definition.IsSkinHead)
                    {
                        // 皮肤贴图自动合成为双层头像（脸层 + 帽层）
                        displayed = SkinHeadComposer.Compose(bitmap) ?? bitmap;
                    }
                    else if (definition.SourcePixelRect is not null || definition.SourceRect is not null)
                    {
                        displayed = new CroppedBitmap
                        {
                            Source = bitmap,
                            SourceRect = ResolveImageSourceRect(definition, bitmap.PixelSize)
                        };
                    }

                    var previous = currentBitmap;
                    currentBitmap = bitmap;
                    image.Source = displayed;
                    image.IsVisible = true;
                    fallback.IsVisible = false;
                    previous?.Dispose();
                });
            }
            catch (OperationCanceledException)
            {
                // A newer state superseded this image load.
            }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        currentSource = string.Empty;
                        image.Source = null;
                        image.IsVisible = false;
                        fallback.IsVisible = true;
                    }
                });
            }
        }

        void ReleaseImage()
        {
            currentSource = string.Empty;
            currentToken = null;
            currentLoad?.Cancel();
            currentLoad?.Dispose();
            currentLoad = null;
            image.Source = null;
            image.IsVisible = false;
            fallback.IsVisible = true;
            currentBitmap?.Dispose();
            currentBitmap = null;
        }

        return new ElementVisual(border, definition, state =>
        {
            border.IsVisible = state?.IsVisible ?? definition.IsVisible;
            border.Opacity = state?.IsEnabled == false ? 0.45 : 1;
            // 回退字形随状态刷新："material:Kind" 渲染为 Material 图标，其余回退文字
            fallback.Content = FeatureIconFactory.CreateGlyph(
                state?.Text ?? definition.FallbackText,
                18,
                SlotResourceKey("TextSecondary"));
            var source = ComponentImageLoader.SnapshotSource(
                state?.ImageSource ?? definition.Source);
            if (string.Equals(source, currentSource, StringComparison.Ordinal) &&
                state?.ImageRefreshToken == currentToken)
                return;

            ReleaseImage();
            currentSource = source;
            currentToken = state?.ImageRefreshToken;
            if (source.Length == 0)
                return;

            currentLoad = new CancellationTokenSource();
            _ = LoadAsync(source, currentLoad.Token);
        }, ReleaseImage);
    }

    internal static PixelRect ResolveImageSourceRect(
        ImageElementDefinition definition,
        PixelSize pixelSize)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));

        if (definition.SourcePixelRect is { } pixels)
        {
            return ClampImageSourceRect(
                pixels.X,
                pixels.Y,
                pixels.Width,
                pixels.Height,
                pixelSize);
        }

        if (definition.SourceRect is { } normalized)
        {
            return ClampImageSourceRect(
                (int)Math.Round(normalized.X * pixelSize.Width),
                (int)Math.Round(normalized.Y * pixelSize.Height),
                (int)Math.Round(normalized.Width * pixelSize.Width),
                (int)Math.Round(normalized.Height * pixelSize.Height),
                pixelSize);
        }

        return new PixelRect(0, 0, pixelSize.Width, pixelSize.Height);
    }

    private static PixelRect ClampImageSourceRect(
        int x,
        int y,
        int width,
        int height,
        PixelSize pixelSize) =>
        ComponentImageLoader.ClampCropRect(new PixelRect(x, y, width, height), pixelSize);

    private ElementVisual CreateButtonElement(ButtonElementDefinition definition)
    {
        var foregroundKey = SlotResourceKey(definition.IsPrimary
            ? "AccentForeground"
            : "TextPrimary");
        var backgroundKey = SlotResourceKey(definition.IsPrimary
            ? "Accent"
            : "ProgressTrack");
        var button = new Button
        {
            Content = ResolveButtonContent(definition.Glyph, definition.Text, foregroundKey),
            Padding = new Thickness(10, 5),
            CornerRadius = new CornerRadius(8),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button[!Button.BackgroundProperty] = new DynamicResourceExtension(backgroundKey);
        button[!Button.ForegroundProperty] = new DynamicResourceExtension(foregroundKey);
        SetAutomationName(button, definition);
        if (_interactive)
        {
            button.Click += async (_, _) =>
                await InvokeActionAsync(definition.ActionId).ConfigureAwait(true);
        }

        return new ElementVisual(button, definition, state =>
        {
            // 状态文字以 "material:" 开头时（如 material:Play）渲染为 Material 图标
            button.Content = ResolveButtonContent(
                definition.Glyph,
                state?.Text ?? definition.Text,
                foregroundKey);
            button.IsVisible = state?.IsVisible ?? definition.IsVisible;
            button.IsEnabled = _interactive && _instance is not null &&
                               (state?.IsEnabled ?? true) &&
                               !_runningActions.Contains(definition.ActionId);
        });
    }

    /// <summary>
    /// 解析按钮显示内容：文字以 "material:" 开头时渲染为 Material 图标（不显示文字）；
    /// 静态字形以 "material:" 开头时渲染为 Material 图标 + 文字组合；
    /// 其余情况保持原有 "字形 + 文字" 组合行为。
    /// foregroundResourceKey：前景色主题资源键，创建的控件绑定该资源（主题切换实时跟随）。
    /// </summary>
    private object? ResolveButtonContent(
        string? glyph,
        string? text,
        string foregroundResourceKey)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            text.StartsWith(FeatureIconFactory.MaterialPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return FeatureIconFactory.CreateGlyph(text, 14, foregroundResourceKey);
        }

        // 静态字形是 Material 图标：渲染图标 + 文字（文字为空时纯图标）
        if (!string.IsNullOrWhiteSpace(glyph) &&
            glyph.StartsWith(FeatureIconFactory.MaterialPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var icon = FeatureIconFactory.CreateGlyph(glyph, 14, foregroundResourceKey);
            if (string.IsNullOrWhiteSpace(text))
                return icon;
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            label[!TextBlock.ForegroundProperty] =
                new DynamicResourceExtension(foregroundResourceKey);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, label }
            };
        }

        return string.IsNullOrWhiteSpace(glyph) ? text : $"{glyph} {text}";
    }

    private ElementVisual CreateDropdownElement(DropdownElementDefinition definition)
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            MaxHeight = 520,
            MinWidth = 230
        };
        var button = new Button
        {
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(7),
            HorizontalContentAlignment = definition.AlignRight
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            ContextMenu = menu
        };
        button[!Button.ForegroundProperty] = SlotBinding("TextSecondary");
        // 字形内容统一走图标工厂："material:ChevronDown" 渲染为 Material 图标，旧符号回退文字；
        // 空字形（如整卡热区不想要图标）显示为空，不回退默认图标
        var glyphForegroundKey = SlotResourceKey("TextSecondary");
        button.Content = CreateDropdownGlyph(definition.Glyph, glyphForegroundKey);
        SetAutomationName(button, definition);
        if (_interactive)
        {
            button.Click += (_, _) =>
            {
                if (!menu.IsOpen && button.IsEnabled)
                    menu.Open(button);
            };
        }

        return new ElementVisual(button, definition, state =>
        {
            var items = definition.PinnedItems
                .Concat(state?.MenuItems ?? [])
                .ToArray();
            menu.ItemsSource = CreateMenuControls(items);
            button.Content = CreateDropdownGlyph(state?.Text ?? definition.Glyph, glyphForegroundKey);
            button.IsVisible = state?.IsVisible ?? definition.IsVisible;
            button.IsEnabled = _interactive && _instance is not null &&
                               (state?.IsEnabled ?? true) && items.Length > 0;
        });
    }

    /// <summary>
    /// 下拉触发按钮的字形内容：空字形显示为空（热区场景不想要图标），
    /// "material:Kind" 渲染为 Material 图标，其余符号回退文字。
    /// foregroundResourceKey：前景色主题资源键（DynamicResource 绑定）。
    /// </summary>
    private static Control? CreateDropdownGlyph(string? glyph, string foregroundResourceKey)
    {
        if (string.IsNullOrWhiteSpace(glyph))
            return null;
        return FeatureIconFactory.CreateGlyph(glyph, 15, foregroundResourceKey);
    }

    private IReadOnlyList<Control> CreateMenuControls(
        IReadOnlyList<ComponentMenuItem> items)
    {
        var controls = new List<Control>(items.Count * 2);
        foreach (var item in items)
        {
            var menuItem = new MenuItem
            {
                Header = CreateMenuItemHeader(item),
                IsEnabled = _interactive && _instance is not null && item.IsEnabled &&
                            !_runningActions.Contains(item.ActionId)
            };
            AutomationProperties.SetAutomationId(menuItem, item.Id);
            AutomationProperties.SetName(menuItem, item.Text);
            if (_interactive)
            {
                menuItem.Click += async (_, _) =>
                    await InvokeActionAsync(item.ActionId, item.Arguments)
                        .ConfigureAwait(true);
            }

            controls.Add(menuItem);
            if (item.SeparatorAfter)
                controls.Add(new Separator());
        }

        return controls;
    }

    private Grid CreateMenuItemHeader(ComponentMenuItem item)
    {
        // 菜单项字形："material:Kind" 渲染为 Material 图标，其余回退文字
        var fallbackGlyph = FeatureIconFactory.CreateGlyph(
            item.Glyph,
            15,
            SlotResourceKey("Accent"));
        var asyncImage = new AsyncImage
        {
            SourceUrl = item.IconSource,
            IsSkinHead = item.IsSkinHead,
            Width = 28,
            Height = 28,
            Stretch = Stretch.Uniform
        };
        // 皮肤头像用最近邻插值放大，保持清晰的 MC 像素风（复用皮肤选择器做法）
        if (item.IsSkinHead)
        {
            RenderOptions.SetBitmapInterpolationMode(
                asyncImage,
                BitmapInterpolationMode.None);
        }
        var iconLayer = new Grid
        {
            Children =
            {
                fallbackGlyph,
                asyncImage
            }
        };
        if (item.IsSelected)
        {
            // 选中标记：✓ 显示在强调色徽章上，前景色跟随主题
            var badge = new Border
            {
                Width = 13,
                Height = 13,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = FeatureIconFactory.CreateGlyph(
                    "material:Check", 8, SlotResourceKey("AccentForeground"))
            };
            badge[!Border.BackgroundProperty] = SlotBinding("Accent");
            iconLayer.Children.Add(badge);
        }
        var icon = new Border
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = iconLayer
        };
        icon[!Border.BackgroundProperty] = new DynamicResourceExtension("ControlBgBrush");
        var titleText = new TextBlock
        {
            Text = item.Text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };
        titleText[!TextBlock.ForegroundProperty] = SlotBinding("TextPrimary");
        var labels = new StackPanel
        {
            MinWidth = 170,
            Spacing = 1,
            Children = { titleText }
        };
        if (!string.IsNullOrWhiteSpace(item.SecondaryText))
        {
            var secondaryText = new TextBlock
            {
                Text = item.SecondaryText,
                FontSize = 10
            };
            secondaryText[!TextBlock.ForegroundProperty] = SlotBinding("TextSecondary");
            labels.Children.Add(secondaryText);
        }

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(2, 3),
            Children = { icon, labels }
        };
        Grid.SetColumn(labels, 1);
        return header;
    }

    private Border CreateDragHandle()
    {
        var glyph = new TextBlock
        {
            Text = "⠿",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        glyph[!TextBlock.ForegroundProperty] = SlotBinding("TextSecondary");
        var handle = new Border
        {
            CornerRadius = new CornerRadius(7),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            IsVisible = false,
            IsHitTestVisible = false,
            Child = glyph
        };
        handle[!Border.BackgroundProperty] =
            new DynamicResourceExtension("DragHandleBgBrush");
        return handle;
    }

    /// <summary>组件动作执行后的反馈消息（成功提示或失败原因），供宿主状态栏展示。</summary>
    public event EventHandler<string>? ActionFeedback;

    private Task<ComponentActionResult?> InvokeActionAsync(string actionId) =>
        InvokeActionAsync(actionId, arguments: null);

    private Task<ComponentActionResult?> InvokeElementValueActionAsync(
        string actionId,
        string elementId,
        string value) =>
        InvokeActionAsync(
            actionId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["elementId"] = elementId,
                ["value"] = value
            });

    private async Task<ComponentActionResult?> InvokeActionAsync(
        string actionId,
        IReadOnlyDictionary<string, string>? arguments)
    {
        if (!_interactive || _instance is null)
            return null;

        // 按钮与表面点击默认不带参数；补上所有文本输入元素的当前值，
        // 使任意动作都能读取输入框内容（显式传入的参数优先）。
        if (_inputValueReaders.Count > 0)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (arguments is not null)
            {
                foreach (var pair in arguments)
                    merged[pair.Key] = pair.Value;
            }

            foreach (var (elementId, reader) in _inputValueReaders)
            {
                if (!merged.ContainsKey(elementId))
                    merged[elementId] = reader();
            }

            arguments = merged;
        }

        var action = _definition.Actions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null || (!action.AllowReentry && !_runningActions.Add(action.Id)))
            return null;

        ApplyState(ReadCurrentState());
        RefreshAppliedState();
        try
        {
            var result = await _instance.InvokeAsync(
                new ComponentActionInvocation(action.Id, arguments),
                CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                ToolTip.SetTip(this, result.Message);
                ActionFeedback?.Invoke(this, result.Message);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            ToolTip.SetTip(this, $"组件命令失败：{exception.Message}");
            ActionFeedback?.Invoke(this, $"组件命令失败：{exception.Message}");
            return ComponentActionResult.Failed(exception.Message);
        }
        finally
        {
            if (!action.AllowReentry)
                _runningActions.Remove(action.Id);
            ApplyState(ReadCurrentState());
            RefreshAppliedState();
        }
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!_interactive || string.IsNullOrWhiteSpace(_definition.SurfaceActionId) ||
            IsInteractiveChild(e.Source as Visual))
        {
            return;
        }

        _ = InvokeActionAsync(_definition.SurfaceActionId);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_interactive || string.IsNullOrWhiteSpace(_definition.SurfaceActionId) ||
            e.Key is not (Key.Enter or Key.Space) ||
            IsInteractiveChild(e.Source as Visual))
        {
            return;
        }

        _ = InvokeActionAsync(_definition.SurfaceActionId);
        e.Handled = true;
    }

    private bool IsInteractiveChild(Visual? source)
    {
        if (source is null)
            return false;
        if (IsInteractiveElement(source) || ReferenceEquals(source, DragHandle))
            return true;

        return source.GetVisualAncestors()
            .TakeWhile(ancestor => !ReferenceEquals(ancestor, this))
            .Any(ancestor => IsInteractiveElement(ancestor) ||
                             ReferenceEquals(ancestor, DragHandle));
    }

    private static bool IsInteractiveElement(Visual visual) =>
        visual is Button or TextBox or CheckBox or Slider;

    private void UpdateGeometryAndLayout()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : Width;
        var height = Bounds.Height > 0 ? Bounds.Height : Height;
        if (width <= 0 || height <= 0)
            return;

        var geometry = CreateGeometry(width, height);
        _surface.Data = geometry;
        _root.Clip = geometry;

        foreach (var visual in _elementVisuals.Values)
            ArrangeControl(visual.Control, visual.Definition.Bounds, width, height);
        ArrangeControl(DragHandle, _definition.DragHandleBounds, width, height);
    }

    private StreamGeometry CreateGeometry(double width, double height)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var first = _definition.Shape.Points[0];
        context.BeginFigure(new Point(first.X * width, first.Y * height), isFilled: true);
        foreach (var point in _definition.Shape.Points.Skip(1))
            context.LineTo(new Point(point.X * width, point.Y * height), isStroked: true);
        context.EndFigure(isClosed: true);
        return geometry;
    }

    private static void ArrangeControl(
        Control control,
        ComponentRect bounds,
        double width,
        double height)
    {
        control.Width = bounds.Width * width;
        control.Height = bounds.Height * height;
        Canvas.SetLeft(control, bounds.X * width);
        Canvas.SetTop(control, bounds.Y * height);
    }

    private void ApplyVisualState()
    {
        var hover = _visualState == PolygonComponentVisualState.Hovered;
        // 表面/边框绑定主题资源，悬停时切换到对应悬停槽位（主题切换实时跟随）
        _surface[!Shape.FillProperty] = SlotBinding(hover ? "SurfaceHover" : "Surface");
        _surface[!Shape.StrokeProperty] = SlotBinding(hover ? "BorderHover" : "Border");
        _surface.StrokeThickness = hover
            ? Math.Max(2, _definition.Theme.BorderThickness)
            : _definition.Theme.BorderThickness;
        // Material elevation 提升：悬浮时阴影更重更远
        _shadow.BlurRadius = hover ? 22 : 10;
        _shadow.OffsetY = hover ? 6 : 2;
        _shadow.Opacity = hover ? 0.5 : 0.3;
        Opacity = _visualState switch
        {
            PolygonComponentVisualState.DragPreview => 0.68,
            PolygonComponentVisualState.LibraryPreview => 0.9,
            _ => 1
        };
    }

    private void ApplyTextRole(TextBlock block, ComponentTextRole role)
    {
        block[!TextBlock.ForegroundProperty] = SlotBinding(
            role == ComponentTextRole.Caption ? "TextSecondary" : "TextPrimary");
        block.FontWeight = role switch
        {
            ComponentTextRole.Title => FontWeight.Bold,
            ComponentTextRole.Emphasis => FontWeight.SemiBold,
            _ => FontWeight.Normal
        };
    }

    private static void SetAutomationName(Control control, ComponentElementDefinition definition)
    {
        AutomationProperties.SetAutomationId(control, definition.Id);
        AutomationProperties.SetName(
            control,
            string.IsNullOrWhiteSpace(definition.AutomationName)
                ? definition.Id
                : definition.AutomationName);
    }

    /// <summary>
    /// 组件主题槽位 → 全局主题资源键：组件不再自带颜色，
    /// 所有画刷绑定主题资源（DynamicResource），主题切换实时跟随。
    /// </summary>
    private string SlotResourceKey(string slot)
    {
        var variant = _definition.Theme.Variant;
        return (variant, slot) switch
        {
            (ComponentThemeVariant.Launch, "Surface") => "ComponentPrimaryBgBrush",
            (ComponentThemeVariant.Launch, "SurfaceHover") => "ComponentPrimaryHoverBgBrush",
            (ComponentThemeVariant.Launch, "Border") => "ComponentPrimaryBorderBrush",
            (ComponentThemeVariant.Launch, "BorderHover") => "ComponentPrimaryBorderBrush",
            (ComponentThemeVariant.Launch, "TextPrimary") => "WhiteBrush",
            (ComponentThemeVariant.Launch, "TextSecondary") => "WhiteBrush",
            (ComponentThemeVariant.Launch, "Accent") => "WhiteBrush",
            (ComponentThemeVariant.Launch, "AccentForeground") => "ComponentPrimaryBgBrush",
            (ComponentThemeVariant.Launch, "ProgressTrack") => "ComponentPrimaryHoverBgBrush",
            (_, "Surface") => "ComponentBgBrush",
            (_, "SurfaceHover") => "ComponentHoverBgBrush",
            (_, "Border") => "ComponentBorderBrush",
            (_, "BorderHover") => "ComponentPrimaryBorderBrush",
            (_, "TextPrimary") => "PrimaryTextBrush",
            (_, "TextSecondary") => "SecondaryTextBrush",
            (_, "Accent") => "AccentBrush",
            (_, "AccentForeground") => "WhiteBrush",
            (_, "ProgressTrack") => "ControlBgBrush",
            _ => "ComponentBgBrush"
        };
    }

    /// <summary>槽位对应的 DynamicResource 绑定（直接赋给控件索引器即可）。</summary>
    private DynamicResourceExtension SlotBinding(string slot) =>
        new(SlotResourceKey(slot));

    private static double ParseSliderValue(
        string? value,
        SliderElementDefinition definition)
    {
        if (value is not null &&
            double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            double.IsFinite(parsed))
        {
            return Math.Clamp(parsed, definition.Minimum, definition.Maximum);
        }

        return definition.Value;
    }

    private static string FormatSliderLabel(double value) =>
        value.ToString("G6", CultureInfo.InvariantCulture);

    private static string SerializeComponentNumber(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private ComponentStateSnapshot ReadCurrentState()
    {
        if (_instance is null)
            return ComponentStateSnapshot.Empty;

        try
        {
            return _stateSnapshotter.Snapshot(_instance.CurrentState);
        }
        catch
        {
            return _appliedState;
        }
    }

    private sealed record ElementVisual(
        Control Control,
        ComponentElementDefinition Definition,
        Action<ComponentElementState?> Update,
        Action? Cleanup = null);
}
