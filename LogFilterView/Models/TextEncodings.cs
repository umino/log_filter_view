using System.Text;
using System.Text.Unicode;

namespace LogFilterView.Models;

/// <summary>文字コードの判定結果。本文のデコードはまだ行っていない。</summary>
public readonly record struct EncodingDetection(Encoding Encoding, string Name, int PreambleLength);

/// <summary>UI に並べる文字コードの選択肢。</summary>
public sealed class EncodingChoice
{
    public EncodingChoice(string displayName, int codePage, bool bom = false)
    {
        DisplayName = displayName;
        CodePage = codePage;
        Bom = bom;
    }

    public string DisplayName { get; }

    /// <summary>0 は自動判別を表す。</summary>
    public int CodePage { get; }

    /// <summary>書き出し時に BOM を付けるか。</summary>
    public bool Bom { get; }

    public bool IsAuto => CodePage == 0;

    /// <summary>設定ファイルに保存するためのキー。</summary>
    public string Key => $"{CodePage}:{(Bom ? 1 : 0)}";

    public Encoding CreateEncoding() => TextEncodings.Create(CodePage, Bom);

    public override string ToString() => DisplayName;
}

public static class TextEncodings
{
    private static bool _providerRegistered;

    /// <summary>Shift_JIS などを使えるようにする。アプリ起動時に 1 度だけ呼ぶ。</summary>
    public static void EnsureProviderRegistered()
    {
        if (_providerRegistered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _providerRegistered = true;
    }

    public static readonly EncodingChoice Auto = new("自動判別", 0);

    public static IReadOnlyList<EncodingChoice> All { get; } = new[]
    {
        Auto,
        new EncodingChoice("UTF-8", 65001),
        new EncodingChoice("UTF-8 (BOM)", 65001, bom: true),
        new EncodingChoice("Shift_JIS", 932),
        new EncodingChoice("EUC-JP", 51932),
        new EncodingChoice("ISO-2022-JP (JIS)", 50220),
        new EncodingChoice("UTF-16 LE", 1200, bom: true),
        new EncodingChoice("UTF-16 BE", 1201, bom: true),
        new EncodingChoice("UTF-32 LE", 12000, bom: true),
        new EncodingChoice("Windows-1252", 1252),
        new EncodingChoice("ISO-8859-1", 28591),
        new EncodingChoice("US-ASCII", 20127),
    };

    public static EncodingChoice FromKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return Auto;
        foreach (var c in All)
        {
            if (c.Key == key) return c;
        }
        return Auto;
    }

    public static EncodingChoice FromCodePage(int codePage)
    {
        foreach (var c in All)
        {
            if (!c.IsAuto && c.CodePage == codePage) return c;
        }
        return Auto;
    }

    public static Encoding Create(int codePage, bool bom)
    {
        EnsureProviderRegistered();
        return codePage switch
        {
            0 or 65001 => new UTF8Encoding(bom, throwOnInvalidBytes: false),
            1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: bom),
            1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: bom),
            12000 => new UTF32Encoding(bigEndian: false, byteOrderMark: bom),
            _ => Encoding.GetEncoding(codePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback),
        };
    }

    /// <summary>
    /// 文字コードだけを判定する。本文はデコードしないので、100MB でも数ミリ秒で終わる。
    /// 自動判別は「BOM → UTF-8 として妥当か → Shift_JIS」の順。
    /// </summary>
    public static EncodingDetection Detect(byte[] bytes, int length, EncodingChoice choice)
    {
        EnsureProviderRegistered();

        if (!choice.IsAuto)
        {
            var chosen = choice.CreateEncoding();
            return new EncodingDetection(chosen, choice.DisplayName, GetPreambleLength(chosen, choice, bytes, length));
        }

        var (bomEncoding, bomLength, bomName) = DetectBom(bytes, length);
        if (bomEncoding is not null) return new EncodingDetection(bomEncoding, bomName, bomLength);

        // 文字列を作らずに UTF-8 として妥当かどうかだけを見る
        if (Utf8.IsValid(bytes.AsSpan(0, length)))
        {
            return new EncodingDetection(new UTF8Encoding(false), "UTF-8", 0);
        }

        return new EncodingDetection(Create(932, false), "Shift_JIS", 0);
    }

    /// <summary>判定結果に従って全体をデコードする（UTF-16 系など、行をバイト単位で切れない場合に使う）。</summary>
    public static string DecodeAll(byte[] bytes, int length, EncodingDetection detection) =>
        detection.Encoding.GetString(bytes, detection.PreambleLength, length - detection.PreambleLength);

    /// <summary>
    /// 改行 <c>0x0A</c> をバイト列のまま走査してよい文字コードか。
    /// </summary>
    /// <remarks>
    /// 条件は 2 つ。(1) 多バイト文字の途中に 0x0A が現れないこと、
    /// (2) 行を単独でデコードできること（直前の行の状態に依存しないこと）。
    /// UTF-16 / UTF-32 は (1) を満たさず、ISO-2022-JP はエスケープシーケンスで状態が変わるため (2) を満たさない。
    /// 判断を誤ると文字化けするので、安全側に倒して明示的な許可リストにしている。
    /// </remarks>
    public static bool IsLineScannable(Encoding encoding) => encoding.CodePage switch
    {
        65001 => true,   // UTF-8
        932 => true,     // Shift_JIS（第 2 バイトは 0x40-0x7E, 0x80-0xFC）
        51932 => true,   // EUC-JP
        20932 => true,   // EUC-JP (JIS X 0212)
        1252 => true,    // Windows-1252
        28591 => true,   // ISO-8859-1
        20127 => true,   // US-ASCII
        _ => false,
    };

    private static int GetPreambleLength(Encoding encoding, EncodingChoice choice, byte[] bytes, int length)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0 && length >= preamble.Length)
        {
            bool match = true;
            for (int i = 0; i < preamble.Length; i++)
            {
                if (bytes[i] != preamble[i]) { match = false; break; }
            }
            if (match) return preamble.Length;
        }

        // BOM なし指定で開いた UTF-8 ファイルにも BOM が付いていることがある
        if (choice.CodePage == 65001 && HasUtf8Bom(bytes, length)) return 3;
        return 0;
    }

    private static bool HasUtf8Bom(byte[] b, int len) =>
        len >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    private static (Encoding? Encoding, int BomLength, string Name) DetectBom(byte[] b, int len)
    {
        if (len >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0x00 && b[3] == 0x00)
            return (new UTF32Encoding(false, true), 4, "UTF-32 LE (BOM)");
        if (len >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0xFE && b[3] == 0xFF)
            return (new UTF32Encoding(true, true), 4, "UTF-32 BE (BOM)");
        if (HasUtf8Bom(b, len))
            return (new UTF8Encoding(true), 3, "UTF-8 (BOM)");
        if (len >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return (new UnicodeEncoding(false, true), 2, "UTF-16 LE (BOM)");
        if (len >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return (new UnicodeEncoding(true, true), 2, "UTF-16 BE (BOM)");
        return (null, 0, string.Empty);
    }
}
