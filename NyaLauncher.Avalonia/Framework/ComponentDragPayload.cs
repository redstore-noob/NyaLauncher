using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Threading;
using System.Threading.Tasks;

namespace NyaLauncher.Avalonia.Framework;

public sealed record ComponentDragPayload(string ComponentId, string? SourceAreaId)
{
    private const string Prefix = "nyalauncher-component-v1|";

    public bool IsFromLibrary => string.IsNullOrWhiteSpace(SourceAreaId);

    public string Serialize()
    {
        return $"{Prefix}{Uri.EscapeDataString(ComponentId)}|" +
               Uri.EscapeDataString(SourceAreaId ?? string.Empty);
    }

    public DataTransfer CreateDataTransfer()
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(Serialize()));
        return transfer;
    }

    public static bool TryParse(IDataTransfer transfer, out ComponentDragPayload? payload)
    {
        payload = null;
        var text = transfer.TryGetText();
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = text[Prefix.Length..].Split('|', 2);
        if (parts.Length != 2)
            return false;

        var componentId = Uri.UnescapeDataString(parts[0]);
        var sourceAreaId = Uri.UnescapeDataString(parts[1]);
        if (string.IsNullOrWhiteSpace(componentId))
            return false;

        payload = new ComponentDragPayload(
            componentId,
            string.IsNullOrWhiteSpace(sourceAreaId) ? null : sourceAreaId);
        return true;
    }
}

public static class ComponentDragSource
{
    private const double DragThreshold = 6;
    private static readonly TimeSpan LongPressDuration = TimeSpan.FromMilliseconds(420);

    public static void Attach(Control control, string componentId, string? sourceAreaId)
    {
        PendingDrag? pending = null;

        void CancelPending()
        {
            var candidate = pending;
            pending = null;
            if (candidate is null)
                return;
            candidate.Cancellation.Cancel();
            candidate.Cancellation.Dispose();
        }

        async Task StartDragAsync(PendingDrag candidate)
        {
            if (!ReferenceEquals(pending, candidate))
                return;

            pending = null;
            candidate.Cancellation.Dispose();
            candidate.PressedEvent.Pointer.Capture(control);
            await DragDrop.DoDragDropAsync(
                candidate.PressedEvent,
                candidate.Payload.CreateDataTransfer(),
                candidate.Payload.IsFromLibrary
                    ? DragDropEffects.Copy
                    : DragDropEffects.Move);
        }

        async Task StartAfterLongPressAsync(PendingDrag candidate)
        {
            try
            {
                await Task.Delay(LongPressDuration, candidate.Cancellation.Token);
                await StartDragAsync(candidate);
            }
            catch (OperationCanceledException)
            {
                // Releasing or moving before the long-press threshold keeps the
                // original child click interaction intact.
            }
        }

        control.AddHandler(
            InputElement.PointerPressedEvent,
            (_, args) =>
            {
                if (!args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                    return;

                CancelPending();
                var candidate = new PendingDrag(
                    args,
                    args.GetPosition(control),
                    new ComponentDragPayload(componentId, sourceAreaId),
                    new CancellationTokenSource());
                pending = candidate;
                if (!candidate.Payload.IsFromLibrary)
                    _ = StartAfterLongPressAsync(candidate);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        control.AddHandler(
            InputElement.PointerMovedEvent,
            async (_, args) =>
            {
                var candidate = pending;
                if (candidate is null ||
                    !args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                var position = args.GetPosition(control);
                var delta = position - candidate.Origin;
                if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                    return;

                if (!candidate.Payload.IsFromLibrary)
                {
                    CancelPending();
                    return;
                }

                args.Handled = true;
                await StartDragAsync(candidate);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        control.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, _) => CancelPending(),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        control.PointerCaptureLost += (_, _) => CancelPending();
    }

    private sealed record PendingDrag(
        PointerPressedEventArgs PressedEvent,
        Point Origin,
        ComponentDragPayload Payload,
        CancellationTokenSource Cancellation);
}
