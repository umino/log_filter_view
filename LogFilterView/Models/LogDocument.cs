using System.IO;
using System.Text;

namespace LogFilterView.Models;

/// <summary>ログの入力元。</summary>
public enum LogSource
{
    /// <summary>未読み込み。</summary>
    None,

    /// <summary>ファイル。</summary>
    File,

    /// <summary>クリップボード。</summary>
    Clipboard,
}

/// <summary>
/// 読み込み済みログ 1 件分。生成後は不変なので複数スレッドから安全に読める。
/// </summary>
/// <remarks>
/// 100MB 級のファイルを扱うため、行ごとの <see cref="string"/> は作らない。
/// ファイル全体を 1 本の文字列として保持し、各行は開始位置と長さのインデックスで表す。
/// 行のテキストが必要になるのは画面に見えている数十行だけ。
/// </remarks>
public sealed class LogDocument
{
    private readonly string _text;
    private readonly int[] _starts;
    private readonly int[] _lengths;

    private LogDocument(LogSource source, string filePath, string displayName, string text,
                        int[] starts, int[] lengths, int maxLineLength,
                        Encoding encoding, string encodingName, long sizeBytes)
    {
        Source = source;
        FilePath = filePath;
        DisplayName = displayName;
        _text = text;
        _starts = starts;
        _lengths = lengths;
        MaxLineLength = maxLineLength;
        Encoding = encoding;
        EncodingName = encodingName;
        SizeBytes = sizeBytes;
        LoadedAt = DateTime.Now;
    }

    public LogSource Source { get; }

    /// <summary>ファイル由来のときのみ意味を持つ。それ以外は空文字。</summary>
    public string FilePath { get; }

    /// <summary>タイトルバーやステータスバーに出す名前。</summary>
    public string DisplayName { get; }

    public Encoding Encoding { get; }
    public string EncodingName { get; }
    public long SizeBytes { get; }
    public DateTime LoadedAt { get; }
    public int LineCount => _starts.Length;
    public int MaxLineLength { get; }
    public int CharCount => _text.Length;

    public ReadOnlySpan<char> GetSpan(int index) => _text.AsSpan(_starts[index], _lengths[index]);

    public string GetText(int index) => _lengths[index] == 0 ? string.Empty : _text.Substring(_starts[index], _lengths[index]);

    public int GetLength(int index) => _lengths[index];

    /// <summary>空のドキュメント（起動直後の状態）。</summary>
    public static LogDocument Empty { get; } =
        new(LogSource.None, string.Empty, string.Empty, string.Empty,
            Array.Empty<int>(), Array.Empty<int>(), 0, new UTF8Encoding(false), "UTF-8", 0);

    public bool IsEmptyDocument => Source == LogSource.None;

    /// <summary>
    /// 既にテキストになっているもの（クリップボードなど）からドキュメントを作る。
    /// 行インデックスの作成だけ行うので、呼び出し側でバックグラウンドに逃がすとよい。
    /// </summary>
    public static LogDocument FromText(string text, string displayName)
    {
        var (starts, lengths, maxLen) = BuildLineIndex(text);
        return new LogDocument(LogSource.Clipboard, string.Empty, displayName, text,
                               starts, lengths, maxLen,
                               new UTF8Encoding(false), "UTF-8",
                               Encoding.UTF8.GetByteCount(text));
    }

    public static async Task<LogDocument> LoadAsync(string path, EncodingChoice choice,
                                                   IProgress<LoadProgress>? progress,
                                                   CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("ファイルが見つかりません。", path);
        if (info.Length > int.MaxValue - 64)
            throw new NotSupportedException("2GB 以上のファイルには対応していません。");

        int length = (int)info.Length;
        byte[] bytes = new byte[length];

        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                                 bufferSize: 1 << 20, useAsync: true))
        {
            const int ChunkSize = 4 << 20;
            int read = 0;
            while (read < length)
            {
                int n = await stream.ReadAsync(bytes.AsMemory(read, Math.Min(ChunkSize, length - read)), ct)
                                    .ConfigureAwait(false);
                if (n <= 0) break;
                read += n;
                progress?.Report(new LoadProgress("読み込み中", read * 60.0 / Math.Max(1, length)));
            }
            if (read != length) length = read;   // 読み込み中にファイルが縮んだ場合
        }

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new LoadProgress("デコード中", 65));
            var (text, encoding, encodingName) = TextEncodings.Decode(bytes, length, choice);

            // 200MB 級の文字列を確保した直後なのでバイト配列は早めに手放す
            bytes = Array.Empty<byte>();

            ct.ThrowIfCancellationRequested();
            progress?.Report(new LoadProgress("行を解析中", 85));
            var (starts, lengths, maxLen) = BuildLineIndex(text);

            progress?.Report(new LoadProgress("完了", 100));
            return new LogDocument(LogSource.File, path, Path.GetFileName(path), text,
                                   starts, lengths, maxLen, encoding, encodingName, info.Length);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 改行位置を走査して行インデックスを作る。CRLF / LF / 末尾改行なしをすべて扱う。
    /// </summary>
    private static (int[] Starts, int[] Lengths, int MaxLength) BuildLineIndex(string text)
    {
        var span = text.AsSpan();
        if (span.Length == 0) return (Array.Empty<int>(), Array.Empty<int>(), 0);

        // 1 行あたり平均 60 文字と見積もって初期容量を確保しておく（再確保のコピーを減らす）
        int estimate = Math.Max(16, span.Length / 60);
        var starts = new List<int>(estimate);
        var lengths = new List<int>(estimate);

        int pos = 0;
        int max = 0;
        while (pos < span.Length)
        {
            int rel = span[pos..].IndexOf('\n');
            int end = rel < 0 ? span.Length : pos + rel;
            int len = end - pos;
            if (len > 0 && span[end - 1] == '\r') len--;

            starts.Add(pos);
            lengths.Add(len);
            if (len > max) max = len;

            if (rel < 0) break;
            pos = end + 1;
        }

        return (starts.ToArray(), lengths.ToArray(), max);
    }
}

public readonly record struct LoadProgress(string Phase, double Percent);
