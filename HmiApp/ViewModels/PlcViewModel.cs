// ============================================================================
//  PlcViewModel —— 「PLC 控制」tab 的 ViewModel（对应题目三）
//  独立连接 OpenPLC 的 Modbus TCP 服务端(默认 127.0.0.1:502)，与监控大屏的
//  SlaveSim 连接互不影响——体现上位机同时对接"设备模拟从站"和"真 PLC"。
//
//  演示的是"控制"而非"改寄存器"：上位机只下指令，真正的启停由 PLC 梯形图按
//  联锁逻辑决定——低液位/急停时 PLC 会否决上位机的启泵请求。
//
//  与 OpenPLC 的地址约定（见 OpenPLC 接入指南；master 线圈地址 ↔ %QX 字.位）：
//    线圈0 %QX0.0  启泵请求   (上位机写, 梯形图读)
//    线圈2 %QX0.2  急停       (上位机写, 梯形图读)
//    保持寄存器0 %QW0 液位(%) (上位机写, 梯形图读)
//    线圈4 %QX0.4  泵运行     (梯形图写, 上位机读)
//    线圈6 %QX0.6  低液位     (梯形图写, 上位机读)
// ============================================================================

using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared;

namespace HmiApp.ViewModels;

public partial class PlcViewModel : ObservableObject
{
    private const byte SlaveId = 1;
    private ModbusTcpMaster? _plc;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(500) };

    [ObservableProperty] private string _plcIp = "127.0.0.1";
    [ObservableProperty] private string _plcPort = "502";
    [ObservableProperty] private bool _plcConnected;
    [ObservableProperty] private string _plcStatus = "未连接";
    [ObservableProperty] private string _plcConnectText = "连接 PLC";
    [ObservableProperty] private string _levelValue = "85";

    // 状态（读自 PLC 输出线圈）
    [ObservableProperty] private bool _startReq;     // 线圈0 启泵请求
    [ObservableProperty] private bool _eStop;        // 线圈2 急停
    [ObservableProperty] private bool _pumpRunning;  // 线圈4 泵运行(PLC 计算结果)
    [ObservableProperty] private bool _lowLevel;     // 线圈6 低液位(PLC 计算结果)

    public PlcViewModel() => _poll.Tick += OnPoll;

    [RelayCommand]
    private void ConnectPlc()
    {
        if (_plc != null) { DisconnectPlc(); return; }
        try
        {
            _plc = new ModbusTcpMaster(PlcIp.Trim(), int.Parse(PlcPort.Trim()), SlaveId);
            _poll.Start();
            PlcConnected = true;
            PlcConnectText = "断开 PLC";
            PlcStatus = "已连接";
        }
        catch (Exception ex) { PlcStatus = "连接失败：" + ex.Message; DisconnectPlc(); }
    }

    private void DisconnectPlc()
    {
        _poll.Stop();
        _plc?.Dispose();
        _plc = null;
        PlcConnected = false;
        PlcConnectText = "连接 PLC";
        if (PlcStatus.StartsWith("已连接")) PlcStatus = "未连接";
    }

    private void OnPoll(object? s, EventArgs e)
    {
        if (_plc == null) return;
        try
        {
            bool[] c = _plc.ReadCoils(0, 7);     // 线圈 0..6
            StartReq = c[0];
            EStop = c[2];
            PumpRunning = c[4];
            LowLevel = c[6];
            PlcStatus = $"已连接 · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { PlcStatus = "读取异常：" + ex.Message; DisconnectPlc(); }
    }

    [RelayCommand] private void StartPump() => Do(() => _plc!.WriteSingleCoil(0, true), "下发启泵请求");
    [RelayCommand] private void StopPump() => Do(() => _plc!.WriteSingleCoil(0, false), "撤销启泵请求");
    [RelayCommand] private void TriggerEstop() => Do(() => _plc!.WriteSingleCoil(2, true), "触发急停");
    [RelayCommand] private void ClearEstop() => Do(() => _plc!.WriteSingleCoil(2, false), "解除急停");

    [RelayCommand]
    private void SetLevel()
    {
        if (!ushort.TryParse(LevelValue.Trim(), out ushort v)) { PlcStatus = "液位值非法(0~100)"; return; }
        Do(() => _plc!.WriteSingleRegister(0, v), $"写液位={v}%");
    }

    private void Do(Action a, string label)
    {
        if (_plc == null) { PlcStatus = "未连接"; return; }
        try { a(); PlcStatus = "已" + label; }
        catch (Exception ex) { PlcStatus = label + "失败：" + ex.Message; }
    }
}
