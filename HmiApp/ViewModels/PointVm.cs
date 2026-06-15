// 一个监测/控制点位的 ViewModel：值变化通过 CommunityToolkit 的源生成器自动通知界面。
using CommunityToolkit.Mvvm.ComponentModel;
using Shared;

namespace HmiApp.ViewModels;

public partial class PointVm : ObservableObject
{
    public RegPoint Point { get; }
    public string Device => Point.Device;
    public string Name => Point.Name;
    public string AreaText { get; }
    public string AddressText => Point.Address.ToString();
    public string Unit => Point.IsBit ? "" : Point.Unit;
    public bool IsBit => Point.IsBit;

    [ObservableProperty] private string _value = "--";
    [ObservableProperty] private double _numeric;
    [ObservableProperty] private bool _isOn;       // 位类型：当前是否为 1
    [ObservableProperty] private bool _isAlarm;    // 是否处于报警（AlarmHigh 且为 1）

    public PointVm(RegPoint p)
    {
        Point = p;
        AreaText = AreaTextOf(p.Area);
    }

    public void SetBit(bool on)
    {
        IsOn = on;
        IsAlarm = on && Point.AlarmHigh;
        Numeric = on ? 1 : 0;
        Value = on ? "ON" : "OFF";
    }

    public void SetReg(ushort raw)
    {
        Numeric = raw * Point.Scale;
        Value = Point.Scale == 1.0 ? raw.ToString() : Numeric.ToString("F1");
    }

    private static string AreaTextOf(Area a) => a switch
    {
        Area.Coil => "线圈",
        Area.DiscreteInput => "离散输入",
        Area.InputRegister => "输入寄存器",
        Area.HoldingRegister => "保持寄存器",
        _ => "",
    };
}

/// <summary>写入下拉条目（包一个可写点位）。</summary>
public class WriteItem
{
    public RegPoint Point { get; }
    public WriteItem(RegPoint p) => Point = p;
    public string WriteLabel => $"{Point.Device} · {Point.Name}  ({(Point.Area == Area.Coil ? "线圈" : "保持寄存器")} @{Point.Address})";
}
