using System.Collections.Generic;
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BloxCord.Client.Helpers;

public static class Markdown
{
    private const double MaxMarkdownFontSize = 48;
    private const double EmojiSizeScale = 1.15;
    private const double EmojiVerticalOffset = 1;

    private static readonly ConcurrentDictionary<string, ImageSource> EmojiImageCache = new(StringComparer.Ordinal);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(Markdown),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static string GetText(DependencyObject obj)
    {
        return (string)obj.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject obj, string value)
    {
        obj.SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            textBlock.Inlines.Clear();
            var text = e.NewValue as string;
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                ParseMarkdown(textBlock.Inlines, text, textBlock.FontSize);
            }
            catch
            {
                // Never crash the UI on emoji/markdown parsing.
                textBlock.Inlines.Clear();
                textBlock.Inlines.Add(new Run(text));
            }
        }
    }

    private static void ParseMarkdown(InlineCollection inlines, string text, double baseFontSize)
    {
        // Supported:
        // - Inline: **bold**, *italic*, ~~strike~~, `code`
        // - Blocks: # headings (font size clamped to 48px), ``` fenced code blocks
        // Not supported by design:
        // - Embeds (e.g., markdown images ![alt](url))

        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');

        bool inCodeBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i] ?? string.Empty;

            if (IsFence(line))
            {
                inCodeBlock = !inCodeBlock;
                // Don't render the fence itself.
                if (i < lines.Length - 1)
                    inlines.Add(new LineBreak());
                continue;
            }

            if (inCodeBlock)
            {
                AddInlineCodeRun(inlines, line);
                if (i < lines.Length - 1)
                    inlines.Add(new LineBreak());
                continue;
            }

            if (TryParseHeading(line, out var headingLevel, out var headingText))
            {
                AddHeading(inlines, headingLevel, headingText, baseFontSize);
                if (i < lines.Length - 1)
                    inlines.Add(new LineBreak());
                continue;
            }

            ParseInlineMarkdown(inlines, line, baseFontSize);
            if (i < lines.Length - 1)
                inlines.Add(new LineBreak());
        }
    }

    private static bool IsFence(string line)
        => line.TrimStart().StartsWith("```", System.StringComparison.Ordinal);

    private static bool TryParseHeading(string line, out int level, out string headingText)
    {
        level = 0;
        headingText = string.Empty;

        var trimmed = line.TrimStart();
        int i = 0;
        while (i < trimmed.Length && trimmed[i] == '#')
            i++;

        if (i == 0 || i > 6)
            return false;

        // Require a space after hashes to avoid matching things like "###not".
        if (i < trimmed.Length && trimmed[i] != ' ')
            return false;

        level = i;
        headingText = trimmed.Substring(i).Trim();
        return true;
    }

    private static void AddHeading(InlineCollection inlines, int level, string text, double baseFontSize)
    {
        // Keep sizes modest; hard clamp at 48px.
        double size = level switch
        {
            1 => 32,
            2 => 28,
            3 => 24,
            4 => 20,
            5 => 18,
            _ => 16
        };

        AddTextWithEmojiInlines(
            inlines,
            text,
            baseFontSize,
            run =>
            {
                run.FontWeight = FontWeights.Bold;
                run.FontSize = ClampFontSize(size);
            });
    }

    private static void AddInlineCodeRun(InlineCollection inlines, string text)
    {
        // Render a single line of fenced code.
        var run = new Run(text)
        {
            FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            FontSize = ClampFontSize(14)
        };
        inlines.Add(run);
    }

    private static void ParseInlineMarkdown(InlineCollection inlines, string text, double baseFontSize)
    {
        // Regex to match markdown tokens
        // Groups: 1=bold, 2=boldText, 3=italic, 4=italicText, 5=strike, 6=strikeText, 7=code, 8=codeText, 9=image, 10=link
        // Note: we intentionally do NOT render images/links as embeds or clickable controls; they remain plain text.
        var regex = new Regex(@"(!\[[^\]]*\]\([^\)]+\))|(\[[^\]]+\]\([^\)]+\))|(\*\*(.*?)\*\*)|(\*(.*?)\*)|(~~(.*?)~~)|(`(.*?)`)");

        int lastIndex = 0;

        foreach (Match match in regex.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                AddTextWithEmojiInlines(inlines, text.Substring(lastIndex, match.Index - lastIndex), baseFontSize);
            }

            // Images/links: render as plain text (no embeds).
            if (match.Groups[1].Success || match.Groups[2].Success)
            {
                AddTextWithEmojiInlines(inlines, match.Value, baseFontSize);
            }
            else if (match.Groups[3].Success) // Bold
            {
                AddTextWithEmojiInlines(
                    inlines,
                    match.Groups[4].Value,
                    baseFontSize,
                    run =>
                    {
                        run.FontWeight = FontWeights.Bold;
                        run.FontSize = ClampFontSize(run.FontSize);
                    });
            }
            else if (match.Groups[5].Success) // Italic
            {
                AddTextWithEmojiInlines(
                    inlines,
                    match.Groups[6].Value,
                    baseFontSize,
                    run =>
                    {
                        run.FontStyle = FontStyles.Italic;
                        run.FontSize = ClampFontSize(run.FontSize);
                    });
            }
            else if (match.Groups[7].Success) // Strike
            {
                AddTextWithEmojiInlines(
                    inlines,
                    match.Groups[8].Value,
                    baseFontSize,
                    run =>
                    {
                        run.TextDecorations = TextDecorations.Strikethrough;
                        run.FontSize = ClampFontSize(run.FontSize);
                    });
            }
            else if (match.Groups[9].Success) // Inline code
            {
                var run = new Run(match.Groups[10].Value)
                {
                    FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                    Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                    FontSize = ClampFontSize(14)
                };
                inlines.Add(run);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            AddTextWithEmojiInlines(inlines, text.Substring(lastIndex), baseFontSize);
        }
    }

    private static void AddTextWithEmojiInlines(
        InlineCollection inlines,
        string text,
        double baseFontSize,
        Action<Run>? styleRun = null)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var plain = new StringBuilder();

        foreach (var segment in EnumerateEmojiSegments(text))
        {
            if (!segment.IsEmoji)
            {
                plain.Append(segment.Text);
                continue;
            }

            if (plain.Length > 0)
            {
                var run = new Run(plain.ToString());
                styleRun?.Invoke(run);
                inlines.Add(run);
                plain.Clear();
            }

            var emojiInline = TryCreateEmojiInline(segment.Text, baseFontSize);
            if (emojiInline is not null)
            {
                inlines.Add(emojiInline);
            }
            else
            {
                // Fallback to text if we can't resolve an image.
                var run = new Run(segment.Text);
                styleRun?.Invoke(run);
                inlines.Add(run);
            }
        }

        if (plain.Length > 0)
        {
            var run = new Run(plain.ToString());
            styleRun?.Invoke(run);
            inlines.Add(run);
        }
    }

    private static Inline? TryCreateEmojiInline(string emojiText, double baseFontSize)
    {
        var codepoints = TryGetTwemojiCodepointString(emojiText);
        if (string.IsNullOrWhiteSpace(codepoints))
            return null;

        // Twemoji assets. We use a pinned version to avoid unexpected CDN changes.
        var uri = new Uri($"https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72/{codepoints}.png", UriKind.Absolute);

        // Important: don't use OnLoad (it can synchronously download and throw, crashing the UI).
        // Also, never cache failures.
        if (!EmojiImageCache.TryGetValue(codepoints, out var source))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnDemand;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation;
                bitmap.EndInit();
                bitmap.Freeze();

                source = bitmap;
                EmojiImageCache[codepoints] = source;
            }
            catch
            {
                return null;
            }
        }

        var size = Math.Max(10, baseFontSize * EmojiSizeScale);

        // Fallback: render the emoji text underneath, and draw the image over it if/when it loads.
        // This fixes cases where images fail to download (e.g., offline) and ensures flags still show.
        var fallbackText = new TextBlock
        {
            Text = emojiText,
            FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji"),
            FontSize = baseFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var img = new Image
        {
            Source = source,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            Margin = new Thickness(0, EmojiVerticalOffset, 0, 0)
        };

        var host = new Grid
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, EmojiVerticalOffset, 0, 0)
        };
        host.Children.Add(fallbackText);
        host.Children.Add(img);

        var container = new InlineUIContainer(host)
        {
            BaselineAlignment = BaselineAlignment.Center
        };
        return container;
    }

    private static string? TryGetTwemojiCodepointString(string emojiText)
    {
        // Convert the emoji grapheme to the hyphen-separated lowercase hex codepoint list.
        // We drop FE0E (text presentation) but keep FE0F/ZWJ/skin tones since Twemoji filenames use them.
        var parts = new List<string>();

        for (int i = 0; i < emojiText.Length;)
        {
            if (!Rune.TryGetRuneAt(emojiText, i, out var rune))
                break;

            i += rune.Utf16SequenceLength;

            if (rune.Value == 0xFE0E)
                continue;

            parts.Add(rune.Value.ToString("x"));
        }

        return parts.Count == 0 ? null : string.Join("-", parts);
    }

    private readonly record struct EmojiSegment(string Text, bool IsEmoji);

    private static IEnumerable<EmojiSegment> EnumerateEmojiSegments(string text)
    {
        // A pragmatic emoji-segmentation heuristic good enough for chat:
        // - ZWJ sequences
        // - Regional indicator flags
        // - Keycap sequences
        // - Most Unicode emoji blocks

        var plain = new StringBuilder();

        for (int i = 0; i < text.Length;)
        {
            if (!Rune.TryGetRuneAt(text, i, out var first))
                break;

            // Keycap: [0-9#*] (FE0F optional) 20E3
            if (IsKeycapBase(first) && TryReadKeycap(text, i, out var keycapLen))
            {
                if (plain.Length > 0)
                {
                    yield return new EmojiSegment(plain.ToString(), false);
                    plain.Clear();
                }
                yield return new EmojiSegment(text.Substring(i, keycapLen), true);
                i += keycapLen;
                continue;
            }

            // Flags: two regional indicators.
            if (IsRegionalIndicator(first) && TryReadFlag(text, i, out var flagLen))
            {
                if (plain.Length > 0)
                {
                    yield return new EmojiSegment(plain.ToString(), false);
                    plain.Clear();
                }
                yield return new EmojiSegment(text.Substring(i, flagLen), true);
                i += flagLen;
                continue;
            }

            // General emoji.
            if (IsLikelyEmojiStart(first))
            {
                var len = ReadEmojiSequence(text, i);
                if (len > 0)
                {
                    if (plain.Length > 0)
                    {
                        yield return new EmojiSegment(plain.ToString(), false);
                        plain.Clear();
                    }
                    yield return new EmojiSegment(text.Substring(i, len), true);
                    i += len;
                    continue;
                }
            }

            plain.Append(text, i, first.Utf16SequenceLength);
            i += first.Utf16SequenceLength;
        }

        if (plain.Length > 0)
        {
            yield return new EmojiSegment(plain.ToString(), false);
            plain.Clear();
        }
    }

    private static int ReadEmojiSequence(string text, int start)
    {
        int i = start;
        if (!Rune.TryGetRuneAt(text, i, out var current))
            return 0;
        i += current.Utf16SequenceLength;

        // Optional variation selector
        i += ReadOptionalVariationSelector(text, i);

        // Optional skin tone modifiers
        i += ReadOptionalSkinTone(text, i);

        // ZWJ sequences: (ZWJ + emoji + optional VS + optional skin tone) repeated
        while (TryPeekRune(text, i, out var next) && next.Value == 0x200D)
        {
            i += next.Utf16SequenceLength;
            if (!Rune.TryGetRuneAt(text, i, out var afterZwj))
                break;
            i += afterZwj.Utf16SequenceLength;
            i += ReadOptionalVariationSelector(text, i);
            i += ReadOptionalSkinTone(text, i);
        }

        return i - start;
    }

    private static int ReadOptionalVariationSelector(string text, int index)
    {
        if (TryPeekRune(text, index, out var r) && (r.Value == 0xFE0F || r.Value == 0xFE0E))
            return r.Utf16SequenceLength;
        return 0;
    }

    private static int ReadOptionalSkinTone(string text, int index)
    {
        int i = 0;
        while (TryPeekRune(text, index + i, out var r) && IsSkinToneModifier(r))
            i += r.Utf16SequenceLength;
        return i;
    }

    private static bool TryReadFlag(string text, int start, out int length)
    {
        length = 0;
        if (!Rune.TryGetRuneAt(text, start, out var a) || !IsRegionalIndicator(a))
            return false;

        int i = start + a.Utf16SequenceLength;
        if (!Rune.TryGetRuneAt(text, i, out var b) || !IsRegionalIndicator(b))
            return false;

        length = a.Utf16SequenceLength + b.Utf16SequenceLength;
        return true;
    }

    private static bool TryReadKeycap(string text, int start, out int length)
    {
        length = 0;
        if (!Rune.TryGetRuneAt(text, start, out var baseRune) || !IsKeycapBase(baseRune))
            return false;

        int i = start + baseRune.Utf16SequenceLength;

        // Optional FE0F
        if (TryPeekRune(text, i, out var maybeVs) && maybeVs.Value == 0xFE0F)
            i += maybeVs.Utf16SequenceLength;

        // U+20E3 COMBINING ENCLOSING KEYCAP
        if (!Rune.TryGetRuneAt(text, i, out var keycap) || keycap.Value != 0x20E3)
            return false;

        i += keycap.Utf16SequenceLength;
        length = i - start;
        return true;
    }

    private static bool TryPeekRune(string text, int index, out Rune rune)
    {
        if (index >= 0 && index < text.Length && Rune.TryGetRuneAt(text, index, out rune))
            return true;
        rune = default;
        return false;
    }

    private static bool IsRegionalIndicator(Rune rune)
        => rune.Value is >= 0x1F1E6 and <= 0x1F1FF;

    private static bool IsSkinToneModifier(Rune rune)
        => rune.Value is >= 0x1F3FB and <= 0x1F3FF;

    private static bool IsKeycapBase(Rune rune)
        => rune.Value is >= '0' and <= '9' || rune.Value == '#' || rune.Value == '*';

    private static bool IsLikelyEmojiStart(Rune rune)
    {
        // Common emoji blocks + a few symbols that become emoji with VS16.
        return rune.Value switch
        {
            0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x3030 or 0x303D => true,
            _ =>
                (rune.Value is >= 0x2600 and <= 0x27BF) ||
                (rune.Value is >= 0x1F000 and <= 0x1FAFF) ||
                (rune.Value is >= 0x1FC00 and <= 0x1FFFF)
        };
    }

    private static double ClampFontSize(double size)
    {
        if (size <= 0)
            return size;
        return size > MaxMarkdownFontSize ? MaxMarkdownFontSize : size;
    }
}
