using System.Windows;
using LogFilterView.Views;

namespace LogFilterView.ViewModels;

/// <summary>
/// 行テンプレートから参照する表示設定。ListBox.Tag に載せて、
/// 各行はここ 1 か所だけを見る（行ごとに複数の RelativeSource を辿らせないため）。
/// </summary>
public sealed class ViewSettings : ObservableObject
{
    private TextWrapping _textWrapping = TextWrapping.NoWrap;
    private Visibility _lineNumberVisibility = Visibility.Visible;
    private double _lineNumberWidth = 56;
    private double _contentMinWidth;
    private HighlightRuleSet _highlight = HighlightRuleSet.Empty;

    public TextWrapping TextWrapping
    {
        get => _textWrapping;
        set => SetProperty(ref _textWrapping, value);
    }

    public Visibility LineNumberVisibility
    {
        get => _lineNumberVisibility;
        set => SetProperty(ref _lineNumberVisibility, value);
    }

    public double LineNumberWidth
    {
        get => _lineNumberWidth;
        set => SetProperty(ref _lineNumberWidth, value);
    }

    /// <summary>
    /// 折り返しなしのときに横スクロール範囲を安定させるための最小幅。
    /// 仮想化パネルは実体化済みの行からしか幅を計算できず、スクロールのたびに
    /// スクロールバーが伸縮してしまうため、最長行から見積もった値を下限として与える。
    /// </summary>
    public double ContentMinWidth
    {
        get => _contentMinWidth;
        set => SetProperty(ref _contentMinWidth, value);
    }

    public HighlightRuleSet Highlight
    {
        get => _highlight;
        set => SetProperty(ref _highlight, value);
    }
}
