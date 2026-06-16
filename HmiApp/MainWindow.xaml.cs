// MainWindow：MVVM 下几乎零逻辑——DataContext 在 XAML 里设为 MainViewModel。
using System;
using System.Windows;

namespace HmiApp;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // 关闭时取消后台采集/调度/PLC 轮询，避免进程退出时后台 Task 仍在跑、刷异常或泄漏。
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as ViewModels.MainViewModel)?.Shutdown();
        base.OnClosed(e);
    }
}
