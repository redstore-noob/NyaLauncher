using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace NyaLauncher.Avalonia.Pages;

internal sealed class AboutPage : UserControl
{
    public AboutPage()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        content.Children.Add(new TextBlock
        {
            Text = "NyaLauncher",
            FontSize = 28,
            FontWeight = FontWeight.Bold
        });
        content.Children.Add(new TextBlock
        {
            Text = "ppre-1 · 基于 .NET 10 与 Avalonia 的可扩展 Minecraft 启动器",
            FontSize = 13,
            Foreground = Brush.Parse("#A5AEC7"),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "第三方插件与启动器在同一进程内运行。安装前请核对来源、版本、SHA-256 与所请求能力。",
            FontSize = 12,
            Foreground = Brush.Parse("#C1C8DB"),
            TextWrapping = TextWrapping.Wrap
        });
        Content = content;
    }
}
