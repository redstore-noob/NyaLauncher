using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private readonly Dictionary<string, ElementVisual> _elementVisuals =
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
        AttachedToVisualTree += (_, _) => AttachRuntime();
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
        SetAutomationName(block, definition);

        return new ElementVisual(block, definition, state =>
        {
            block.Text = state?.Text ?? definition.Text;
            block.IsVisible = state?.IsVisible ?? definition.IsVisible;
            block.Opacity = state?.IsEnabled == false ? 0.45 : 1;
        });
    }

    private ElementVisual CreateProgressElement(ProgressElementDefinition definition)
    {
        var displayedValue = definition.Value;
        var label = new TextBlock
        {
            FontSize = 10,
            Foreground = ParseBrush(_definition.Theme.TextSecondary, "#A5AEC7"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var valueText = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = ParseBrush(_definition.Theme.TextPrimary, "#F6F7FF"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
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
            Height = 7,
            Foreground = ParseBrush(_definition.Theme.Accent, "#6C7BFF"),
            Background = ParseBrush(_definition.Theme.ProgressTrack, "#30384F")
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
            Background = ParseBrush(_definition.Theme.ProgressTrack, "#30384F"),
            Foreground = ParseBrush(_definition.Theme.TextPrimary, "#F6F7FF"),
            BorderBrush = ParseBrush(_definition.Theme.Border, "#3A4563"),
            CornerRadius = new CornerRadius(7)
        };
        SetAutomationName(input, definition);

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
            Foreground = ParseBrush(_definition.Theme.TextPrimary, "#F6F7FF"),
            VerticalContentAlignment = VerticalAlignment.Center
        };
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
            Foreground = ParseBrush(_definition.Theme.TextSecondary, "#A5AEC7"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var valueText = new TextBlock
        {
            Text = FormatSliderLabel(definition.Value),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = ParseBrush(_definition.Theme.TextPrimary, "#F6F7FF"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
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
        var fallback = new TextBlock
        {
            Text = definition.FallbackText,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = ParseBrush(_definition.Theme.TextSecondary, "#A5AEC7"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
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
                    if (definition.SourcePixelRect is not null || definition.SourceRect is not null)
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
            fallback.Text = state?.Text ?? definition.FallbackText;
            var source = ComponentImageLoader.SnapshotSource(
                state?.ImageSource ?? definition.Source);
            if (string.Equals(source, currentSource, StringComparison.Ordinal))
                return;

            ReleaseImage();
            currentSource = source;
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
        var button = new Button
        {
            Content = string.IsNullOrWhiteSpace(definition.Glyph)
                ? definition.Text
                : $"{definition.Glyph} {definition.Text}",
            Padding = new Thickness(10, 5),
            CornerRadius = new CornerRadius(8),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = definition.IsPrimary
                ? ParseBrush(_definition.Theme.Accent, "#6C7BFF")
                : ParseBrush(_definition.Theme.ProgressTrack, "#30384F"),
            Foreground = definition.IsPrimary
                ? ParseBrush(_definition.Theme.AccentForeground, "#FFFFFF")
                : ParseBrush(_definition.Theme.TextPrimary, "#F6F7FF"),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        SetAutomationName(button, definition);
        if (_interactive)
        {
            button.Click += async (_, _) =>
                await InvokeActionAsync(definition.ActionId).ConfigureAwait(true);
        }

        return new ElementVisual(button, definition, state =>
        {
            button.Content = state?.Text ??
                (string.IsNullOrWhiteSpace(definition.Glyph)
                    ? definition.Text
                    : $"{definition.Glyph} {definition.Text}");
            button.IsVisible = state?.IsVisible ?? definition.IsVisible;
            button.IsEnabled = _interactive && _instance is not null &&
                               (state?.IsEnabled ?? true) &&
                               !_runningActions.Contains(definition.ActionId);
        });
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
            Content = definition.Glyph,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(7),
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Foreground = ParseBrush(_definition.Theme.TextSecondary, "#A5AEC7"),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            ContextMenu = menu
        };
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
            button.Content = state?.Text ?? definition.Glyph;
            button.IsVisible = state?.IsVisible ?? definition.IsVisible;
            button.IsEnabled = _interactive && _instance is not null &&
                               (state?.IsEnabled ?? true) && items.Length > 0;
        });
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
        var fallbackGlyph = new TextBlock
        {
            Text = item.Glyph,
            FontSize = 15,
            Foreground = ParseBrush(_definition.Theme.Accent, "#6C7BFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var iconLayer = new Grid
        {
            Children =
            {
                fallbackGlyph,
                new AsyncImage
                {
                    SourceUrl = item.IconSource,
                    Width = 28,
                    Height = 28,
                    Stretch = Stretch.Uniform
                }
            }
        };
        if (item.IsSelected)
        {
            iconLayer.Children.Add(new Border
            {
                Width = 13,
                Height = 13,
                CornerRadius = new CornerRadius(7),
                Background = ParseBrush(_definition.Theme.Accent, "#6C7BFF"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = "✓",
                    FontSize = 8,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
        var icon = new Border
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(6),
            Background = ParseBrush("#222A3D", "#222A3D"),
            ClipToBounds = true,
            Child = iconLayer
        };
        var labels = new StackPanel
        {
            MinWidth = 170,
            Spacing = 1,
            Children =
            {
                new TextBlock
                {
                    Text = item.Text,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ParseBrush(_definition.Theme.TextPrimary, "#F6F7FF")
                }
            }
        };
        if (!string.IsNullOrWhiteSpace(item.SecondaryText))
        {
            labels.Children.Add(new TextBlock
            {
                Text = item.SecondaryText,
                FontSize = 10,
                Foreground = ParseBrush(_definition.Theme.TextSecondary, "#A5AEC7")
            });
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
        return new Border
        {
            Background = ParseBrush("#332A3042", "#332A3042"),
            CornerRadius = new CornerRadius(7),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            IsVisible = false,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "⠿",
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = ParseBrush(_definition.Theme.TextSecondary, "#A5AEC7"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

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
            if (!result.Success && !string.IsNullOrWhiteSpace(result.Message))
                ToolTip.SetTip(this, result.Message);
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            ToolTip.SetTip(this, $"组件命令失败：{exception.Message}");
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
        _surface.Fill = ParseBrush(
            hover ? _definition.Theme.SurfaceHover : _definition.Theme.Surface,
            hover ? "#2D354D" : "#22283A");
        _surface.Stroke = ParseBrush(
            hover ? _definition.Theme.BorderHover : _definition.Theme.Border,
            hover ? "#7C8CFF" : "#3A4563");
        _surface.StrokeThickness = hover
            ? Math.Max(2, _definition.Theme.BorderThickness)
            : _definition.Theme.BorderThickness;
        Opacity = _visualState switch
        {
            PolygonComponentVisualState.DragPreview => 0.68,
            PolygonComponentVisualState.LibraryPreview => 0.9,
            _ => 1
        };
    }

    private void ApplyTextRole(TextBlock block, ComponentTextRole role)
    {
        block.Foreground = role == ComponentTextRole.Caption
            ? ParseBrush(_definition.Theme.TextSecondary, "#A5AEC7")
            : ParseBrush(_definition.Theme.TextPrimary, "#F6F7FF");
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

    private static IBrush ParseBrush(string? value, string fallback)
    {
        try
        {
            return Brush.Parse(string.IsNullOrWhiteSpace(value) ? fallback : value);
        }
        catch
        {
            return Brush.Parse(fallback);
        }
    }

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
