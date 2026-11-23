using System.Windows;
using NightCoopLauncher.App.ViewModels;

namespace NightCoopLauncher.App;

public partial class MainWindow : Window
{
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // The design size is larger than a small or strongly scaled screen.
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);

        viewModel.GameLaunched += (_, _) => WindowState = WindowState.Minimized;
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
