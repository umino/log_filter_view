using System.Text;

namespace LogFilterView.Models;

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
    /// BOM → 厳密な UTF-8 デコード → Shift_JIS の順で判定し、デコード済み文字列を返す。
    /// UTF-8 の場合は判定に使ったデコード結果をそのまま使うので二度手間にならない。
    /// </summary>
    public static (string Text, Encoding Encoding, string Name) DecodeAuto(byte[] bytes, int length)
    {
        EnsureProviderRegistered();

        var (bomEncoding, bomLength, bomName) = DetectBom(bytes, length);
        if (bomEncoding is not null)
        {
            return (bomEncoding.GetString(bytes, bomLength, length - bomLength), bomEncoding, bomName);
        }

        // BOM なし: まず UTF-8 として厳密にデコードしてみる（成功すればそれが答え）
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            string text = strict.GetString(bytes, 0, length);
            return (text, new UTF8Encoding(false), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            // UTF-8 ではない
        }

        var sjis = Create(932, false);
        return (sjis.GetString(bytes, 0, length), sjis, "Shift_JIS");
    }

    /// <summary>指定した文字コードでデコードする（BOM があれば読み飛ばす）。</summary>
    public static (string Text, Encoding Encoding, string Name) Decode(byte[] bytes, int length, EncodingChoice choice)
    {
        if (choice.IsAuto) return DecodeAuto(bytes, length);

        var encoding = choice.CreateEncoding();
        int offset = 0;
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0 && length >= preamble.Length)
        {
            bool match = true;
            for (int i = 0; i < preamble.Length; i++)
            {
                if (bytes[i] != preamble[i]) { match = false; break; }
            }
            if (match) offset = preamble.Length;
        }
        else if (choice.CodePage == 65001 && HasUtf8Bom(bytes, length))
        {
            offset = 3;
        }

        return (encoding.GetString(bytes, offset, length - offset), encoding, choice.DisplayName);
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
