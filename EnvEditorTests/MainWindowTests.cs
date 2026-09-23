using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EnvEditorTests;

/// <summary>
/// 主窗口构造冒烟测试。
/// 回归目标：MainWindow.xaml 里 TabSync 带 IsChecked="True"，InitializeComponent 解析到它时会
/// 提前触发 Checked → OnNavSync，而此时 MainFrame 尚未赋值，曾导致启动即 NullReferenceException。
/// </summary>
public class MainWindowTests
{
    /// <remarks>
    /// 一个 AppDomain 只允许存在一个 Application 实例，故构造 + 断言必须放在同一个 STA 线程里一次做完。
    /// 页面里的 {StaticResource InvBool}/{StateBrush} 定义在 App.xaml，所以要先建 Application
    /// 并调用 InitializeComponent 加载其资源，否则 StaticResource 解析会失败。
    /// </remarks>
    [Fact]
    public void MainWindow_ConstructsAndDefaultsToSyncPage()
    {
        Exception? captured = null;
        string? initialPageType = null;
        var completed = false;

        var thread = new Thread(() =>
        {
            try
            {
                var app = new EnvEditor.App();
                app.InitializeComponent();
                var window = new EnvEditor.MainWindow();

                // MainFrame 是 XAML 生成的 internal 字段，测试程序集改用公开的 FindName
                var frame = (Frame)window.FindName("MainFrame")!;
                // Frame 在进入可视树之前不会提交 Content，必须 Show() 并让调度器跑完一轮导航
                window.Show();
                frame.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                initialPageType = frame.Content?.GetType().Name;
                window.Close();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                completed = true;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "构造主窗口超时（STA 线程未结束）");
        Assert.True(completed);
        Assert.Null(captured);
        Assert.Equal("SyncPage", initialPageType);
    }
}