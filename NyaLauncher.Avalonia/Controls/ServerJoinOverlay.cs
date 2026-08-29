using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// 「选择版本进服」遮罩层内容卡片：顶部展示服务器状态摘要（MOTD、游戏版本、在线人数），
/// 中部为已安装版本列表，选中（或双击）后触发 <see cref="VersionLaunchRequested"/>，
/// 由宿主切换实例并携带进服参数启动。
/// </summary>
internal sealed class ServerJoinOverlay : UserControl, IModalHostAware
{
    public event EventHandler<string>? VersionLaunchRequested;

    public ModalOverlayHost? Host { get; set; }

    public ServerJoinOverlay(
        ServerJoinRequest request,
        IReadOnlyList<string> versionIds,
        string? selectedVersionId)
    {
        var versionList = new ListBox
        {
            MinHeight = 170,
            MaxHeight = 280,
            FontSize = 13,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            [!ListBox.BackgroundProperty] = new DynamicResourceExtension("ControlBgBrush"),
            [!ListBox.ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush"),
            ItemsSource = versionIds
        };
        if (versionIds.Count > 0)
        {
            var index = IndexOf(versionIds, selectedVersionId);
            if (index >= 0)
                versionList.SelectedIndex = index;
            else
                versionList.SelectedIndex = 0;
        }

        var launchButton = new Button
        {
            Content = "启动并进服",
            Padding = new Thickness(18, 8),
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            IsEnabled = false
        };
        launchButton[!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
        launchButton[!Button.ForegroundProperty] = new DynamicResourceExtension("WhiteBrush");

        var cancelButton = new Button
        {
            Content = "取消",
            Padding = new Thickness(18, 8),
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        cancelButton[!Button.BackgroundProperty] = new DynamicResourceExtension("ButtonBgBrush");
        cancelButton[!Button.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");
        cancelButton[!Button.BorderBrushProperty] = new DynamicResourceExtension("EmphasizedBorderBrush");

        var launchRequested = 0;
        void ConfirmSelection()
        {
            if (versionList.SelectedItem is not string versionId ||
                Interlocked.CompareExchange(ref launchRequested, 1, 0) != 0)
            {
                return;
            }
            VersionLaunchRequested?.Invoke(this, versionId);
        }

        launchButton.Click += (_, _) => ConfirmSelection();
        versionList.DoubleTapped += (_, _) => ConfirmSelection();
        cancelButton.Click += (_, _) => Host?.Close();
        versionList.SelectionChanged += (_, _) =>
            launchButton.IsEnabled = versionList.SelectedItem is not null;

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space &&
                versionList.SelectedItem is not null)
            {
                ConfirmSelection();
                e.Handled = true;
            }
        };

        Content = BuildCard(request, versionList, launchButton, cancelButton);
    }

    private Border BuildCard(
        ServerJoinRequest request,
        ListBox versionList,
        Button launchButton,
        Button cancelButton)
    {
        var players = request.MaxPlayers > 0
            ? $"{request.OnlinePlayers}/{request.MaxPlayers} 在线"
            : "在线人数未知";
        var motdText = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush")
        };
        MinecraftTextMarkup.Apply(motdText, request.Motd);
        var infoText = new TextBlock
        {
            Text = request.VersionName is null
                ? players
                : $"{request.VersionName} · {players}",
            FontSize = 11,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("HintTextBrush")
        };

        var statusCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
            Child = new StackPanel { Spacing = 4 },
            [!Border.BackgroundProperty] = new DynamicResourceExtension("CardBgBrush")
        };
        ((StackPanel)statusCard.Child!).Children.Add(motdText);
        ((StackPanel)statusCard.Child!).Children.Add(infoText);

        var versionHeader = new TextBlock
        {
            Text = "已安装的版本",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush")
        };

        var emptyHint = new TextBlock
        {
            Text = "未检测到游戏版本，请先在下载中心安装或添加实例。",
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 8),
            IsVisible = versionList.Items.Count == 0,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("HintTextBrush")
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(launchButton);

        var card = new Border
        {
            Width = 460,
            MaxWidth = 520,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(26, 22),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = new StackPanel { Spacing = 14 },
            [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBgBrush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("SubtleBorderBrush")
        };

        var stack = (StackPanel)card.Child!;
        stack.Children.Add(new TextBlock
        {
            Text = "选择进服版本",
            FontSize = 19,
            FontWeight = FontWeight.Bold,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{request.Address} · {request.Host}:{request.Port}",
            FontSize = 12,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("HintTextBrush")
        });
        stack.Children.Add(statusCard);
        stack.Children.Add(versionHeader);
        stack.Children.Add(versionList);
        stack.Children.Add(emptyHint);
        stack.Children.Add(buttonRow);
        return card;
    }

    private static int IndexOf(IReadOnlyList<string> list, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return -1;
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
