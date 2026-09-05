using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGen;

/// <summary>
/// Sudare のアイコンを描いて .ico と PNG を書き出す。
///
/// 図柄は「簾（すだれ）」＝横に並んだスラット。そのままログの行にも見えるようにし、
/// 抽出で残った行を表す 2 本だけ、本文の強調色と同じ黄・緑で塗る。
/// 小さいサイズでも潰れないよう、要素は横棒だけに絞っている。
/// </summary>
internal static class Program
{
    /// <summary>ICO に入れるサイズ。256 だけ PNG 圧縮、それ以外は BMP で持つ。</summary>
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    // 本文の強調パレット（HighlightRuleSet）と同じ色を使い、アプリと図柄を結び付ける
    private static readonly Color Background = Color.FromRgb(0x1E, 0x2A, 0x38);
    private static readonly Color BackgroundLow = Color.FromRgb(0x15, 0x1F, 0x2B);
    private static readonly Color Slat = Color.FromRgb(0x7C, 0x8D, 0xA6);
    private static readonly Color Accent1 = Color.FromRgb(0xFF, 0xF1, 0x76);   // 黄
    private static readonly Color Accent2 = Color.FromRgb(0xA5, 0xD6, 0xA7);   // 緑

    /// <summary>
    /// スラット 1 本。y は上端、len は描画領域に対する長さの割合、
    /// color は塗り、thread はその行に綴じ糸を重ねるか。
    /// </summary>
    private readonly record struct Bar(double Y, double Len, Color Color);

    /// <summary>綴じ糸の横位置（アイコン幅に対する割合）。</summary>
    private static readonly double[] Threads = { 0.32, 0.62 };

    // 上から 5 本。16px でも 1 本ずつ分かれて見える太さと間隔にしてある。
    // 長さを散らしてログの行らしさを出しつつ、色付きの 2 本（＝抽出で残った行）を
    // いちばん長くして主役にする。
    private static readonly Bar[] Bars =
    {
        // 長さは、右端が綴じ糸（Threads）の近くで終わらない値を選ぶ。
        // 糸のすぐ右で終わるとスラット 1 本ぶんに満たない破片が残り、
        // 丸めのせいで小さな出っ張りに見えてしまう。
        new(0.1600, 0.86, Slat),
        new(0.3075, 1.00, Accent1),
        new(0.4550, 0.50, Slat),
        new(0.6025, 1.00, Accent2),
        new(0.7500, 0.90, Slat),
    };

    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);

        var frames = new List<(int Size, byte[] Data, bool Png)>();
        foreach (int size in Sizes)
        {
            var bitmap = Render(size);
            bool png = size >= 256;
            frames.Add((size, png ? EncodePng(bitmap) : EncodeBmp(bitmap), png));

            // 目視確認と README 用に、大きいものは PNG でも残す
            if (size is 256 or 64 or 32 or 16)
            {
                File.WriteAllBytes(Path.Combine(outDir, $"preview_{size}.png"), EncodePng(bitmap));
            }
        }

        string icoPath = Path.Combine(outDir, "Sudare.ico");
        File.WriteAllBytes(icoPath, BuildIco(frames));

        var info = new FileInfo(icoPath);
        Console.WriteLine($"{icoPath}  {info.Length:N0} bytes  ({frames.Count} フレーム)");
        foreach (var (size, data, png) in frames)
        {
            Console.WriteLine($"  {size,3}x{size,-3} {(png ? "PNG" : "BMP")}  {data.Length,8:N0} bytes");
        }
        return 0;
    }

    #region 描画

    private static RenderTargetBitmap Render(int px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, px);

        var bitmap = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Draw(DrawingContext dc, double px)
    {
        // 背景。アイコン全体に掛かる縦グラデーションを絶対座標で作っておき、
        // あとで綴じ糸の「切り欠き」にも同じものを使う（継ぎ目が出ないようにするため）。
        var background = new LinearGradientBrush(Background, BackgroundLow, 0)
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, px),
        };
        background.Freeze();

        // 角丸の比率はサイズによらず一定にする
        var body = new Rect(0, 0, px, px);
        double corner = px * 0.185;
        dc.DrawRoundedRectangle(background, null, body, corner, corner);

        double left = px * 0.135;
        double field = px * 0.73;                 // スラットを描ける横幅
        double height = px * 0.095;               // スラット 1 本の高さ
        double radius = height / 2;

        foreach (var bar in Bars)
        {
            var brush = new SolidColorBrush(bar.Color);
            brush.Freeze();
            var rect = new Rect(left, px * bar.Y, field * bar.Len, height);

            // 16px では丸めが潰れて滲むので、小さいときは角を落とさない
            if (px >= 32) dc.DrawRoundedRectangle(brush, null, rect, radius, radius);
            else dc.DrawRectangle(brush, null, rect);
        }

        // 簾の綴じ糸。背景色でスラットを縦に切り抜くと、桟を糸で編んだ見え方になる。
        // 小さいサイズでは 1px を割ってスラットを濁らせるだけなので描かない。
        if (px >= 48)
        {
            double threadWidth = Math.Max(1, Math.Round(px * 0.028));
            foreach (double x in Threads)
            {
                // 上下いっぱいに引く。背景の上では見えず、スラットを横切る所だけが糸になる
                double cut = Math.Round(px * x - threadWidth / 2);
                dc.DrawRectangle(background, null, new Rect(cut, 0, threadWidth, px));
            }
        }

        // 暗い背景に置いたときに輪郭が沈まないよう、ごく薄い内側の縁を足す
        if (px < 32) return;
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)), Math.Max(1, px * 0.008));
        edge.Freeze();
        double inset = edge.Thickness / 2;
        dc.DrawRoundedRectangle(null, edge,
            new Rect(inset, inset, px - edge.Thickness, px - edge.Thickness), corner - inset, corner - inset);
    }

    #endregion

    #region エンコード

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// ICO 内の BMP フレームを作る。BITMAPINFOHEADER の高さは
    /// 「色 + マスク」の 2 枚ぶんを指定し、画素は下から上へ並べる決まり。
    /// 透過は 32bit の α で表すので、AND マスクは全 0（＝不透明）でよい。
    /// </summary>
    private static byte[] EncodeBmp(BitmapSource bitmap)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        int stride = width * 4;

        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        int maskStride = (width + 31) / 32 * 4;
        int maskSize = maskStride * height;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(40);                       // biSize
        writer.Write(width);                    // biWidth
        writer.Write(height * 2);               // biHeight（色 + マスク）
        writer.Write((short)1);                 // biPlanes
        writer.Write((short)32);                // biBitCount
        writer.Write(0);                        // biCompression = BI_RGB
        writer.Write(stride * height + maskSize);
        writer.Write(0);                        // biXPelsPerMeter
        writer.Write(0);                        // biYPelsPerMeter
        writer.Write(0);                        // biClrUsed
        writer.Write(0);                        // biClrImportant

        for (int y = height - 1; y >= 0; y--) writer.Write(pixels, y * stride, stride);
        writer.Write(new byte[maskSize]);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildIco(List<(int Size, byte[] Data, bool Png)> frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((short)0);                 // 予約
        writer.Write((short)1);                 // 1 = アイコン
        writer.Write((short)frames.Count);

        int offset = 6 + 16 * frames.Count;
        foreach (var (size, data, _) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));   // 256 は 0 で表す
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);              // パレット数
            writer.Write((byte)0);              // 予約
            writer.Write((short)1);             // プレーン数
            writer.Write((short)32);            // ビット深度
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data, _) in frames) writer.Write(data);

        writer.Flush();
        return stream.ToArray();
    }

    #endregion
}
