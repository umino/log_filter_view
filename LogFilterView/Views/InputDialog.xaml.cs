using System.Windows;
using System.Windows.Input;

namespace LogFilterView.Views;

public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
    }

    public string InputText
    {
        get => InputBox.Text;
        set => InputBox.Text = value;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }

    /// <summary>入力を求めて、キャンセルされたら null を返す。</summary>
    public static string? Ask(Window? owner, string title, string message, string initial = "")
    {
        var dialog = new InputDialog
        {
            Owner = owner,
            Title = title,
            InputText = initial,
        };
        dialog.MessageText.Text = message;
        dialog.InputBox.SelectAll();
        return dialog.ShowDialog() == true ? dialog.InputText.Trim() : null;
    }
}
