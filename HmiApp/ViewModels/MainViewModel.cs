// ============================================================================
//  MainViewModel —— 整个上位机的 ViewModel（MVVM 核心）
//  负责：连接/断开、800ms 轮询、把读到的值回填各 PointVm(绑定自动刷新界面)、
//        收发报文集合、趋势/柱状图数据(LiveCharts)、写入与周期调度命令。
//  复用：Shared.ModbusTcpMaster(手写主站) + HmiApp.WriteScheduler。
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Shared;

namespace HmiApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private ModbusTcpMaster? _master;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly List<PointVm> _all = new();
    private readonly List<PointVm> _supportPts;

    [ObservableProperty] private string _ip = "127.0.0.1";
    [ObservableProperty] private string _port = "1502";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _connectText = "连接";

    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private int _alarmCount;
    [ObservableProperty] private int _deviceCount;

    // 分类集合（各 tab）
    public ObservableCollection<PointVm> ThreeMachine { get; } = new();
    public ObservableCollection<PointVm> Transport { get; } = new();
    public ObservableCollection<PointVm> Fluid { get; } = new();
    public ObservableCollection<PointVm> Power { get; } = new();
    public ObservableCollection<PointVm> Alarms { get; } = new();

    // 总览头条点位
    public PointVm? ShearerCurrent { get; private set; }
    public PointVm? EmulPressure { get; private set; }
    public PointVm? SubVoltage { get; private set; }
    public PointVm? BeltSpeed { get; private set; }

    // 报文
    public ObservableCollection<FrameLog> Frames { get; } = new();
    private const int FrameCap = 300;
    [ObservableProperty] private bool _logPolling = true;
    [ObservableProperty] private bool _framePaused;
    [ObservableProperty] private FrameLog? _selectedFrame;
    public string SelectedFrameDetail => SelectedFrame is null ? "" : $"完整帧 ({SelectedFrame.DirText}):\r\n{SelectedFrame.Hex}\r\n\r\n{SelectedFrame.Detail}";
    partial void OnSelectedFrameChanged(FrameLog? value) => OnPropertyChanged(nameof(SelectedFrameDetail));

    // 趋势（LiveCharts 折线）
    public ObservableCollection<PointVm> AnalogPoints { get; } = new();
    [ObservableProperty] private PointVm? _trendPoint;
    private readonly ObservableCollection<double> _trendValues = new();
    public ISeries[] TrendSeries { get; }
    public Axis[] TrendXAxes { get; }
    public Axis[] TrendYAxes { get; }
    partial void OnTrendPointChanged(PointVm? value) => _trendValues.Clear();

    // 柱状（组合开关回路电流 / 支架群压力）
    private readonly ObservableCollection<double> _loopCurrents = new() { 0, 0, 0, 0 };
    public ISeries[] LoopSeries { get; }
    public Axis[] LoopXAxes { get; }
    private readonly ObservableCollection<double> _supportPressures = new();
    public ISeries[] SupportSeries { get; }
    public Axis[] SupportXAxes { get; }

    // 写入
    public ObservableCollection<WriteItem> WritablePoints { get; } = new();
    [ObservableProperty] private WriteItem? _writeTarget;
    [ObservableProperty] private string _writeValue = "";
    [ObservableProperty] private string _writeBit = "";
    [ObservableProperty] private string _writeCount = "1";
    [ObservableProperty] private string _writePeriod = "1000";
    [ObservableProperty] private string _writeHint = "";
    public WriteScheduler Scheduler { get; } = new();

    // PLC 控制 tab 的子 ViewModel（独立连接 OpenPLC）
    public PlcViewModel Plc { get; } = new();

    // 设备控制卡（工业 HMI 风格：开关/滑块/动作按钮）
    public ObservableCollection<DeviceControlVm> Controls { get; } = new();
    [ObservableProperty] private string _ctrlHint = "";

    public MainViewModel()
    {
        foreach (var p in RegisterMap.Points)
        {
            var vm = new PointVm(p);
            _all.Add(vm);
            switch (p.Category)
            {
                case Category.ThreeMachine: ThreeMachine.Add(vm); break;
                case Category.Transport: Transport.Add(vm); break;
                case Category.Fluid: Fluid.Add(vm); break;
                case Category.Power: Power.Add(vm); break;
            }
            if (!p.IsBit) AnalogPoints.Add(vm);
            if (p.Writable) WritablePoints.Add(new WriteItem(p));
        }
        DeviceCount = _all.Select(v => v.Device).Distinct().Count();
        ShearerCurrent = Find("采煤机", "左截割电机电流");
        EmulPressure = Find("乳化泵站", "出口压力");
        SubVoltage = Find("移变", "进线电压");
        BeltSpeed = Find("皮带输送机", "带速");
        TrendPoint = EmulPressure ?? AnalogPoints.FirstOrDefault();
        WriteTarget = WritablePoints.FirstOrDefault();

        _supportPts = _all.Where(v => v.Device == "液压支架群").OrderBy(v => v.Point.Address).ToList();
        foreach (var _ in _supportPts) _supportPressures.Add(0);

        // LiveCharts 配色
        var cyan = new SolidColorPaint(SKColor.Parse("#2A9FD6")) { StrokeThickness = 2 };
        var subPaint = new SolidColorPaint(SKColor.Parse("#8FA6C4"));
        var gridPaint = new SolidColorPaint(SKColor.Parse("#244468")) { StrokeThickness = 1 };

        TrendSeries = new ISeries[] { new LineSeries<double> { Values = _trendValues, Stroke = cyan, Fill = null, GeometrySize = 0, LineSmoothness = 0.3 } };
        TrendXAxes = new[] { new Axis { IsVisible = false } };
        TrendYAxes = new[] { new Axis { LabelsPaint = subPaint, SeparatorsPaint = gridPaint } };

        LoopSeries = new ISeries[] { new ColumnSeries<double> { Values = _loopCurrents, Fill = new SolidColorPaint(SKColor.Parse("#2A9FD6")) } };
        LoopXAxes = new[] { new Axis { Labels = new[] { "回路1", "回路2", "回路3", "回路4" }, LabelsPaint = subPaint } };

        SupportSeries = new ISeries[] { new ColumnSeries<double> { Values = _supportPressures, Fill = new SolidColorPaint(SKColor.Parse("#1D4E89")) } };
        SupportXAxes = new[] { new Axis { Labels = Enumerable.Range(1, _supportPts.Count).Select(i => $"{i}#").ToArray(), LabelsPaint = subPaint } };

        _poll.Tick += OnPoll;
        BuildControls();
    }

    private PointVm? Find(string d, string n) => _all.FirstOrDefault(v => v.Device == d && v.Name == n);

    // ===================== 设备控制卡 =====================
    public void CtrlCoil(ushort a, bool on) => CtrlDo(() => _master!.WriteSingleCoil(a, on), $"线圈{a}={(on ? "ON" : "OFF")}");
    public void CtrlReg(ushort a, ushort v) => CtrlDo(() => _master!.WriteSingleRegister(a, v), $"寄存器{a}={v}");
    public void CtrlMulti(ushort a, ushort[] v) => CtrlDo(() => _master!.WriteMultipleRegisters(a, v), $"多寄存器@{a}×{v.Length}");

    private void CtrlDo(Action act, string label)
    {
        if (_master == null) { CtrlHint = "未连接（请先在顶部连接 SlaveSim）"; return; }
        try { act(); CtrlHint = "已下发：" + label; }
        catch (Exception ex) { CtrlHint = "下发失败：" + ex.Message; }
    }

    private void BuildControls()
    {
        List<PointVm> Rd(params (string d, string n)[] xs)
            => xs.Select(x => Find(x.d, x.n)).Where(p => p != null).Cast<PointVm>().ToList();

        Controls.Add(new DeviceControlVm(this) { Device = "采煤机", RunPoint = Find("采煤机", "运行中"),
            Readings = Rd(("采煤机", "左截割电机电流"), ("采煤机", "牵引速度"), ("采煤机", "牵引电机温度")),
            HasStartStop = true, StartCoil = 0, HasSetpoint = true, SetpointLabel = "牵引速度设定 (m/min)", SpMin = 0, SpMax = 12, SpAddr = 0, SpScale = 0.1, Setpoint = 6 });

        Controls.Add(new DeviceControlVm(this) { Device = "刮板输送机", RunPoint = Find("刮板输送机", "运行中"),
            Readings = Rd(("刮板输送机", "电机电流"), ("刮板输送机", "链速"), ("刮板输送机", "链张力")),
            HasStartStop = true, StartCoil = 20, HasSetpoint = true, SetpointLabel = "链速设定 (m/s)", SpMin = 0, SpMax = 2.5, SpAddr = 20, SpScale = 0.01, Setpoint = 1.2 });

        Controls.Add(new DeviceControlVm(this) { Device = "转载机", RunPoint = Find("转载机", "运行中"),
            Readings = Rd(("转载机", "电机电流"), ("转载机", "电机温度")), HasStartStop = true, StartCoil = 30 });

        Controls.Add(new DeviceControlVm(this) { Device = "破碎机", RunPoint = Find("破碎机", "运行中"),
            Readings = Rd(("破碎机", "电机电流"), ("破碎机", "振动")), HasStartStop = true, StartCoil = 40 });

        Controls.Add(new DeviceControlVm(this) { Device = "皮带输送机", RunPoint = Find("皮带输送机", "运行中"),
            Readings = Rd(("皮带输送机", "带速"), ("皮带输送机", "张力")), HasStartStop = true, StartCoil = 50 });

        Controls.Add(new DeviceControlVm(this) { Device = "乳化泵站", RunPoint = Find("乳化泵站", "运行中"),
            Readings = Rd(("乳化泵站", "出口压力"), ("乳化泵站", "液箱液位")),
            HasStartStop = true, StartCoil = 60, HasSetpoint = true, SetpointLabel = "出口压力设定 (MPa)", SpMin = 0, SpMax = 40, SpAddr = 60, SpScale = 0.1, Setpoint = 32 });

        Controls.Add(new DeviceControlVm(this) { Device = "喷雾泵站", RunPoint = Find("喷雾泵站", "运行中"),
            Readings = Rd(("喷雾泵站", "出口压力"), ("喷雾泵站", "流量")),
            HasStartStop = true, StartCoil = 70, HasSetpoint = true, SetpointLabel = "出口压力设定 (MPa)", SpMin = 0, SpMax = 16, SpAddr = 70, SpScale = 0.1, Setpoint = 8 });

        Controls.Add(new DeviceControlVm(this) { Device = "液压支架", RunPoint = Find("液压支架", "护帮板伸出"),
            Readings = Rd(("液压支架", "前柱压力"), ("液压支架", "后柱压力"), ("液压支架", "推移行程")),
            Actions = new[] {
                new DeviceActionVm("升柱", () => CtrlCoil(10, true)),
                new DeviceActionVm("降柱", () => CtrlCoil(10, false)),
                new DeviceActionVm("移架", () => CtrlCoil(12, true)),
            } });

        Controls.Add(new DeviceControlVm(this) { Device = "液压支架群·压力设定",
            Readings = Rd(("液压支架群", "1#立柱压力设定"), ("液压支架群", "8#立柱压力设定")),
            HasSetpoint = true, SetpointLabel = "全部立柱压力设定 (MPa·FC16群写)", SpMin = 0, SpMax = 40, SpAddr = RegisterMap.SupportGroupStart, SpScale = 0.1, SpMulti = true, Setpoint = 30 });

        for (int i = 0; i < 4; i++)
        {
            ushort cc = (ushort)(90 + i);
            Controls.Add(new DeviceControlVm(this) { Device = $"组合开关·回路{i + 1}", RunPoint = Find("组合开关", $"回路{i + 1}通断"),
                Readings = Rd(("组合开关", $"回路{i + 1}电流")), HasStartStop = true, StartCoil = cc, StartLabel = "合闸", StopLabel = "分闸" });
        }
    }

    // ===================== 连接 =====================
    [RelayCommand]
    private void Connect()
    {
        if (_master != null) { Disconnect(); return; }
        try
        {
            _master = new ModbusTcpMaster(Ip.Trim(), int.Parse(Port.Trim()), RegisterMap.SlaveId);
            _master.FrameLogged += OnFrameLogged;
            _poll.Start();
            IsConnected = true;
            ConnectText = "断开";
            StatusText = "已连接";
        }
        catch (Exception ex) { StatusText = "连接失败：" + ex.Message; Disconnect(); }
    }

    private void Disconnect()
    {
        _poll.Stop();
        if (_master != null) { _master.FrameLogged -= OnFrameLogged; _master.Dispose(); _master = null; }
        IsConnected = false;
        ConnectText = "连接";
        if (StatusText.StartsWith("已连接")) StatusText = "未连接";
    }

    // ===================== 轮询 =====================
    private void OnPoll(object? s, EventArgs e)
    {
        if (_master == null) return;
        try
        {
            UpdateRegs(Area.InputRegister, (a, c) => _master!.ReadInputRegisters(a, c));
            UpdateRegs(Area.HoldingRegister, (a, c) => _master!.ReadHoldingRegisters(a, c));
            UpdateBits(Area.DiscreteInput, (a, c) => _master!.ReadDiscreteInputs(a, c));
            UpdateBits(Area.Coil, (a, c) => _master!.ReadCoils(a, c));

            SyncAlarms();
            RunningCount = _all.Count(v => v.IsOn && v.Name.Contains("运行"));
            AlarmCount = Alarms.Count;

            if (TrendPoint != null) { _trendValues.Add(TrendPoint.Numeric); while (_trendValues.Count > 120) _trendValues.RemoveAt(0); }
            for (int i = 0; i < 4; i++) _loopCurrents[i] = Find("组合开关", $"回路{i + 1}电流")?.Numeric ?? 0;
            for (int i = 0; i < _supportPts.Count; i++) _supportPressures[i] = _supportPts[i].Numeric;

            StatusText = $"已连接 · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { StatusText = "读取异常：" + ex.Message; Disconnect(); }
    }

    // 按数据区批量读；保持寄存器跨度大(0..207>125上限)，按 120 一段分块读
    private void UpdateRegs(Area area, Func<ushort, ushort, ushort[]> read)
    {
        var pts = _all.Where(v => v.Point.Area == area).ToList();
        if (pts.Count == 0) return;
        ushort min = pts.Min(v => v.Point.Address), max = pts.Max(v => v.Point.Address);
        for (int start = min; start <= max; start += 120)
        {
            ushort cnt = (ushort)Math.Min(120, max - start + 1);
            ushort[] data = read((ushort)start, cnt);
            foreach (var v in pts.Where(v => v.Point.Address >= start && v.Point.Address < start + cnt))
                v.SetReg(data[v.Point.Address - start]);
        }
    }

    private void UpdateBits(Area area, Func<ushort, ushort, bool[]> read)
    {
        var pts = _all.Where(v => v.Point.Area == area).ToList();
        if (pts.Count == 0) return;
        ushort min = pts.Min(v => v.Point.Address), max = pts.Max(v => v.Point.Address);
        bool[] data = read(min, (ushort)(max - min + 1));
        foreach (var v in pts) v.SetBit(data[v.Point.Address - min]);
    }

    private void SyncAlarms()
    {
        var cur = _all.Where(v => v.IsAlarm).ToList();
        for (int i = Alarms.Count - 1; i >= 0; i--) if (!cur.Contains(Alarms[i])) Alarms.RemoveAt(i);
        foreach (var a in cur) if (!Alarms.Contains(a)) Alarms.Add(a);
    }

    // ===================== 报文 =====================
    private void OnFrameLogged(FrameLog f)
    {
        if (FramePaused) return;
        if (!f.IsWrite && !LogPolling) return;
        Frames.Insert(0, f);
        while (Frames.Count > FrameCap) Frames.RemoveAt(Frames.Count - 1);
    }

    [RelayCommand] private void ClearFrames() => Frames.Clear();

    // ===================== 写入 / 调度 =====================
    [RelayCommand]
    private void WriteOnce()
    {
        var b = BuildWrite();
        if (b == null) return;
        try { b.Value.action(); WriteHint = "已写入：" + b.Value.desc; }
        catch (Exception ex) { WriteHint = "写入失败：" + ex.Message; }
    }

    [RelayCommand]
    private void AddTask()
    {
        var b = BuildWrite();
        if (b == null) return;
        Scheduler.Add(b.Value.desc, ParseInt(WriteCount, 1), ParseInt(WritePeriod, 1000), b.Value.action);
        WriteHint = "已加入周期任务：" + b.Value.desc;
    }

    [RelayCommand] private void StopAll() => Scheduler.StopAll();
    [RelayCommand] private void StopTask(WriteTaskVm t) => Scheduler.Stop(t);

    // 一键演示多寄存器写(FC16)：把支架群 1#~8# 压力设定一次写完
    [RelayCommand]
    private void WriteSupportGroup()
    {
        if (_master == null) { WriteHint = "未连接"; return; }
        ushort val = ushort.TryParse(WriteValue.Trim(), out var v) ? v : (ushort)320;
        var vals = Enumerable.Repeat(val, RegisterMap.SupportGroupCount).ToArray();
        try { _master.WriteMultipleRegisters(RegisterMap.SupportGroupStart, vals); WriteHint = $"FC16 群写支架群压力设定 ×{vals.Length} = {val}"; }
        catch (Exception ex) { WriteHint = "群写失败：" + ex.Message; }
    }

    private (string desc, Action action)? BuildWrite()
    {
        if (_master == null) { WriteHint = "未连接"; return null; }
        if (WriteTarget is null) { WriteHint = "未选点位"; return null; }
        var p = WriteTarget.Point;
        string valText = WriteValue.Trim(), bitText = WriteBit.Trim();
        ushort addr = p.Address;

        if (p.Area == Area.Coil)
        {
            bool on = valText is "1" or "true" or "ON" or "on";
            return ($"写线圈 @{addr} = {(on ? "ON" : "OFF")}", () => _master!.WriteSingleCoil(addr, on));
        }

        if (!string.IsNullOrEmpty(bitText))   // 按位写：read-modify-write
        {
            if (!int.TryParse(bitText, out int bit) || bit < 0 || bit > 15) { WriteHint = "位应为 0~15"; return null; }
            bool set = valText is "1" or "true";
            return ($"按位写 @{addr}.bit{bit} = {(set ? 1 : 0)}", () =>
            {
                ushort cur = _master!.ReadHoldingRegisters(addr, 1)[0];
                ushort nw = set ? (ushort)(cur | (1 << bit)) : (ushort)(cur & ~(1 << bit));
                _master!.WriteSingleRegister(addr, nw);
            });
        }

        if (valText.Contains(','))            // 多字节：写多寄存器 FC16
        {
            var parts = valText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var vals = new ushort[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!ushort.TryParse(parts[i], out vals[i])) { WriteHint = "多值非法"; return null; }
            return ($"写多寄存器 @{addr} ×{vals.Length}", () => _master!.WriteMultipleRegisters(addr, vals));
        }

        if (!ushort.TryParse(valText, out ushort vv)) { WriteHint = "值应为 0~65535 整数"; return null; }
        return ($"写寄存器 @{addr} = {vv}", () => _master!.WriteSingleRegister(addr, vv));
    }

    private static int ParseInt(string s, int dft) => int.TryParse(s.Trim(), out int v) ? v : dft;
}
