using System.Windows.Media;
using Sudare.Models;

namespace Sudare.Views;

public sealed class HighlightRule
{
    public HighlightRule(PatternMatcher matcher, Brush background)
    {
        Matcher = matcher;
        Background = background;
    }

    public PatternMatcher Matcher { get; }
    public Brush Background { get; }
}

/// <summary>行テキスト内の強調表示範囲を計算する。</summary>
public sealed class HighlightRuleSet
{
    /// <summary>これより長い行はハイライトしない（描画コストが釣り合わないため）。</summary>
    public const int MaxHighlightLength = 4000;

    /// <summary>1 行あたりの最大ハイライト数。</summary>
    private const int MaxRangesPerLine = 256;

    private static readonly Color[] PaletteColors =
    {
        Color.FromRgb(0xFF, 0xF1, 0x76), // 黄
        Color.FromRgb(0xA5, 0xD6, 0xA7), // 緑
        Color.FromRgb(0x90, 0xCA, 0xF9), // 青
        Color.FromRgb(0xFF, 0xCC, 0x80), // 橙
        Color.FromRgb(0xCE, 0x93, 0xD8), // 紫
        Color.FromRgb(0x80, 0xDE, 0xEA), // 水
        Color.FromRgb(0xF4, 0x8F, 0xB1), // 桃
        Color.FromRgb(0xE6, 0xEE, 0x9C), // 黄緑
    };

    public static readonly Brush SearchBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x65)));

    public static IReadOnlyList<Brush> Palette { get; } = CreatePalette();

    public static readonly HighlightRuleSet Empty = new(Array.Empty<HighlightRule>());

    private readonly HighlightRule[] _rules;

    public HighlightRuleSet(IReadOnlyList<HighlightRule> rules) => _rules = rules.ToArray();

    public bool IsEmpty => _rules.Length == 0;

    /// <summary>
    /// 強調範囲を左から順に、重なりのない形で返す。先に登録されたルールを優先する。
    /// </summary>
    public List<(int Start, int Length, Brush Brush)> Compute(string text)
    {
        var result = new List<(int, int, Brush)>();
        if (_rules.Length == 0 || text.Length == 0) return result;

        var span = text.AsSpan(0, Math.Min(text.Length, MaxHighlightLength));
        var raw = new List<(int Start, int Length, int Priority, Brush Brush)>();
        var buffer = new List<(int Start, int Length)>();

        for (int i = 0; i < _rules.Length; i++)
        {
            buffer.Clear();
            _rules[i].Matcher.CollectRanges(span, buffer, MaxRangesPerLine);
            foreach (var (start, length) in buffer)
            {
                raw.Add((start, length, i, _rules[i].Background));
            }
            if (raw.Count > MaxRangesPerLine * 2) break;
        }

        if (raw.Count == 0) return result;

        raw.Sort(static (a, b) =>
        {
            int c = a.Start.CompareTo(b.Start);
            if (c != 0) return c;
            c = a.Priority.CompareTo(b.Priority);
            return c != 0 ? c : b.Length.CompareTo(a.Length);
        });

        int cursor = 0;
        foreach (var (start, length, _, brush) in raw)
        {
            if (start < cursor) continue;              // 既に塗った範囲と重なる
            if (start + length > span.Length) continue;
            result.Add((start, length, brush));
            cursor = start + length;
        }

        return result;
    }

    private static Brush[] CreatePalette()
    {
        var brushes = new Brush[PaletteColors.Length];
        for (int i = 0; i < PaletteColors.Length; i++)
        {
            brushes[i] = Freeze(new SolidColorBrush(PaletteColors[i]));
        }
        return brushes;
    }

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
