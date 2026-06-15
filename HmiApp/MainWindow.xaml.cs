// MainWindow：MVVM 下几乎零逻辑——DataContext 在 XAML 里设为 MainViewModel。
using System.Windows;

namespace HmiApp;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
