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

public static class FilterEngine
{
    /// <summary>1 チャンクの行数。並列化の粒度と false sharing のバランスを見て決めた値。</summary>
    private const int ChunkSize = 2048;

    /// <summary>
    /// フィルタを適用して、一致した行番号（0 基点）の配列を返す。
    /// 戻り値が <c>null</c> のときは「全行が対象」を意味し、巨大な連番配列の確保を避ける。
    /// </summary>
    public static int[]? Apply(LogDocument document, CompiledFilter filter,
                               IProgress<double>? progress, CancellationToken ct)
    {
        if (filter.IsEmpty) return null;

        int lineCount = document.LineCount;
        if (lineCount == 0) return Array.Empty<int>();

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

        int matched = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i]) matched++;
        }

        var result = new int[matched];
        int k = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i]) result[k++] = i;
        }
        return result;
    }
}
