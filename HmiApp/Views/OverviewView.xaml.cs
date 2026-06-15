using System.Windows;
using System.Windows.Controls;

namespace HmiApp.Views;

public partial class OverviewView : UserControl
{
    public OverviewView() => InitializeComponent();

    // 设备墙列数（窄屏隐藏中间图表后，设备墙扩成 3 列铺满）。绑定到 UniformGrid.Columns。
    public int DeviceColumns
    {
        get => (int)GetValue(DeviceColumnsProperty);
        set => SetValue(DeviceColumnsProperty, value);
    }
    public static readonly DependencyProperty DeviceColumnsProperty =
        DependencyProperty.Register(nameof(DeviceColumns), typeof(int), typeof(OverviewView), new PropertyMetadata(2));

    // 按宽度自适应：图表区是严格 2×3，窗口变窄时优先收起"最左列"，最右列(移变电压+实时报警)保留最久；
    // 极窄时整块图表区收起、设备墙铺满。
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width;
        if (w >= 1400)                       // 宽：3 列全显
        {
            ChartCol.Width = new GridLength(2, GridUnitType.Star);
            DeviceColumns = 2;
            SetCol(ColA, CellA0, CellA1, true);
            SetCol(ColB, CellB0, CellB1, true);
            SetCol(ColC, CellC0, CellC1, true);
        }
        else if (w >= 1120)                  // 中：收起最左列(第1列)，留 2 列
        {
            ChartCol.Width = new GridLength(2, GridUnitType.Star);
            DeviceColumns = 2;
            SetCol(ColA, CellA0, CellA1, false);
            SetCol(ColB, CellB0, CellB1, true);
            SetCol(ColC, CellC0, CellC1, true);
        }
        else if (w >= 900)                   // 窄：再收起第2列，只留最右列
        {
            ChartCol.Width = new GridLength(1, GridUnitType.Star); // 单列时与设备墙各占一半
            DeviceColumns = 2;
            SetCol(ColA, CellA0, CellA1, false);
            SetCol(ColB, CellB0, CellB1, false);
            SetCol(ColC, CellC0, CellC1, true);
        }
        else                                 // 极窄：整块图表区收起，设备墙扩成 3 列铺满
        {
            ChartCol.Width = new GridLength(0);
            DeviceColumns = 3;
        }
    }

    // 收起/展开某一列：列宽归零并隐藏该列的两个单元，保证不占位、不参与布局
    private static void SetCol(ColumnDefinition col, UIElement top, UIElement bottom, bool show)
    {
        col.Width = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var v = show ? Visibility.Visible : Visibility.Collapsed;
        top.Visibility = v;
        bottom.Visibility = v;
    }
}
