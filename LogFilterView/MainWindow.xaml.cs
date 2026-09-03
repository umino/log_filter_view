using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LogFilterView.Models;
using LogFilterView.Services;
using LogFilterView.ViewModels;

namespace LogFilterView;

public partial class MainWindow : Window
{
    /// <summary>この行数を超える一括選択・コピーは確認してから行う。</summary>
    private const int BulkOperationThreshold = 200_000;

    public static readonly RoutedUICommand CopyWithLineNumbersCommand =
        new("行番号付きでコピー", nameof(CopyWithLineNumbersCommand), typeof(MainWindow));

    public static readonly RoutedUICommand FocusSearchCommand =
        new("検索ボックスへ", nameof(FocusSearchCommand), typeof(MainWindow));

    public static readonly RoutedUICommand FocusGoToCommand =
        new("行番号ボックスへ", nameof(FocusGoToCommand), typeof(MainWindow));

    public static readonly RoutedUICommand AboutCommand =
        new("バージョン情報", nameof(AboutCommand), typeof(MainWindow));

    public static readonly RoutedUICommand ToggleMarkerCommand =
        new("マーカーの ON/OFF", nameof(ToggleMarkerCommand), typeof(MainWindow));

    /// <summary>色を指定してマーカーを反転させる。CommandParameter は色番号（0 起点）。</summary>
    public static readonly RoutedUICommand ToggleMarkerColorCommand =
        new("色を指定してマーカーの ON/OFF", nameof(ToggleMarkerColorCommand), typeof(MainWindow));

    public static readonly RoutedUICommand ExpandAroundCommand =
        new("この行の前後を展開", nameof(ExpandAroundCommand), typeof(MainWindow));

    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private ScrollViewer? _listScrollViewer;

    public MainWindow(MainViewModel viewModel, AppSettings settings)
    {
        _viewModel = viewModel;
        _settings = settings;

        InitializeComponent();
        DataContext = viewModel;

        RestoreWindowPlacement();
        ApplyFilterPaneState();

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.ScrollRequested += OnScrollRequested;
        viewModel.FocusSearchRequested += () => FocusAndSelect(SearchBox);

        LineList.PreviewKeyDown += LineList_PreviewKeyDown;
    }

    #region ウィンドウ状態

