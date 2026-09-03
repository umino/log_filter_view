using System.Windows.Media;

namespace LogFilterView.Views;

/// <summary>マーカー 1 色分。左端の帯・行背景・行番号で濃さを変えた 3 つの色を持つ。</summary>
public sealed class MarkerColor
{
    internal MarkerColor(int index, string name, uint accent, uint row, uint number)
    {
        Index = index;
        Name = name;
        Accent = Freeze(accent);
        Row = Freeze(row);
        Number = Freeze(number);
    }

    /// <summary>パレット上の位置。プロジェクトファイルにはこの番号を保存する。</summary>
    public int Index { get; }

    public string Name { get; }

    /// <summary>左端の帯。一番濃い。</summary>
    public Brush Accent { get; }

    /// <summary>行全体にうっすら敷く色。本文が読めるだけの薄さにする。</summary>
    public Brush Row { get; }

    /// <summary>行番号の文字色。</summary>
    public Brush Number { get; }

    public override string ToString() => Name;

    private static Brush Freeze(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }
}

/// <summary>マーカーに使える色の一覧。</summary>
public static class MarkerPalette
{
    /// <summary>色を指定せずにマーカーを付けたときの色（従来の橙）。</summary>
    public const int DefaultIndex = 0;

    public static IReadOnlyList<MarkerColor> Colors { get; } = new[]
    {
        new MarkerColor(0, "橙", 0xFFF57C00, 0xFFFFF1DC, 0xFFE65100),
        new MarkerColor(1, "赤", 0xFFE53935, 0xFFFDE7E9, 0xFFC62828),
        new MarkerColor(2, "黄", 0xFFFBC02D, 0xFFFFFAE0, 0xFFF57F17),
        new MarkerColor(3, "緑", 0xFF43A047, 0xFFE8F5E9, 0xFF2E7D32),
        new MarkerColor(4, "青", 0xFF1E88E5, 0xFFE3F2FD, 0xFF1565C0),
        new MarkerColor(5, "紫", 0xFF8E24AA, 0xFFF5E6FA, 0xFF6A1B9A),
    };

    public static int Count => Colors.Count;

    /// <summary>
    /// 色番号から色を引く。範囲外の番号は畳んで返すので、
    /// 将来パレットを減らしても古いプロジェクトが開けなくなることはない。
    /// </summary>
    public static MarkerColor Get(int index)
    {
        int normalized = index % Count;
        if (normalized < 0) normalized += Count;
        return Colors[normalized];
    }

    /// <summary>保存された色番号を、そのまま使える範囲に丸める。</summary>
    public static int Normalize(int index) => Get(index).Index;
}
