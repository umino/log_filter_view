namespace LogFilterView.Models;

/// <summary>UI 上のフィルタ設定（文字列のまま）。</summary>
public sealed record FilterRequest(
    string IncludeText,
    string ExcludeText,
    MatchMode Mode,
    bool CaseSensitive,
    LogicMode IncludeLogic,
    LogicMode ExcludeLogic);

/// <summary>コンパイル済みフィルタ。生成後は不変でスレッドセーフ。</summary>
public sealed class CompiledFilter
{
    public static CompiledFilter Empty { get; } =
        new(Array.Empty<PatternMatcher>(), Array.Empty<PatternMatcher>(), LogicMode.Or, LogicMode.Or);

    private CompiledFilter(PatternMatcher[] include, PatternMatcher[] exclude,
                           LogicMode includeLogic, LogicMode excludeLogic)
    {
        Include = include;
        Exclude = exclude;
        IncludeLogic = includeLogic;
        ExcludeLogic = excludeLogic;
    }

    public PatternMatcher[] Include { get; }
    public PatternMatcher[] Exclude { get; }
    public LogicMode IncludeLogic { get; }
    public LogicMode ExcludeLogic { get; }

    public bool IsEmpty => Include.Length == 0 && Exclude.Length == 0;

    public bool IsMatch(ReadOnlySpan<char> line)
    {
        var include = Include;
        if (include.Length > 0)
        {
            if (IncludeLogic == LogicMode.And)
            {
                for (int i = 0; i < include.Length; i++)
                {
                    if (!include[i].IsMatch(line)) return false;
                }
            }
            else
            {
                bool any = false;
                for (int i = 0; i < include.Length; i++)
                {
                    if (include[i].IsMatch(line)) { any = true; break; }
                }
                if (!any) return false;
            }
        }

        var exclude = Exclude;
        if (exclude.Length > 0)
        {
            if (ExcludeLogic == LogicMode.And)
            {
                bool all = true;
                for (int i = 0; i < exclude.Length; i++)
                {
                    if (!exclude[i].IsMatch(line)) { all = false; break; }
                }
                if (all) return false;
            }
            else
            {
                for (int i = 0; i < exclude.Length; i++)
                {
                    if (exclude[i].IsMatch(line)) return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 入力欄のテキストからフィルタを組み立てる。1 行 1 パターン、
    /// 空行と <c>#</c> で始まる行（コメント）は無視する。
    /// </summary>
    public static CompiledFilter Compile(FilterRequest request)
    {
        var include = CompilePatterns(request.IncludeText, request.Mode, request.CaseSensitive);
        var exclude = CompilePatterns(request.ExcludeText, request.Mode, request.CaseSensitive);
        if (include.Length == 0 && exclude.Length == 0) return Empty;
        return new CompiledFilter(include, exclude, request.IncludeLogic, request.ExcludeLogic);
    }

    public static PatternMatcher[] CompilePatterns(string text, MatchMode mode, bool caseSensitive)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<PatternMatcher>();

        var list = new List<PatternMatcher>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line[0] == '#') continue;
            list.Add(PatternMatcher.Create(line, mode, caseSensitive));
        }
        return list.ToArray();
    }
}

/// <summary>行の範囲（両端を含む、0 基点）。</summary>
public readonly record struct LineRange(int Start, int End);

/// <summary>
/// 表示すべき行の一覧と、その各行が「ヒット行」か「前後の文脈行」かの区別。
/// <see cref="Map"/> が <c>null</c> のときは全行表示を意味する。
/// </summary>
public readonly record struct ViewComposition(int[]? Map, bool[]? IsContext);

public static class FilterEngine
{
    /// <summary>1 チャンクの行数。並列化の粒度と false sharing のバランスを見て決めた値。</summary>
    private const int ChunkSize = 2048;

    /// <summary>
    /// フィルタを適用して、行ごとの一致有無を返す。
    /// 戻り値が <c>null</c> のときは「条件なし（全行が対象）」を意味する。
    /// </summary>
    public static bool[]? Match(LogDocument document, CompiledFilter filter,
                                IProgress<double>? progress, CancellationToken ct)
    {
        if (filter.IsEmpty) return null;

        int lineCount = document.LineCount;
        if (lineCount == 0) return Array.Empty<bool>();

        var hits = new bool[lineCount];
        int chunkCount = (lineCount + ChunkSize - 1) / ChunkSize;

        int completed = 0;
        int reportedPercent = -1;

        var options = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Environment.ProcessorCount,
        };

        Parallel.For(0, chunkCount, options, chunkIndex =>
        {
            int start = chunkIndex * ChunkSize;
            int end = Math.Min(start + ChunkSize, lineCount);
            for (int i = start; i < end; i++)
            {
                hits[i] = filter.IsMatch(document.GetSpan(i));
            }

            if (progress is null) return;

            int done = Interlocked.Increment(ref completed);
            int percent = (int)(done * 100L / chunkCount);
            int previous = Volatile.Read(ref reportedPercent);
            if (percent > previous && Interlocked.CompareExchange(ref reportedPercent, percent, previous) == previous)
            {
                progress.Report(percent);
            }
        });

        ct.ThrowIfCancellationRequested();
        return hits;
    }

    /// <summary>
    /// 一致結果から実際に表示する行を組み立てる。
    /// ヒット行の前後 <paramref name="contextLines"/> 行と、個別に展開を指示された範囲を足し込む。
    /// 前後行・展開行は除外語の判定を通さない（grep -C と同じく、文脈は無条件に見せる）。
    /// </summary>
    /// <remarks>
    /// 照合をやり直さずに済むので、前後行数の変更や 1 行だけの展開はミリ秒で反映できる。
    /// </remarks>
    public static ViewComposition Compose(LogDocument document, bool[]? hits,
                                          int contextLines, IReadOnlyList<LineRange> expansions)
    {
        if (hits is null) return new ViewComposition(null, null);

        int lineCount = document.LineCount;
        if (lineCount == 0) return new ViewComposition(Array.Empty<int>(), null);

        if (contextLines <= 0 && expansions.Count == 0)
        {
            return new ViewComposition(ToIndexArray(hits), null);
        }

        var display = new bool[lineCount];

        // 直前に塗り終えた位置を覚えておき、重なる範囲を二度塗りしない（全体で O(行数)）
        int filled = -1;
        for (int i = 0; i < lineCount; i++)
        {
            if (!hits[i]) continue;
            int start = Math.Max(i - contextLines, filled + 1);
            int end = Math.Min(i + contextLines, lineCount - 1);
            for (int j = start; j <= end; j++) display[j] = true;
            if (end > filled) filled = end;
        }

        foreach (var range in expansions)
        {
            int start = Math.Max(0, range.Start);
            int end = Math.Min(lineCount - 1, range.End);
            for (int j = start; j <= end; j++) display[j] = true;
        }

        var map = ToIndexArray(display);
        var isContext = new bool[map.Length];
        for (int k = 0; k < map.Length; k++) isContext[k] = !hits[map[k]];

        return new ViewComposition(map, isContext);
    }

    private static int[] ToIndexArray(bool[] flags)
    {
        int count = 0;
        for (int i = 0; i < flags.Length; i++)
        {
            if (flags[i]) count++;
        }

        var result = new int[count];
        int k = 0;
        for (int i = 0; i < flags.Length; i++)
        {
            if (flags[i]) result[k++] = i;
        }
        return result;
    }
}
