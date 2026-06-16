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

using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared;

namespace HmiApp.ViewModels;

public partial class PlcViewModel : ObservableObject
{
    private const byte SlaveId = 1;
    private ModbusTcpMaster? _plc;
    // 后台轮询：与监控大屏同一套异步模式（PeriodicTimer + 后台 Task + Dispatcher 回填）。
    private CancellationTokenSource? _plcCts;
    private Task? _plcTask;
    private readonly Dispatcher _dispatcher;

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
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    [RelayCommand]
    private async Task ConnectPlc()
    {
        if (PlcConnected) { await DisconnectPlcAsync(); return; }
        await DisconnectPlcAsync();   // 清理上次残留（读异常被动退出后的连接/任务）
        try
        {
            _plc = await ModbusTcpMaster.ConnectAsync(PlcIp.Trim(), int.Parse(PlcPort.Trim()), SlaveId, "PLC");
            _plc.FrameLogged += _root.LogFrame;     // PLC 报文汇入右侧共享报文栏（带 [PLC] 前缀）
            _plcCts = new CancellationTokenSource();
            _plcTask = Task.Run(() => PlcLoopAsync(_plcCts.Token));
            PlcConnected = true;
            PlcConnectText = "断开 PLC";
            PlcStatus = "已连接";
        }
        catch (Exception ex) { PlcStatus = "连接失败：" + ex.Message; await DisconnectPlcAsync(); }
    }

    private async Task DisconnectPlcAsync()
    {
        if (_plcCts != null)
        {
            _plcCts.Cancel();
            if (_plcTask != null) { try { await _plcTask; } catch { /* 取消属正常 */ } }
            _plcTask = null;
            _plcCts.Dispose();
            _plcCts = null;
        }
        if (_plc != null) { _plc.FrameLogged -= _root.LogFrame; _plc.Dispose(); _plc = null; }
        PlcConnected = false;
        PlcConnectText = "连接 PLC";
        if (PlcStatus.StartsWith("已连接")) PlcStatus = "未连接";
    }

    private async Task PlcLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var plc = _plc;
                if (plc == null) break;
                try
                {
                    bool[] c = await plc.ReadCoilsAsync(CoilStart, 7, ct).ConfigureAwait(false);   // 线圈 0..6
                    await _dispatcher.InvokeAsync(() =>
                    {
                        StartReq = c[CoilStart - CoilStart];   // 0
                        EStop = c[CoilEstop - CoilStart];      // 2
                        PumpRunning = c[CoilPump - CoilStart]; // 4
                        LowLevel = c[CoilLow - CoilStart];     // 6
                        PlcStatus = $"已连接 · {DateTime.Now:HH:mm:ss}";
                        _root.DrainFrameQueue();   // PLC 独立运行时也负责把共享报文队列倒进报文栏
                    });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // 读异常：在 UI 线程置状态并就地释放连接（不 await 自身 Task，避免死锁），然后退出循环
                    await _dispatcher.InvokeAsync(() =>
                    {
                        PlcStatus = "读取异常：" + ex.Message;
                        if (_plc != null) { _plc.FrameLogged -= _root.LogFrame; _plc.Dispose(); _plc = null; }
                        PlcConnected = false;
                        PlcConnectText = "连接 PLC";
                        _plcCts?.Cancel();
                    });
                    break;
                }
            }
        }
        catch (OperationCanceledException) { /* 断开/关闭，正常退出 */ }
    }

    [RelayCommand] private Task StartPump() => Do(ct => _plc!.WriteSingleCoilAsync(CoilStart, true, ct), "下发启泵请求");
    [RelayCommand] private Task StopPump() => Do(ct => _plc!.WriteSingleCoilAsync(CoilStart, false, ct), "撤销启泵请求");
    [RelayCommand] private Task TriggerEstop() => Do(ct => _plc!.WriteSingleCoilAsync(CoilEstop, true, ct), "触发急停");
    [RelayCommand] private Task ClearEstop() => Do(ct => _plc!.WriteSingleCoilAsync(CoilEstop, false, ct), "解除急停");

    [RelayCommand]
    private async Task SetLevel()
    {
        if (!ushort.TryParse(LevelValue.Trim(), out ushort v)) { PlcStatus = "液位值非法(0~100)"; return; }
        await Do(ct => _plc!.WriteSingleRegisterAsync(HrLevel, v, ct), $"写液位={v}%");
    }

    private async Task Do(Func<CancellationToken, Task> a, string label)
    {
        if (_plc == null) { PlcStatus = "未连接"; return; }
        try { await a(CancellationToken.None); PlcStatus = "已" + label; }
        catch (Exception ex) { PlcStatus = label + "失败：" + ex.Message; }
    }

    /// <summary>关闭时取消后台轮询（best-effort，不等待）。</summary>
    public void Shutdown() => _plcCts?.Cancel();
}
