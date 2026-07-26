using System.Collections;
using System.ComponentModel;

namespace LogFilterView.Models;

/// <summary>表示 1 行分。画面に見えている行の分だけ生成される。</summary>
public sealed class LineRow : INotifyPropertyChanged
{
    private bool _isMarked;

    public LineRow(int viewIndex, int lineNumber, string text, bool isContext, bool isMarked)
    {
        ViewIndex = viewIndex;
        LineNumber = lineNumber;
        Text = text;
        IsContext = isContext;
        _isMarked = isMarked;
        LineNumberText = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>フィルタ後の並びでの位置（0 基点）。</summary>
    public int ViewIndex { get; }

    /// <summary>元ファイル上の行番号（1 基点）。</summary>
    public int LineNumber { get; }

    public string LineNumberText { get; }
    public string Text { get; }

    /// <summary>条件に一致したのではなく、前後の文脈として表示されている行。</summary>
    public bool IsContext { get; }

    /// <summary>マーカーの有無。実体化済みの行に対して後から更新される。</summary>
    public bool IsMarked
    {
        get => _isMarked;
        internal set
        {
            if (_isMarked == value) return;
            _isMarked = value;
            PropertyChanged?.Invoke(this, MarkedChangedArgs);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly PropertyChangedEventArgs MarkedChangedArgs = new(nameof(IsMarked));

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
    private readonly bool[]? _isContext;
    private readonly IReadOnlySet<int>? _markedLines;
    private readonly Dictionary<int, LineRow> _cache = new(256);

    public VirtualLineCollection(LogDocument document, int[]? map,
                                 bool[]? isContext = null, IReadOnlySet<int>? markedLines = null)
    {
        _document = document;
        _map = map;
        _isContext = isContext;
        _markedLines = markedLines;
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
            var row = new LineRow(index, lineIndex + 1, _document.GetText(lineIndex),
                                  _isContext is not null && _isContext[index],
                                  _markedLines is not null && _markedLines.Contains(lineIndex));
            _cache[index] = row;
            return row;
        }
    }

    /// <summary>
    /// マーカーの付け外しを、既に実体化されている行へ反映する。
    /// 実体化済みは画面に見えている前後 4096 行までなので、走査は一瞬で終わる。
    /// </summary>
    public void RefreshMarkers()
    {
        if (_markedLines is null) return;
        foreach (var (index, row) in _cache)
        {
            int lineIndex = _map is null ? index : _map[index];
            row.IsMarked = _markedLines.Contains(lineIndex);
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

    /// <summary>
    /// 元ファイルの行番号（1 基点）に対応する表示位置を返す。
    /// フィルタで隠れている行の場合は、表示されている直前・直後のうち近いほうを返す。
    /// </summary>
    public int FromLineNumberNearest(int lineNumber, out bool exact)
    {
        exact = false;
        if (Count == 0) return -1;

        int target = lineNumber - 1;
        if (_map is null)
        {
            exact = target >= 0 && target < Count;
            return Math.Clamp(target, 0, Count - 1);
        }

        int i = Array.BinarySearch(_map, target);
        if (i >= 0)
        {
            exact = true;
            return i;
        }

        int after = ~i;                 // target より大きい最初の要素
        int before = after - 1;
        if (after >= Count) return before;
        if (before < 0) return after;

        return (target - _map[before]) <= (_map[after] - target) ? before : after;
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
