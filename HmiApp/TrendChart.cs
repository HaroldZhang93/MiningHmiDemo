// ============================================================================
//  TrendChart —— 轻量实时趋势图（不依赖任何第三方库，纯 WPF 绘制）
//  用法：Reset(标题,单位) 切换监测点；每次轮询调用 Add(值) 推入一个点。
//  内部维护一个固定长度的滚动缓冲，OnRender 里画网格 + 折线 + 当前值。
//  对应"生动的动态数据展示"。
// ============================================================================

using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HmiApp;

public class TrendChart : FrameworkElement
{
    private readonly Queue<double> _data = new();
    private const int Capacity = 120;          // 最多保留 120 个点（约 1.5 分钟 @800ms）
    private string _title = "趋势";
    private string _unit = "";

    private static readonly Typeface Font = new("Microsoft YaHei");
    private readonly Pen _linePen = new(new SolidColorBrush(Color.FromRgb(0x1D, 0x9B, 0xD6)), 2);
    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE3)), 1);
    private readonly Brush _axisText = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private readonly Brush _titleBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

    public TrendChart()
    {
        _linePen.Freeze();
        _gridPen.Freeze();
        _axisText.Freeze();
        _titleBrush.Freeze();
    }

    public void Reset(string title, string unit)
    {
        _title = title; _unit = unit; _data.Clear(); InvalidateVisual();
    }

    public void Add(double v)
    {
        _data.Enqueue(v);
        while (_data.Count > Capacity) _data.Dequeue();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        const double padL = 46, padR = 12, padT = 22, padB = 16;
        var plot = new Rect(padL, padT, Math.Max(1, w - padL - padR), Math.Max(1, h - padT - padB));

        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        dc.DrawRectangle(null, _gridPen, plot);
        dc.DrawText(MakeText(_title, 13, _titleBrush), new Point(padL, 3));

        var arr = _data.ToArray();
        if (arr.Length == 0)
        {
            dc.DrawText(MakeText("（未连接 / 无数据）", 12, _axisText),
                new Point(plot.Left + plot.Width / 2 - 60, plot.Top + plot.Height / 2 - 8));
            return;
        }

        double min = arr.Min(), max = arr.Max();
        if (Math.Abs(max - min) < 1e-9) { max = min + 1; min -= 1; }   // 平直数据也给出范围
        double pad = (max - min) * 0.12; min -= pad; max += pad;
        double range = max - min;

        // Y 轴网格 + 刻度（5 条）
        for (int i = 0; i <= 4; i++)
        {
            double t = i / 4.0;
            double y = plot.Bottom - t * plot.Height;
            double val = min + t * range;
            dc.DrawLine(_gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            dc.DrawText(MakeText(val.ToString("F1"), 10, _axisText), new Point(2, y - 7));
        }

        // 折线
        if (arr.Length >= 2)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    double x = plot.Left + (double)i / (arr.Length - 1) * plot.Width;
                    double y = plot.Bottom - (arr[i] - min) / range * plot.Height;
                    if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, _linePen, geo);
        }

        // 当前值
        dc.DrawText(MakeText($"当前 {arr[^1]:F1} {_unit}", 12, _linePen.Brush),
            new Point(plot.Right - 130, 3));
    }

    private static FormattedText MakeText(string s, double size, Brush b)
        => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, b, 1.0);
}
