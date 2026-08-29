using System;
using System.Collections.Concurrent;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>
/// Minecraft 样式码（§0-§f 颜色，§l/o/n/m 格式，§r 重置）渲染器：
/// 将包含 § 码的文本转换为彩色 Inline Run 序列。无 § 码时退化为普通文本。
/// </summary>
internal static class MinecraftTextMarkup
{
    private static readonly ConcurrentDictionary<Color, ImmutableSolidColorBrush> BrushCache = new();

    /// <summary>把文本写入 TextBlock；含 § 码时渲染为彩色内联序列。</summary>
    public static void Apply(TextBlock block, string? text)
    {
        text ??= string.Empty;
        if (!text.Contains('§'))
        {
            block.Inlines = null;
            block.Text = text;
            return;
        }

        block.Inlines = BuildInlines(text);
    }

    public static InlineCollection BuildInlines(string text)
    {
        var inlines = new InlineCollection();
        var builder = new StringBuilder();
        Color? color = null;
        var bold = false;
        var italic = false;
        var underline = false;
        var strikethrough = false;

        void Flush()
        {
            if (builder.Length == 0)
                return;

            var run = new Run(builder.ToString());
            if (color is not null)
                run.Foreground = GetBrush(color.Value);
            if (bold)
                run.FontWeight = FontWeight.Bold;
            if (italic)
                run.FontStyle = FontStyle.Italic;
            if (underline || strikethrough)
            {
                var decorations = new TextDecorationCollection();
                if (underline)
                {
                    foreach (var decoration in TextDecorations.Underline)
                        decorations.Add(decoration);
                }
                if (strikethrough)
                {
                    foreach (var decoration in TextDecorations.Strikethrough)
                        decorations.Add(decoration);
                }
                run.TextDecorations = decorations;
            }

            inlines.Add(run);
            builder.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (current != '§' || i + 1 >= text.Length)
            {
                builder.Append(current);
                continue;
            }

            Flush();
            var code = char.ToLowerInvariant(text[++i]);
            if (TryGetColor(code, out var parsed))
            {
                color = parsed;
            }
            else
            {
                switch (code)
                {
                    case 'l': bold = true; break;
                    case 'o': italic = true; break;
                    case 'n': underline = true; break;
                    case 'm': strikethrough = true; break;
                    case 'r':
                        color = null;
                        bold = italic = underline = strikethrough = false;
                        break;
                    case 'k': // 乱码效果无法在静态文本中呈现，忽略
                        break;
                }
            }
        }

        Flush();
        if (inlines.Count == 0)
            inlines.Add(new Run(string.Empty));
        return inlines;
    }

    private static ImmutableSolidColorBrush GetBrush(Color color) =>
        BrushCache.GetOrAdd(color, static value => new ImmutableSolidColorBrush(value));

    private static bool TryGetColor(char code, out Color color)
    {
        switch (code)
        {
            case '0': color = Color.Parse("#000000"); return true;
            case '1': color = Color.Parse("#0000AA"); return true;
            case '2': color = Color.Parse("#00AA00"); return true;
            case '3': color = Color.Parse("#00AAAA"); return true;
            case '4': color = Color.Parse("#AA0000"); return true;
            case '5': color = Color.Parse("#AA00AA"); return true;
            case '6': color = Color.Parse("#FFAA00"); return true;
            case '7': color = Color.Parse("#AAAAAA"); return true;
            case '8': color = Color.Parse("#555555"); return true;
            case '9': color = Color.Parse("#5555FF"); return true;
            case 'a': color = Color.Parse("#55FF55"); return true;
            case 'b': color = Color.Parse("#55FFFF"); return true;
            case 'c': color = Color.Parse("#FF5555"); return true;
            case 'd': color = Color.Parse("#FF55FF"); return true;
            case 'e': color = Color.Parse("#FFFF55"); return true;
            case 'f': color = Color.Parse("#FFFFFF"); return true;
            default: color = default; return false;
        }
    }

    /// <summary>JSON 聊天组件的 color 名称 → § 码（现代服务器状态响应）。</summary>
    public static char? ColorNameToCode(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return name.ToLowerInvariant() switch
        {
            "black" => '0',
            "dark_blue" => '1',
            "dark_green" => '2',
            "dark_aqua" => '3',
            "dark_red" => '4',
            "dark_purple" => '5',
            "gold" => '6',
            "gray" => '7',
            "dark_gray" => '8',
            "blue" => '9',
            "green" => 'a',
            "aqua" => 'b',
            "red" => 'c',
            "light_purple" => 'd',
            "yellow" => 'e',
            "white" => 'f',
            _ => null
        };
    }
}
