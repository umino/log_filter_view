using System.Buffers;
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
/// 保持のしかたは 2 通りある。
/// <list type="bullet">
///   <item>
///     <b>バイト保持</b>（UTF-8 / Shift_JIS など、改行をバイト列のまま走査できる文字コード）。
///     読み込んだ <c>byte[]</c> をそのまま持ち、行はバイトオフセットで表す。
///     文字列化するのは画面に見えている行と、照合でデコードが要るときだけ。
///     UTF-16 に展開しないので常駐はファイルサイズとほぼ同じで済む。
///   </item>
///   <item>
///     <b>文字列保持</b>（UTF-16 / ISO-2022-JP、およびクリップボード）。
///     従来どおり全体を 1 本の <c>string</c> にして、行は文字オフセットで表す。
///   </item>
/// </list>
/// どちらの場合も、行の内容を取り出す経路は <see cref="CreateAccessor"/> と
/// <see cref="GetText(int)"/> に統一されている。
/// </remarks>
public sealed class LogDocument
{
    private readonly byte[]? _bytes;
    private readonly string? _text;

    /// <summary>行の開始位置。バイト保持ならバイト単位、文字列保持なら文字単位。</summary>
    private readonly int[] _starts;

    /// <summary>行の長さ（改行文字を含まない）。単位は <see cref="_starts"/> と同じ。</summary>
    private readonly int[] _lengths;

    private LogDocument(LogSource source, string filePath, string displayName,
                        byte[]? bytes, string? text,
                        int[] starts, int[] lengths, int maxLineLength,
                        Encoding encoding, string encodingName, long sizeBytes)
    {
        Source = source;
        FilePath = filePath;
        DisplayName = displayName;
        _bytes = bytes;
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

    /// <summary>バイト列のまま保持しているか（デバッグ・表示用）。</summary>
    public bool IsByteBacked => _bytes is not null;

    /// <summary>
    /// 最も長い行の長さ。バイト保持では「バイト数」なので、多バイト文字を含む行では
    /// 実際の文字数より大きくなる。横スクロール幅の見積もりにしか使わないので、
    /// 多めに出る分には（余白が少し広くなるだけで）問題ない。
    /// </summary>
    public int MaxLineLength { get; }

    /// <summary>行の内容を文字列として取り出す。スレッドセーフ。</summary>
    public string GetText(int index)
    {
        int start = _starts[index];
        int length = _lengths[index];
        if (length == 0) return string.Empty;

        return _text is not null
            ? _text.Substring(start, length)
            : Encoding.GetString(_bytes!, start, length);
    }

    /// <summary>行の長さ（<see cref="MaxLineLength"/> と同じ単位）。</summary>
    public int GetLength(int index) => _lengths[index];

    /// <summary>
    /// 文字列を作らずに行を読むための道具。デコード用バッファを内部で使い回すので、
    /// <b>スレッドごとに 1 つ</b>用意して使い終わったら破棄すること。
    /// </summary>
    public LineAccessor CreateAccessor() => new(this);

    internal ReadOnlySpan<char> ReadLine(int index, ref char[]? buffer)
    {
        int start = _starts[index];
        int length = _lengths[index];

        if (_text is not null) return _text.AsSpan(start, length);
        if (length == 0) return ReadOnlySpan<char>.Empty;

        int required = Encoding.GetMaxCharCount(length);
        if (buffer is null || buffer.Length < required)
        {
            if (buffer is not null) ArrayPool<char>.Shared.Return(buffer);
            buffer = ArrayPool<char>.Shared.Rent(required);
        }

        int written = Encoding.GetChars(_bytes.AsSpan(start, length), buffer);
        return buffer.AsSpan(0, written);
    }

    /// <summary>空のドキュメント（起動直後の状態）。</summary>
    public static LogDocument Empty { get; } =
        new(LogSource.None, string.Empty, string.Empty, null, string.Empty,
            Array.Empty<int>(), Array.Empty<int>(), 0, new UTF8Encoding(false), "UTF-8", 0);

    public bool IsEmptyDocument => Source == LogSource.None;

    /// <summary>
    /// 既にテキストになっているもの（クリップボードなど）からドキュメントを作る。
    /// 行インデックスの作成だけ行うので、呼び出し側でバックグラウンドに逃がすとよい。
    /// </summary>
    public static LogDocument FromText(string text, string displayName)
    {
        var (starts, lengths, maxLen) = BuildCharIndex(text);
        return new LogDocument(LogSource.Clipboard, string.Empty, displayName, null, text,
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
                progress?.Report(new LoadProgress("読み込み中", read * 70.0 / Math.Max(1, length)));
            }
            if (read != length) length = read;   // 読み込み中にファイルが縮んだ場合
        }

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new LoadProgress("文字コードを判定中", 75));
            var detection = TextEncodings.Detect(bytes, length, choice);

