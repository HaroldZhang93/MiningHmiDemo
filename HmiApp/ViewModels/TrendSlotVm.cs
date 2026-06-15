// 一个"趋势图槽位"：自带选点下拉(Points/Selected) + 一条 LiveCharts 折线。
// 总览放多个槽位，用户每个图各自选监控项，互不影响。
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace HmiApp.ViewModels;

public partial class TrendSlotVm : ObservableObject
{
    private readonly ObservableCollection<double> _vals = new();
    public ObservableCollection<PointVm> Points { get; }
    public ISeries[] Series { get; }
    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    [ObservableProperty] private PointVm? _selected;
    partial void OnSelectedChanged(PointVm? value) => _vals.Clear();

    public TrendSlotVm(ObservableCollection<PointVm> points, PointVm? initial, string hex)
    {
        Points = points;
        Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = _vals,
                Stroke = new SolidColorPaint(SKColor.Parse(hex)) { StrokeThickness = 2 },
                Fill = null, GeometrySize = 0, LineSmoothness = 0.3,
            }
        };
        XAxes = new[] { new Axis { IsVisible = false } };
        YAxes = new[] { new Axis { LabelsPaint = new SolidColorPaint(SKColor.Parse("#8FA6C4")), SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#244468")) { StrokeThickness = 1 } } };
        Selected = initial;
    }

    public void Push()
    {
        if (Selected == null) return;
        _vals.Add(Selected.Numeric);
        while (_vals.Count > 120) _vals.RemoveAt(0);
    }
}
