using System.Text;
using System.Text.RegularExpressions;

namespace Sudare.Models;

/// <summary>
/// 1 パターン分のマッチャ。フィルタ処理は複数スレッドから同時に呼ばれるため、
/// 実装はスレッドセーフ（不変）でなければならない。
/// </summary>
public abstract class PatternMatcher
{
    protected PatternMatcher(string pattern) => Pattern = pattern;

    public string Pattern { get; }

    public abstract bool IsMatch(ReadOnlySpan<char> line);

    /// <summary>ハイライト用に一致範囲を収集する。<paramref name="limit"/> 件で打ち切る。</summary>
    public abstract void CollectRanges(ReadOnlySpan<char> line, List<(int Start, int Length)> sink, int limit);

    /// <summary>パターンをコンパイルする。正規表現が不正な場合は <see cref="FilterPatternException"/>。</summary>
    public static PatternMatcher Create(string pattern, MatchMode mode, bool caseSensitive) => mode switch
    {
        MatchMode.Plain => new PlainMatcher(pattern, caseSensitive),
        MatchMode.Wildcard => new RegexMatcher(pattern, WildcardToRegex(pattern), caseSensitive),
        MatchMode.Regex => new RegexMatcher(pattern, pattern, caseSensitive),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>
    /// ワイルドカードを正規表現へ変換する。行頭・行末のアンカーは付けない
    /// （ログ用途では「ERROR」が「ERROR を含む行」を意味するほうが自然なため）。
    /// </summary>
    internal static string WildcardToRegex(string pattern)
    {
        var sb = new StringBuilder(pattern.Length * 2);
        foreach (char c in pattern)
        {
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        return sb.ToString();
    }
}

/// <summary>パターンのコンパイルに失敗したことを表す。</summary>
public sealed class FilterPatternException : Exception
{
    public FilterPatternException(string pattern, string message, Exception? inner = null)
        : base(message, inner) => Pattern = pattern;

    public string Pattern { get; }
}

internal sealed class PlainMatcher : PatternMatcher
{
    private readonly StringComparison _comparison;

    public PlainMatcher(string pattern, bool caseSensitive) : base(pattern)
    {
        if (pattern.Length == 0) throw new FilterPatternException(pattern, "空のパターンは使用できません。");
        _comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    }

    public override bool IsMatch(ReadOnlySpan<char> line) => line.IndexOf(Pattern, _comparison) >= 0;

    public override void CollectRanges(ReadOnlySpan<char> line, List<(int Start, int Length)> sink, int limit)
    {
        int offset = 0;
        while (offset < line.Length && sink.Count < limit)
        {
            int i = line[offset..].IndexOf(Pattern, _comparison);
            if (i < 0) break;
            sink.Add((offset + i, Pattern.Length));
            offset += i + Pattern.Length;
        }
    }
}

internal sealed class RegexMatcher : PatternMatcher
{
    private readonly Regex _regex;

    public RegexMatcher(string pattern, string regexSource, bool caseSensitive) : base(pattern)
    {
        if (pattern.Length == 0) throw new FilterPatternException(pattern, "空のパターンは使用できません。");

        var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (!caseSensitive) options |= RegexOptions.IgnoreCase;
        try
        {
            // 病的なパターンで固まらないよう 1 行あたりの上限を設ける
            _regex = new Regex(regexSource, options, TimeSpan.FromMilliseconds(500));
        }
        catch (ArgumentException ex)
        {
            throw new FilterPatternException(pattern, $"パターンが不正です: {pattern}\n{ex.Message}", ex);
        }
    }

    public override bool IsMatch(ReadOnlySpan<char> line)
    {
        try
        {
            return _regex.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public override void CollectRanges(ReadOnlySpan<char> line, List<(int Start, int Length)> sink, int limit)
    {
        try
        {
            foreach (var m in _regex.EnumerateMatches(line))
            {
                if (sink.Count >= limit) break;
                if (m.Length <= 0) continue;   // 長さ 0 の一致は無限ループの元なので捨てる
                sink.Add((m.Index, m.Length));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // ハイライトは best effort
        }
    }
}
