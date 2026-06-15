// ============================================================================
//  PlcViewModel —— 「PLC 控制」tab 的 ViewModel（对应题目三）
//  独立连接 OpenPLC 的 Modbus TCP 服务端(默认 127.0.0.1:502)，与监控大屏的
//  SlaveSim 连接互不影响——体现上位机同时对接"设备模拟从站"和"真 PLC"。
//
//  演示的是"控制"而非"改寄存器"：上位机只下指令，真正的启停由 PLC 梯形图按
//  联锁逻辑决定——低液位/急停时 PLC 会否决上位机的启泵请求。
//
//  连 OpenPLC v4 的 Modbus 服务端（默认 127.0.0.1:5020；v4 modbus_slave 默认端口是
//  5020 不是 502！）。地址与 OpenPLC 程序的 %QX/%QW 对应（见接入指南 §2）：
//    线圈0 %QX0.0 启泵请求(上位机写)   线圈2 %QX0.2 急停(写)   保持寄存器0 %QW0 液位(写)
//    线圈4 %QX0.4 泵运行(PLC算,读)     线圈6 %QX0.6 低液位(PLC算,读)
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

    // 与 OpenPLC 程序的 %QX/%QW 对应的地址
    private const ushort CoilStart = 0, CoilEstop = 2, CoilPump = 4, CoilLow = 6, HrLevel = 0;

    private readonly MainViewModel _root;   // 用于把 PLC 报文汇入右侧共享报文栏

    [ObservableProperty] private string _plcIp = "127.0.0.1";
    [ObservableProperty] private string _plcPort = "5020";   // OpenPLC v4 modbus_slave 默认端口=5020(非502)
    [ObservableProperty] private bool _plcConnected;
    [ObservableProperty] private string _plcStatus = "未连接";
    [ObservableProperty] private string _plcConnectText = "连接 PLC";
    [ObservableProperty] private string _levelValue = "85";

    // 状态（读自 PLC 输出线圈）
    [ObservableProperty] private bool _startReq;     // 线圈0 启泵请求
    [ObservableProperty] private bool _eStop;        // 线圈2 急停
    [ObservableProperty] private bool _pumpRunning;  // 线圈4 泵运行(PLC 计算结果)
    [ObservableProperty] private bool _lowLevel;     // 线圈6 低液位(PLC 计算结果)

    public PlcViewModel(MainViewModel root)
    {
        _root = root;
        _poll.Tick += OnPoll;
    }

    [RelayCommand]
    private void ConnectPlc()
    {
        if (_plc != null) { DisconnectPlc(); return; }
        try
        {
            _plc = new ModbusTcpMaster(PlcIp.Trim(), int.Parse(PlcPort.Trim()), SlaveId, "PLC");
            _plc.FrameLogged += _root.LogFrame;     // PLC 报文汇入右侧共享报文栏（带 [PLC] 前缀）
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
        if (_plc != null) { _plc.FrameLogged -= _root.LogFrame; _plc.Dispose(); _plc = null; }
        PlcConnected = false;
        PlcConnectText = "连接 PLC";
        if (PlcStatus.StartsWith("已连接")) PlcStatus = "未连接";
    }

    private void OnPoll(object? s, EventArgs e)
    {
        if (_plc == null) return;
        try
        {
            bool[] c = _plc.ReadCoils(CoilStart, 7);   // 线圈 100..106
            StartReq = c[CoilStart - CoilStart];        // 100
            EStop = c[CoilEstop - CoilStart];           // 102
            PumpRunning = c[CoilPump - CoilStart];      // 104
            LowLevel = c[CoilLow - CoilStart];          // 106
            PlcStatus = $"已连接 · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { PlcStatus = "读取异常：" + ex.Message; DisconnectPlc(); }
    }

    [RelayCommand] private void StartPump() => Do(() => _plc!.WriteSingleCoil(CoilStart, true), "下发启泵请求");
    [RelayCommand] private void StopPump() => Do(() => _plc!.WriteSingleCoil(CoilStart, false), "撤销启泵请求");
    [RelayCommand] private void TriggerEstop() => Do(() => _plc!.WriteSingleCoil(CoilEstop, true), "触发急停");
    [RelayCommand] private void ClearEstop() => Do(() => _plc!.WriteSingleCoil(CoilEstop, false), "解除急停");

    [RelayCommand]
    private void SetLevel()
    {
        if (!ushort.TryParse(LevelValue.Trim(), out ushort v)) { PlcStatus = "液位值非法(0~100)"; return; }
        Do(() => _plc!.WriteSingleRegister(HrLevel, v), $"写液位={v}%");
    }

    private void Do(Action a, string label)
    {
        if (_plc == null) { PlcStatus = "未连接"; return; }
        try { a(); PlcStatus = "已" + label; }
        catch (Exception ex) { PlcStatus = label + "失败：" + ex.Message; }
    }
}