            ct.ThrowIfCancellationRequested();
            progress?.Report(new LoadProgress("行を解析中", 85));

            if (TextEncodings.IsLineScannable(detection.Encoding))
            {
                // バイト列のまま行に切る。全文を UTF-16 に展開しないので常駐が半分で済む
                if (detection.PreambleLength > 0 || length != bytes.Length)
                {
                    bytes = bytes.AsSpan(detection.PreambleLength, length - detection.PreambleLength).ToArray();
                }

                var (byteStarts, byteLengths, byteMax) = BuildByteIndex(bytes);
                progress?.Report(new LoadProgress("完了", 100));
                return new LogDocument(LogSource.File, path, Path.GetFileName(path), bytes, null,
                                       byteStarts, byteLengths, byteMax,
                                       detection.Encoding, detection.Name, info.Length);
            }

            // UTF-16 / ISO-2022-JP など、行をバイト単位で切れない文字コードは従来どおり全文を文字列にする
            string text = TextEncodings.DecodeAll(bytes, length, detection);
            bytes = Array.Empty<byte>();

            var (charStarts, charLengths, charMax) = BuildCharIndex(text);
            progress?.Report(new LoadProgress("完了", 100));
            return new LogDocument(LogSource.File, path, Path.GetFileName(path), null, text,
                                   charStarts, charLengths, charMax,
                                   detection.Encoding, detection.Name, info.Length);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// バイト列から改行位置を走査して行インデックスを作る。
    /// この方法が使えるのは <see cref="TextEncodings.IsLineScannable"/> が真の文字コードだけ。
    /// </summary>
    private static (int[] Starts, int[] Lengths, int MaxLength) BuildByteIndex(byte[] bytes)
    {
        var span = new ReadOnlySpan<byte>(bytes);
        if (span.Length == 0) return (Array.Empty<int>(), Array.Empty<int>(), 0);

        int estimate = Math.Max(16, span.Length / 60);
        var starts = new List<int>(estimate);
        var lengths = new List<int>(estimate);

        int pos = 0;
        int max = 0;
        while (pos < span.Length)
        {
            int rel = span[pos..].IndexOf((byte)'\n');
            int end = rel < 0 ? span.Length : pos + rel;
            int len = end - pos;
            if (len > 0 && span[end - 1] == (byte)'\r') len--;

            starts.Add(pos);
            lengths.Add(len);
            if (len > max) max = len;

            if (rel < 0) break;
            pos = end + 1;
        }

        return (starts.ToArray(), lengths.ToArray(), max);
    }

    /// <summary>
    /// 文字列から改行位置を走査して行インデックスを作る。CRLF / LF / 末尾改行なしをすべて扱う。
    /// </summary>
    private static (int[] Starts, int[] Lengths, int MaxLength) BuildCharIndex(string text)
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

/// <summary>
/// 行を文字列化せずに読むための道具。内部でデコード用バッファを使い回すため、
/// <b>スレッドをまたいで共有してはいけない</b>。
/// </summary>
public sealed class LineAccessor : IDisposable
{
    private readonly LogDocument _document;
    private char[]? _buffer;

    internal LineAccessor(LogDocument document) => _document = document;

    /// <summary>
    /// 行の内容を返す。戻り値は次に <see cref="GetSpan"/> を呼ぶまでしか有効でない。
    /// </summary>
    public ReadOnlySpan<char> GetSpan(int lineIndex) => _document.ReadLine(lineIndex, ref _buffer);

    public void Dispose()
    {
        if (_buffer is null) return;
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = null;
    }
}

public readonly record struct LoadProgress(string Phase, double Percent);
