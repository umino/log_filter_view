using System.IO;
using System.Text;
using Sudare.Models;

namespace Sudare.Services;

public static class LogExporter
{
    /// <summary>
    /// 現在表示中（抽出後）の行をファイルへ書き出す。
    /// 行文字列を作らず <see cref="ReadOnlySpan{T}"/> のまま書くので、100 万行でもメモリを食わない。
    /// </summary>
    public static async Task ExportAsync(string path, VirtualLineCollection lines, Encoding encoding,
                                         bool withLineNumbers, string newLine,
                                         IProgress<double>? progress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read,
                                              bufferSize: 1 << 20);
            using var writer = new StreamWriter(stream, encoding, bufferSize: 1 << 20);
            writer.NewLine = newLine;

            // このスレッド専用のアクセサ（UI 側が使っているものとは共有しない）
            using var accessor = lines.Document.CreateAccessor();

            int count = lines.Count;
            int width = count == 0 ? 1 : lines.ToLineNumber(count - 1).ToString().Length;
            Span<char> numberBuffer = stackalloc char[24];
            int lastPercent = -1;

            for (int i = 0; i < count; i++)
            {
                if ((i & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();

                if (withLineNumbers)
                {
                    int lineNumber = lines.ToLineNumber(i);
                    lineNumber.TryFormat(numberBuffer, out int written);
                    for (int p = written; p < width; p++) writer.Write(' ');
                    writer.Write(numberBuffer[..written]);
                    writer.Write(": ");
                }

                writer.Write(accessor.GetSpan(lines.ToLineIndex(i)));
                writer.Write(newLine);

                if (progress is not null && count > 0)
                {
                    int percent = (int)(i * 100L / count);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress.Report(percent);
                    }
                }
            }
        }, ct).ConfigureAwait(false);
    }
}
