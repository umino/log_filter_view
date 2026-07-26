using System.Collections;

namespace LogFilterView.Models;

/// <summary>表示 1 行分。画面に見えている行の分だけ生成される。</summary>
public sealed class LineRow
{
    public LineRow(int viewIndex, int lineNumber, string text)
    {
        ViewIndex = viewIndex;
        LineNumber = lineNumber;
        Text = text;
        LineNumberText = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>フィルタ後の並びでの位置（0 基点）。</summary>
    public int ViewIndex { get; }

    /// <summary>元ファイル上の行番号（1 基点）。</summary>
    public int LineNumber { get; }

    public string LineNumberText { get; }
    public string Text { get; }

    public override string ToString() => Text;
}

/// <summary>
/// フィルタ結果を WPF に見せるための仮想コレクション。
/// <see cref="IList"/> を実装しているので <c>ListCollectionView</c> は中身をコピーせず
/// インデクサ経由でアクセスし、<c>VirtualizingStackPanel</c> と組み合わせると
/// 画面に見えている行だけが実体化される（100 万行でも一瞬で切り替わる）。
/// </summary>
public sealed class VirtualLineCollection : IList, IReadOnlyList<LineRow>
{
    /// <summary>実体化済み行のキャッシュ上限。超えたら丸ごと破棄する。</summary>
    private const int CacheLimit = 4096;

    private readonly LogDocument _document;
    private readonly int[]? _map;
    private readonly Dictionary<int, LineRow> _cache = new(256);

    public VirtualLineCollection(LogDocument document, int[]? map)
    {
        _document = document;
        _map = map;
        Count = map?.Length ?? document.LineCount;
        MaxLineLength = ComputeMaxLineLength(document, map);
    }

    public int Count { get; }

    /// <summary>表示対象の行のうち最大の文字数。横スクロール幅の見積もりに使う。</summary>
    public int MaxLineLength { get; }

    /// <summary>フィルタが掛かっていない（全行表示）か。</summary>
    public bool IsUnfiltered => _map is null;

    public LineRow this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_cache.TryGetValue(index, out var cached)) return cached;

            if (_cache.Count >= CacheLimit) _cache.Clear();

            int lineIndex = _map is null ? index : _map[index];
            var row = new LineRow(index, lineIndex + 1, _document.GetText(lineIndex));
            _cache[index] = row;
            return row;
        }
    }

    /// <summary>表示位置 → 元ファイルの行番号（1 基点）。</summary>
    public int ToLineNumber(int viewIndex) => (_map is null ? viewIndex : _map[viewIndex]) + 1;

    /// <summary>元ファイルの行番号（1 基点）→ 表示位置。見つからなければ直後の行を返す。</summary>
    public int FromLineNumber(int lineNumber)
    {
        int target = lineNumber - 1;
        if (_map is null) return Math.Clamp(target, 0, Math.Max(0, Count - 1));
        if (Count == 0) return -1;

        int i = Array.BinarySearch(_map, target);
        if (i >= 0) return i;
        i = ~i;
        return Math.Min(i, Count - 1);
    }

    /// <summary>指定範囲を検索する。見つからなければ -1。</summary>
    public int Find(PatternMatcher matcher, int startViewIndex, bool forward)
    {
        if (Count == 0) return -1;

        if (forward)
        {
            for (int i = Math.Max(0, startViewIndex); i < Count; i++)
            {
                if (matcher.IsMatch(GetSpan(i))) return i;
            }
            for (int i = 0; i < Math.Min(startViewIndex, Count); i++)
            {
                if (matcher.IsMatch(GetSpan(i))) return i;
            }
        }
        else
        {
            for (int i = Math.Min(startViewIndex, Count - 1); i >= 0; i--)
            {
                if (matcher.IsMatch(GetSpan(i))) return i;
            }
            for (int i = Count - 1; i > Math.Max(-1, startViewIndex); i--)
            {
                if (matcher.IsMatch(GetSpan(i))) return i;
            }
        }
        return -1;
    }

    /// <summary>行テキストを文字列化せずに参照する。</summary>
    public ReadOnlySpan<char> GetSpan(int viewIndex) => _document.GetSpan(_map is null ? viewIndex : _map[viewIndex]);

    private static int ComputeMaxLineLength(LogDocument document, int[]? map)
    {
        if (map is null) return document.MaxLineLength;

        int max = 0;
        for (int i = 0; i < map.Length; i++)
        {
            int len = document.GetLength(map[i]);
            if (len > max) max = len;
        }
        return max;
    }

    public IEnumerator<LineRow> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #region IList（読み取り専用として実装）

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    bool IList.IsFixedSize => true;
    bool IList.IsReadOnly => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    int IList.IndexOf(object? value) =>
        value is LineRow row && row.ViewIndex < Count && ReferenceEquals(row, this[row.ViewIndex]) ? row.ViewIndex : -1;

    bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;

    void ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
    }

    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();

    #endregion
}
