// ============================================================================
//  DeviceControlVm —— 单台设备的"控制卡" ViewModel（工业 HMI 风格）
//  把"写寄存器"封装成直观控件：启停开关、设定值滑块/输入框、动作按钮(升柱/合闸…)。
//  读数直接复用主监测的 PointVm 实例(轮询会自动刷新)；写操作走 MainViewModel 的
//  CtrlCoil/CtrlReg/CtrlMulti(经 SlaveSim 连接下发)。
// ============================================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HmiApp.ViewModels;

public partial class DeviceControlVm : ObservableObject
{
    private readonly MainViewModel _root;
    public DeviceControlVm(MainViewModel root) => _root = root;

    public string Device { get; init; } = "";

    // 运行指示灯（绑定某个状态点位；为空则不显示灯）
    public PointVm? RunPoint { get; init; }
    public bool ShowLamp => RunPoint != null;

    // 关键读数（复用主监测的 PointVm，自动刷新）
    public IReadOnlyList<PointVm> Readings { get; init; } = Array.Empty<PointVm>();

    // 启停开关
    public bool HasStartStop { get; init; }
    public ushort StartCoil { get; init; }
    public string StartLabel { get; init; } = "启动";
    public string StopLabel { get; init; } = "停止";

    // 设定值（滑块 + 输入框 + 下发）
    public bool HasSetpoint { get; init; }
    public string SetpointLabel { get; init; } = "";
    public double SpMin { get; init; }
    public double SpMax { get; init; } = 100;
    public ushort SpAddr { get; init; }
    public double SpScale { get; init; } = 1.0;
    public bool SpMulti { get; init; }          // true=FC16 连续多寄存器(如支架群)
    [ObservableProperty] private double _setpoint;

    // 额外动作按钮（升柱/降柱/移架 等）
    public IReadOnlyList<DeviceActionVm> Actions { get; init; } = Array.Empty<DeviceActionVm>();
    public bool HasActions => Actions.Count > 0;

    [RelayCommand] private void Start() => _root.CtrlCoil(StartCoil, true);
    [RelayCommand] private void Stop() => _root.CtrlCoil(StartCoil, false);

    [RelayCommand]
    private void Send()
    {
        ushort raw = (ushort)Math.Max(0, Math.Round(Setpoint / SpScale));
        if (SpMulti) _root.CtrlMulti(SpAddr, Enumerable.Repeat(raw, (int)Shared.RegisterMap.SupportGroupCount).ToArray());
        else _root.CtrlReg(SpAddr, raw);
    }
}

/// <summary>一个动作按钮（标签 + 命令）。</summary>
public class DeviceActionVm
{
    public string Label { get; }
    public IRelayCommand Command { get; }
    public DeviceActionVm(string label, Action exec) { Label = label; Command = new RelayCommand(exec); }
}
