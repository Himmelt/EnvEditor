using System.Windows;
using System.Windows.Controls;
using EnvEditor.ViewModels;

namespace EnvEditor;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SyncPage _syncPage;
    private readonly SettingsPage _settingsPage;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        _syncPage = new SyncPage { DataContext = _vm };
        _settingsPage = new SettingsPage { DataContext = _vm };
        DataContext = _vm;
        MainFrame.Content = _syncPage;
        Loaded += (_, _) => _vm.LoadConfig();
    }

    private void OnNavSync(object sender, RoutedEventArgs e) => MainFrame.Content = _syncPage;
    private void OnNavSettings(object sender, RoutedEventArgs e) => MainFrame.Content = _settingsPage;
}
