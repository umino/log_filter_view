using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Sudare.Views;

/// <summary>
/// <see cref="TextBlock"/> にキーワード強調を付ける添付プロパティ。
/// 仮想化された ListBox 上で、実体化された行に対してのみ働く。
/// </summary>
public static class TextHighlighter
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.RegisterAttached(
            "SourceText", typeof(string), typeof(TextHighlighter),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty RulesProperty =
        DependencyProperty.RegisterAttached(
            "Rules", typeof(HighlightRuleSet), typeof(TextHighlighter),
            new PropertyMetadata(null, OnChanged));

    public static string? GetSourceText(DependencyObject d) => (string?)d.GetValue(SourceTextProperty);
    public static void SetSourceText(DependencyObject d, string? value) => d.SetValue(SourceTextProperty, value);

    public static HighlightRuleSet? GetRules(DependencyObject d) => (HighlightRuleSet?)d.GetValue(RulesProperty);
    public static void SetRules(DependencyObject d, HighlightRuleSet? value) => d.SetValue(RulesProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock) Render(textBlock);
    }

    private static void Render(TextBlock textBlock)
    {
        string text = GetSourceText(textBlock) ?? string.Empty;
        var rules = GetRules(textBlock);

        if (rules is null || rules.IsEmpty || text.Length == 0)
        {
            // Text を直接設定すると Inlines は自動的に破棄される（最速の経路）
            textBlock.Text = text;
            return;
        }

        var ranges = rules.Compute(text);
        if (ranges.Count == 0)
        {
            textBlock.Text = text;
            return;
        }

        var inlines = textBlock.Inlines;
        inlines.Clear();

        int cursor = 0;
        foreach (var (start, length, brush) in ranges)
        {
            if (start > cursor) inlines.Add(new Run(text[cursor..start]));
            inlines.Add(new Run(text.Substring(start, length)) { Background = brush });
            cursor = start + length;
        }
        if (cursor < text.Length) inlines.Add(new Run(text[cursor..]));
    }
}
