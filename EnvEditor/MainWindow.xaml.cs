using System.Windows;
using System.Windows.Controls;
using EnvEditor.ViewModels;

namespace EnvEditor;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SyncPage _syncPage;
    private readonly SettingsPage _settingsPage;
    // InitializeComponent 解析 XAML 时，TabSync 的 IsChecked="True" 会提前触发 OnNavSync，
    // 那一刻 MainFrame/_syncPage 都还没赋值。用此标志挡掉初始化期间的那次回调。
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        _syncPage = new SyncPage { DataContext = _vm };
        _settingsPage = new SettingsPage { DataContext = _vm };
        DataContext = _vm;
        MainFrame.Content = _syncPage; // 默认页在此显式设置，不再依赖 XAML 的 Checked
        _ready = true;
        Loaded += (_, _) => _vm.LoadConfig();
    }

    private void OnNavSync(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        MainFrame.Content = _syncPage;
    }

    private void OnNavSettings(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        MainFrame.Content = _settingsPage;
    }
}