    private void RestoreWindowPlacement()
    {
        if (_settings.WindowWidth > 200) Width = _settings.WindowWidth;
        if (_settings.WindowHeight > 200) Height = _settings.WindowHeight;

        if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
        {
            // 前回のモニタが外れている場合に画面外へ行かないようにする
            var area = SystemParameters.WorkArea;
            if (_settings.WindowLeft > area.Left - Width + 80 && _settings.WindowLeft < area.Right - 80 &&
                _settings.WindowTop >= area.Top - 40 && _settings.WindowTop < area.Bottom - 80)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _settings.WindowLeft;
                Top = _settings.WindowTop;
            }
        }

        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
        if (_settings.FilterPaneWidth >= 240) FilterColumn.Width = new GridLength(_settings.FilterPaneWidth);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (bounds.Width > 0)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }

        if (_viewModel.FilterPaneVisible && FilterColumn.Width.IsAbsolute && FilterColumn.Width.Value >= 240)
        {
            _settings.FilterPaneWidth = FilterColumn.Width.Value;
        }

        _viewModel.SaveSettings();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.FilterPaneVisible)) ApplyFilterPaneState();
    }

    private void ApplyFilterPaneState()
    {
        bool visible = _viewModel.FilterPaneVisible;

        if (!visible && FilterColumn.Width.IsAbsolute && FilterColumn.Width.Value >= 240)
        {
            _settings.FilterPaneWidth = FilterColumn.Width.Value;
        }

        FilterPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PaneSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FilterColumn.MinWidth = visible ? 240 : 0;
        FilterColumn.Width = visible
            ? new GridLength(Math.Max(240, _settings.FilterPaneWidth))
            : new GridLength(0);
    }

    #endregion

    #region スクロール / フォーカス

    private void OnScrollRequested(int viewIndex)
    {
        // 仮想化された ListBox では ScrollIntoView(item) が全件走査になりかねないので、
        // ScrollUnit=Item を前提に「行単位のオフセット」を直接指定する。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var scrollViewer = GetListScrollViewer();
            if (scrollViewer is null) return;

            double viewport = Math.Max(1, scrollViewer.ViewportHeight);
            double offset = scrollViewer.VerticalOffset;

            if (viewIndex >= offset + 1 && viewIndex < offset + viewport - 1) return;   // 既に見えている

            double target = viewIndex - Math.Floor(viewport / 3);
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, target));
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private ScrollViewer? GetListScrollViewer()
    {
        if (_listScrollViewer is not null) return _listScrollViewer;
        _listScrollViewer = FindDescendant<ScrollViewer>(LineList);
        return _listScrollViewer;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var found = FindDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static void FocusAndSelect(TextBox box)
    {
        box.Focus();
        box.SelectAll();
    }

    private void FocusSearchExecuted(object sender, ExecutedRoutedEventArgs e) => FocusAndSelect(SearchBox);

    private void FocusGoToExecuted(object sender, ExecutedRoutedEventArgs e) => FocusAndSelect(GoToBox);

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _viewModel.FindPreviousCommand.Execute(null);
        else _viewModel.FindNextCommand.Execute(null);
        e.Handled = true;
    }

    private void GoToBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _viewModel.GoToLineCommand.Execute(null);
        e.Handled = true;
    }

    private void LineList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+A は全行を実体化してしまうため、行数が多いときは確認する
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (_viewModel.Lines.Count > BulkOperationThreshold && !ConfirmBulkOperation(_viewModel.Lines.Count))
            {
                e.Handled = true;
            }
        }
    }

    private static bool ConfirmBulkOperation(int count) =>
        MessageBox.Show($"{count:N0} 行を対象にします。時間とメモリを消費しますが続行しますか?\n" +
                        "（全体をファイルに書き出すだけなら「抽出結果を保存」のほうが高速です）",
                        "LogFilterView", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    #endregion

    #region マーカー / 前後の展開

    private void SelectionRequired(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = LineList.SelectedItems.Count > 0;

    private void ToggleMarkerExecuted(object sender, ExecutedRoutedEventArgs e) =>
        _viewModel.ToggleMarkers(SelectedLineNumbers());

    private void ToggleMarkerColorExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (!MainViewModel.TryParseColorIndex(e.Parameter, out int colorIndex)) return;
        _viewModel.ToggleMarkerColor(SelectedLineNumbers(), colorIndex);
    }

    private void ExpandAroundExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (!int.TryParse(e.Parameter as string, out int radius)) radius = 20;
        _viewModel.ExpandAround(SelectedLineNumbers(), radius);
    }

    private List<int> SelectedLineNumbers() =>
        LineList.SelectedItems.OfType<LineRow>().Select(r => r.LineNumber).ToList();

    #endregion

    #region コピー

    private void CopyCanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = LineList.SelectedItems.Count > 0;

    private void CopyExecuted(object sender, ExecutedRoutedEventArgs e) => CopySelection(false);

    private void CopyWithLineNumbersExecuted(object sender, ExecutedRoutedEventArgs e) => CopySelection(true);

    private void CopySelection(bool withLineNumbers)
    {
        var rows = LineList.SelectedItems.OfType<LineRow>().OrderBy(r => r.ViewIndex).ToList();
        if (rows.Count == 0) return;
        if (rows.Count > BulkOperationThreshold && !ConfirmBulkOperation(rows.Count)) return;

        int width = withLineNumbers ? rows[^1].LineNumberText.Length : 0;
        var builder = new StringBuilder(rows.Count * 80);
        foreach (var row in rows)
        {
            if (withLineNumbers)
            {
                builder.Append(row.LineNumberText.PadLeft(width)).Append(": ");
            }
            builder.AppendLine(row.Text);
        }

        try
        {
            Clipboard.SetText(builder.ToString());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"クリップボードにコピーできませんでした。\n{ex.Message}", "LogFilterView",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region ドラッグ＆ドロップ

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        e.Handled = true;

        string path = files[0];
        _ = ProjectService.IsProjectPath(path)
            ? _viewModel.OpenProjectAsync(path)
            : _viewModel.OpenAsync(path);
    }

    #endregion

    private void AboutExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "1.0";
        MessageBox.Show(
            $"LogFilterView {version}\n\n" +
            "ログ閲覧に特化したテキストビューアです。\n" +
            "含む語 / 除外語による行フィルタ、ワイルドカード・正規表現検索、\n" +
            "抽出結果の保存、折り返し表示、文字コード切り替えに対応しています。",
            "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
