using System.Windows;
using System.Windows.Threading;
using LogFilterView.Models;
using LogFilterView.Services;
using LogFilterView.ViewModels;

namespace LogFilterView;

public partial class App : Application
{
    private SettingsService? _settingsService;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // .NET Core 以降は Shift_JIS 等が既定では使えないため最初に登録する
        TextEncodings.EnsureProviderRegistered();

        DispatcherUnhandledException += OnUnhandledException;

        _settingsService = new SettingsService();
        var settings = _settingsService.Load();
        _viewModel = new MainViewModel(_settingsService, settings);

        var window = new MainWindow(_viewModel, settings);
        MainWindow = window;
        window.Show();

        if (e.Args.Length > 0)
        {
            string path = e.Args[0];
            _ = ProjectService.IsProjectPath(path)
                ? _viewModel.OpenProjectAsync(path)
                : _viewModel.OpenAsync(path);
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"予期しないエラーが発生しました。\n\n{e.Exception}", "LogFilterView",
                        MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
