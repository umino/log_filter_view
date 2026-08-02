using System.Windows.Media;

namespace LogFilterView.ViewModels;

/// <summary>
/// 「含む」リストの 1 行。
/// </summary>
/// <remarks>
/// OFF の行は本文中では <c>#</c> 始まり（コメント）として保持する。
/// 抽出側は元々 <c>#</c> 行を読み飛ばす作りなので、
/// プリセット・プロジェクト・設定のファイル形式を変えずに ON/OFF を持ち回せる。
/// </remarks>
public sealed class PatternLine : ObservableObject
{
    private string _text;
    private bool _isEnabled;
    private Brush? _color;

    public PatternLine(string text, bool isEnabled)
    {
        _text = text;
        _isEnabled = isEnabled;
    }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty);
    }

    /// <summary>この行を抽出条件として使うか。OFF でも文言は残す。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>
    /// この行に割り当てられた強調色。ログ本文の色と同じものを指す。
    /// OFF の行と空行では <c>null</c>（色は付かない）。
    /// </summary>
    public Brush? Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public bool IsBlank => Text.Trim().Length == 0;
}
