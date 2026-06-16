// ============================================================================
//  MainViewModel —— 整个上位机的 ViewModel（MVVM 核心）
//  多从站分区版：为每个区(综采三机/运输/供液/供电)各维护一条 Modbus 连接，
//  分区轮询、分区读写——模拟井下"一个上位机(主站)管多个控制器(从站)"的拓扑。
//  复用：Shared.ModbusTcpMaster(手写主站) + HmiApp.WriteScheduler。
// ============================================================================

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
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
    // 每个分区一条连接（Category → 该区从站的 Modbus 主站连接）
    private readonly Dictionary<Category, ModbusTcpMaster> _zone = new();
    private readonly List<PointVm> _all = new();
    private readonly List<PointVm> _supportPts;

    // 后台采集：用 PeriodicTimer 在后台 Task 里跑，IO 不再占用 UI 线程。
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private readonly Dispatcher _dispatcher;   // 把后台读到的数据封送回 UI 线程更新绑定
    // 报文在后台线程产生，先入并发队列，等 UI 线程那一拍统一 drain 进 Frames（避免集合跨线程改）。
    private readonly ConcurrentQueue<FrameLog> _frameQ = new();

    [ObservableProperty] private string _ip = "127.0.0.1";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _connectText = "连接全部分区";

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

    // 趋势：多个可独立选点的趋势图槽位（总览放 3 个，用户各自选监控项）
    public ObservableCollection<PointVm> AnalogPoints { get; } = new();
    public IReadOnlyList<TrendSlotVm> Trends { get; }

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

    // PLC 控制 tab 的子 ViewModel（独立连接 OpenPLC；传入 root 以共享报文栏）
    public PlcViewModel Plc { get; }

    // 设备控制卡（工业 HMI 风格：开关/滑块/动作按钮）
    public ObservableCollection<DeviceControlVm> Controls { get; } = new();
    [ObservableProperty] private string _ctrlHint = "";

    // 设备功能说明（题目四：业务学习——纯知识性，不连 Modbus）
    public IReadOnlyList<DeviceInfoGroup> DeviceInfoGroups { get; } = DeviceInfoCatalog.Build();
    [RelayCommand] private void ExpandAllInfo() => SetAllInfo(true);
    [RelayCommand] private void CollapseAllInfo() => SetAllInfo(false);
    private void SetAllInfo(bool on)
    {
        foreach (var g in DeviceInfoGroups)
            foreach (var d in g.Devices) d.IsExpanded = on;
    }

    public MainViewModel()
    {
        // VM 由 XAML 在 UI 线程实例化，此处抓到的就是 UI Dispatcher。
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Plc = new PlcViewModel(this);
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
        WriteTarget = WritablePoints.FirstOrDefault();

        _supportPts = _all.Where(v => v.Device == "液压支架群").OrderBy(v => v.Point.Address).ToList();
        foreach (var _ in _supportPts) _supportPressures.Add(0);

        var subPaint = new SolidColorPaint(SKColor.Parse("#8FA6C4"));

        LoopSeries = new ISeries[] { new ColumnSeries<double> { Values = _loopCurrents, Fill = new SolidColorPaint(SKColor.Parse("#2A9FD6")) } };
        LoopXAxes = new[] { new Axis { Labels = new[] { "回路1", "回路2", "回路3", "回路4" }, LabelsPaint = subPaint } };

        SupportSeries = new ISeries[] { new ColumnSeries<double> { Values = _supportPressures, Fill = new SolidColorPaint(SKColor.Parse("#1D4E89")) } };
        SupportXAxes = new[] { new Axis { Labels = Enumerable.Range(1, _supportPts.Count).Select(i => $"{i}#").ToArray(), LabelsPaint = subPaint } };

        Trends = new[]
        {
            new TrendSlotVm(AnalogPoints, EmulPressure, "#2A9FD6"),
            new TrendSlotVm(AnalogPoints, ShearerCurrent, "#2ECC71"),
            new TrendSlotVm(AnalogPoints, SubVoltage, "#F39C12"),
        };

        BuildControls();
    }

    /// <summary>窗口关闭时调用：取消后台采集与调度，避免进程退出时后台 Task 泄漏/刷异常。</summary>
    public void Shutdown()
    {
        _pollCts?.Cancel();
        Scheduler.Shutdown();
        Plc.Shutdown();
    }

    private PointVm? Find(string d, string n) => _all.FirstOrDefault(v => v.Device == d && v.Name == n);

    // ===================== 设备控制卡（按分区路由到对应从站）=====================
    public Task CtrlCoil(Category z, ushort a, bool on) => CtrlDo(z, (m, ct) => m.WriteSingleCoilAsync(a, on, ct), $"线圈{a}={(on ? "ON" : "OFF")}");
    public Task CtrlReg(Category z, ushort a, ushort v) => CtrlDo(z, (m, ct) => m.WriteSingleRegisterAsync(a, v, ct), $"寄存器{a}={v}");
    public Task CtrlMulti(Category z, ushort a, ushort[] v) => CtrlDo(z, (m, ct) => m.WriteMultipleRegistersAsync(a, v, ct), $"多寄存器@{a}×{v.Length}");

    private async Task CtrlDo(Category z, Func<ModbusTcpMaster, CancellationToken, Task> act, string label)
    {
        if (!_zone.TryGetValue(z, out var m)) { CtrlHint = $"未连接「{RegisterMap.CategoryName(z)}」区"; return; }
        // 写与后台轮询读共用同一连接，经 master 内部 SemaphoreSlim 自动串行；await 期间不阻塞 UI。
        try { await act(m, CancellationToken.None); CtrlHint = "已下发：" + label; }
        catch (Exception ex) { CtrlHint = "下发失败：" + ex.Message; }
    }

    private void BuildControls()
    {
        List<PointVm> Rd(params (string d, string n)[] xs)
            => xs.Select(x => Find(x.d, x.n)).Where(p => p != null).Cast<PointVm>().ToList();

        var TM = Category.ThreeMachine; var TR = Category.Transport; var FL = Category.Fluid; var PW = Category.Power;

        Controls.Add(new DeviceControlVm(this) { Device = "采煤机", Zone = TM, RunPoint = Find("采煤机", "运行中"),
            Readings = Rd(("采煤机", "左截割电机电流"), ("采煤机", "牵引速度"), ("采煤机", "牵引电机温度")),
            HasStartStop = true, StartCoil = 0, HasSetpoint = true, SetpointLabel = "牵引速度设定 (m/min)", SpMin = 0, SpMax = 12, SpAddr = 0, SpScale = 0.1, Setpoint = 6 });

        Controls.Add(new DeviceControlVm(this) { Device = "刮板输送机", Zone = TM, RunPoint = Find("刮板输送机", "运行中"),
            Readings = Rd(("刮板输送机", "电机电流"), ("刮板输送机", "链速"), ("刮板输送机", "链张力")),
            HasStartStop = true, StartCoil = 20, HasSetpoint = true, SetpointLabel = "链速设定 (m/s)", SpMin = 0, SpMax = 2.5, SpAddr = 20, SpScale = 0.01, Setpoint = 1.2 });

        Controls.Add(new DeviceControlVm(this) { Device = "转载机", Zone = TR, RunPoint = Find("转载机", "运行中"),
            Readings = Rd(("转载机", "电机电流"), ("转载机", "电机温度")), HasStartStop = true, StartCoil = 30 });

        Controls.Add(new DeviceControlVm(this) { Device = "破碎机", Zone = TR, RunPoint = Find("破碎机", "运行中"),
            Readings = Rd(("破碎机", "电机电流"), ("破碎机", "振动")), HasStartStop = true, StartCoil = 40 });

        Controls.Add(new DeviceControlVm(this) { Device = "皮带输送机", Zone = TR, RunPoint = Find("皮带输送机", "运行中"),
            Readings = Rd(("皮带输送机", "带速"), ("皮带输送机", "张力")), HasStartStop = true, StartCoil = 50 });

        Controls.Add(new DeviceControlVm(this) { Device = "乳化泵站", Zone = FL, RunPoint = Find("乳化泵站", "运行中"),
            Readings = Rd(("乳化泵站", "出口压力"), ("乳化泵站", "液箱液位")),
            HasStartStop = true, StartCoil = 60, HasSetpoint = true, SetpointLabel = "出口压力设定 (MPa)", SpMin = 0, SpMax = 40, SpAddr = 60, SpScale = 0.1, Setpoint = 32 });

        Controls.Add(new DeviceControlVm(this) { Device = "喷雾泵站", Zone = FL, RunPoint = Find("喷雾泵站", "运行中"),
            Readings = Rd(("喷雾泵站", "出口压力"), ("喷雾泵站", "流量")),
            HasStartStop = true, StartCoil = 70, HasSetpoint = true, SetpointLabel = "出口压力设定 (MPa)", SpMin = 0, SpMax = 16, SpAddr = 70, SpScale = 0.1, Setpoint = 8 });

        Controls.Add(new DeviceControlVm(this) { Device = "液压支架", Zone = TM, RunPoint = Find("液压支架", "护帮板伸出"),
            Readings = Rd(("液压支架", "前柱压力"), ("液压支架", "后柱压力"), ("液压支架", "推移行程")),
            Actions = new[] {
                new DeviceActionVm("升柱", () => CtrlCoil(TM, 10, true)),
                new DeviceActionVm("降柱", () => CtrlCoil(TM, 10, false)),
                new DeviceActionVm("移架", () => CtrlCoil(TM, 12, true)),
            } });

        Controls.Add(new DeviceControlVm(this) { Device = "液压支架群·压力设定", Zone = TM,
            Readings = Rd(("液压支架群", "1#立柱压力设定"), ("液压支架群", "8#立柱压力设定")),
            HasSetpoint = true, SetpointLabel = "全部立柱压力设定 (MPa·FC16群写)", SpMin = 0, SpMax = 40, SpAddr = RegisterMap.SupportGroupStart, SpScale = 0.1, SpMulti = true, Setpoint = 30 });

        for (int i = 0; i < 4; i++)
        {
            ushort cc = (ushort)(90 + i);
            Controls.Add(new DeviceControlVm(this) { Device = $"组合开关·回路{i + 1}", Zone = PW, RunPoint = Find("组合开关", $"回路{i + 1}通断"),
                Readings = Rd(("组合开关", $"回路{i + 1}电流")), HasStartStop = true, StartCoil = cc, StartLabel = "合闸", StopLabel = "分闸" });
        }
    }

    // ===================== 连接（一键连全部分区）=====================
    [RelayCommand]
    private async Task Connect()
    {
        if (IsConnected) { await DisconnectAsync(); return; }
        await DisconnectAsync();   // 清理上次残留（如全掉线后被动取消的后台任务/连接）

        var fails = new List<string>();
        foreach (Category cat in Enum.GetValues<Category>())
        {
            try
            {
                var m = await ModbusTcpMaster.ConnectAsync(Ip.Trim(), RegisterMap.PortOf(cat), RegisterMap.SlaveId, RegisterMap.CategoryName(cat));
                m.FrameLogged += LogFrame;
                _zone[cat] = m;
            }
            catch (Exception ex) { fails.Add(RegisterMap.CategoryName(cat)); _ = ex; }
        }
        if (_zone.Count == 0) { StatusText = "全部分区连接失败（确认 SlaveSim 已启动）"; return; }
        IsConnected = true;
        ConnectText = "断开";
        // 启动后台采集循环：网络 IO 全部在后台 Task 上跑，UI 线程只负责回填。
        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token));
        StatusText = $"已连接 {_zone.Count}/4 区" + (fails.Count > 0 ? "（失败:" + string.Join(",", fails) + "）" : "");
    }

    // 优雅停止：先 Cancel + 等后台 Task 真正结束，再 Dispose 连接——否则后台正在 ReadAsync 时
    // 把 master 释放掉会触发 ObjectDisposedException 竞态。
    private async Task DisconnectAsync()
    {
        if (_pollCts != null)
        {
            _pollCts.Cancel();
            if (_pollTask != null) { try { await _pollTask; } catch { /* 取消属正常 */ } }
            _pollTask = null;
            _pollCts.Dispose();
            _pollCts = null;
        }
        foreach (var m in _zone.Values) { m.FrameLogged -= LogFrame; m.Dispose(); }
        _zone.Clear();
        IsConnected = false;
        ConnectText = "连接全部分区";
        if (StatusText.StartsWith("已连接")) StatusText = "未连接";
    }

    // ===================== 后台采集循环 =====================
    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(800));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                // 1) 后台线程：只做网络 IO，把原始寄存器值收进纯数据快照（不碰任何 VM / 绑定集合）
                PollSnapshot snap = await ReadAllZonesAsync(ct).ConfigureAwait(false);
                // 2) UI 线程：一次性把快照批量回填到 VM（SetReg/SetBit、趋势、报警、报文）
                await _dispatcher.InvokeAsync(() => ApplySnapshot(snap));
            }
        }
        catch (OperationCanceledException) { /* 断开/关闭，正常退出 */ }
    }

    // 4 区各自独立连接、独立信号量，可并行读：整轮耗时≈最慢一区，而非四区之和。
    private async Task<PollSnapshot> ReadAllZonesAsync(CancellationToken ct)
    {
        var masters = _zone.ToArray();   // 期间 _zone 只在 UI 线程(ApplySnapshot)改，且与本读被 await 串行，安全
        var tasks = masters.Select(kv => ReadZoneAsync(kv.Key, kv.Value, ct)).ToArray();
        var zoneSnaps = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new PollSnapshot(zoneSnaps);
    }

    private async Task<ZoneSnapshot> ReadZoneAsync(Category cat, ModbusTcpMaster m, CancellationToken ct)
    {
        var regBlocks = new List<RegBlock>();
        var bitBlocks = new List<BitBlock>();
        try
        {
            await CollectRegsAsync(cat, Area.InputRegister, m.ReadInputRegistersAsync, regBlocks, ct).ConfigureAwait(false);
            await CollectRegsAsync(cat, Area.HoldingRegister, m.ReadHoldingRegistersAsync, regBlocks, ct).ConfigureAwait(false);
            await CollectBitsAsync(cat, Area.DiscreteInput, m.ReadDiscreteInputsAsync, bitBlocks, ct).ConfigureAwait(false);
            await CollectBitsAsync(cat, Area.Coil, m.ReadCoilsAsync, bitBlocks, ct).ConfigureAwait(false);
            return new ZoneSnapshot(cat, true, null, regBlocks, bitBlocks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // 全局取消(断开/关闭)：向上抛，由 PollLoop 统一吞掉退出
        }
        catch (Exception ex)
        {
            // 单区失败(含事务超时的 OCE、IO 异常)：标记该区掉线，不影响其余区，循环继续
            string err = ex is OperationCanceledException ? "读取超时" : ex.Message;
            return new ZoneSnapshot(cat, false, err, regBlocks, bitBlocks);
        }
    }

    // 按"分区 + 数据区"批量读；保持寄存器跨度大(三机区到207)，按 120 一段分块读 → 收进快照块（不赋值 VM）
    private async Task CollectRegsAsync(Category cat, Area area, Func<ushort, ushort, CancellationToken, Task<ushort[]>> read, List<RegBlock> outBlocks, CancellationToken ct)
    {
        var pts = _all.Where(v => v.Point.Category == cat && v.Point.Area == area).ToList();
        if (pts.Count == 0) return;
        ushort min = pts.Min(v => v.Point.Address), max = pts.Max(v => v.Point.Address);
        for (int start = min; start <= max; start += 120)
        {
            ushort cnt = (ushort)Math.Min(120, max - start + 1);
            ushort[] data = await read((ushort)start, cnt, ct).ConfigureAwait(false);
            outBlocks.Add(new RegBlock(cat, area, (ushort)start, data));
        }
    }

    private async Task CollectBitsAsync(Category cat, Area area, Func<ushort, ushort, CancellationToken, Task<bool[]>> read, List<BitBlock> outBlocks, CancellationToken ct)
    {
        var pts = _all.Where(v => v.Point.Category == cat && v.Point.Area == area).ToList();
        if (pts.Count == 0) return;
        ushort min = pts.Min(v => v.Point.Address), max = pts.Max(v => v.Point.Address);
        bool[] data = await read(min, (ushort)(max - min + 1), ct).ConfigureAwait(false);
        outBlocks.Add(new BitBlock(cat, area, min, data));
    }

    // ===================== UI 线程：批量回填快照（原 OnPoll 后半段）=====================
    private void ApplySnapshot(PollSnapshot snap)
    {
        foreach (var z in snap.Zones)
        {
            if (!z.Ok) { HandleZoneDropped(z.Cat, z.Error ?? "未知错误"); continue; }
            foreach (var rb in z.RegBlocks)
                foreach (var v in _all.Where(v => v.Point.Category == rb.Cat && v.Point.Area == rb.Area
                           && v.Point.Address >= rb.Start && v.Point.Address < rb.Start + rb.Regs.Length))
                    v.SetReg(rb.Regs[v.Point.Address - rb.Start]);
            foreach (var bb in z.BitBlocks)
                foreach (var v in _all.Where(v => v.Point.Category == bb.Cat && v.Point.Area == bb.Area))
                    v.SetBit(bb.Bits[v.Point.Address - bb.Start]);
        }

        DrainFrameQueue();
        SyncAlarms();
        RunningCount = _all.Count(v => v.IsOn && v.Name.Contains("运行"));
        AlarmCount = Alarms.Count;
        // 趋势/柱状读的是 PointVm.Numeric，必须在上面 SetReg/SetBit 之后执行
        foreach (var t in Trends) t.Push();
        for (int i = 0; i < 4; i++) _loopCurrents[i] = Find("组合开关", $"回路{i + 1}电流")?.Numeric ?? 0;
        for (int i = 0; i < _supportPts.Count; i++) _supportPressures[i] = _supportPts[i].Numeric;

        if (_zone.Count == 0)
        {
            IsConnected = false; ConnectText = "连接全部分区";
            _pollCts?.Cancel();   // 全部掉线：请求后台循环退出（残留字段在下次 Connect 的 DisconnectAsync 清理）
        }
        else if (!StatusText.Contains("掉线")) StatusText = $"已连接 {_zone.Count}/4 区 · {DateTime.Now:HH:mm:ss}";
    }

    // 单区掉线：在 UI 线程摘掉该区连接（与下一拍后台读之间被 await 串行，安全）
    private void HandleZoneDropped(Category cat, string err)
    {
        if (_zone.TryGetValue(cat, out var m)) { m.FrameLogged -= LogFrame; m.Dispose(); _zone.Remove(cat); }
        StatusText = $"「{RegisterMap.CategoryName(cat)}」区掉线：{err}";
    }

    private void SyncAlarms()
    {
        var cur = _all.Where(v => v.IsAlarm).ToList();
        for (int i = Alarms.Count - 1; i >= 0; i--) if (!cur.Contains(Alarms[i])) Alarms.RemoveAt(i);
        foreach (var a in cur) if (!Alarms.Contains(a)) Alarms.Add(a);
    }

    // ===================== 报文 =====================
    // 后台线程(采集/PLC/写)产生报文 → 只入并发队列（线程安全、不碰绑定集合）。
    public void LogFrame(FrameLog f) => _frameQ.Enqueue(f);

    // UI 线程统一把队列里的报文倒进 Frames（由 ApplySnapshot 和 PLC 轮询那一拍调用）。
    public void DrainFrameQueue()
    {
        while (_frameQ.TryDequeue(out var f))
        {
            if (FramePaused) continue;
            if (!f.IsWrite && !LogPolling) continue;
            Frames.Insert(0, f);
        }
        while (Frames.Count > FrameCap) Frames.RemoveAt(Frames.Count - 1);
    }

    [RelayCommand] private void ClearFrames() => Frames.Clear();

    // ===================== 写入 / 调度 =====================
    [RelayCommand]
    private async Task WriteOnce()
    {
        var b = BuildWrite();
        if (b == null) return;
        try { await b.Value.action(CancellationToken.None); WriteHint = "已写入：" + b.Value.desc; }
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

    // 一键演示多寄存器写(FC16)：把支架群 1#~8# 压力设定一次写完（三机区）
    [RelayCommand]
    private async Task WriteSupportGroup()
    {
        if (!_zone.TryGetValue(Category.ThreeMachine, out var m)) { WriteHint = "未连接三机区"; return; }
        ushort val = ushort.TryParse(WriteValue.Trim(), out var v) ? v : (ushort)320;
        var vals = Enumerable.Repeat(val, RegisterMap.SupportGroupCount).ToArray();
        try { await m.WriteMultipleRegistersAsync(RegisterMap.SupportGroupStart, vals); WriteHint = $"FC16 群写支架群压力设定 ×{vals.Length} = {val}"; }
        catch (Exception ex) { WriteHint = "群写失败：" + ex.Message; }
    }

    // 委托产物改为 Func<CancellationToken,Task>：单次写(WriteOnce)与周期写(Scheduler)统一异步执行。
    private (string desc, Func<CancellationToken, Task> action)? BuildWrite()
    {
        if (WriteTarget is null) { WriteHint = "未选点位"; return null; }
        var p = WriteTarget.Point;
        if (!_zone.TryGetValue(p.Category, out var m)) { WriteHint = $"未连接「{RegisterMap.CategoryName(p.Category)}」区"; return null; }
        string valText = WriteValue.Trim(), bitText = WriteBit.Trim();
        ushort addr = p.Address;

        if (p.Area == Area.Coil)
        {
            bool on = valText is "1" or "true" or "ON" or "on";
            return ($"写线圈 @{addr} = {(on ? "ON" : "OFF")}", ct => m.WriteSingleCoilAsync(addr, on, ct));
        }

        if (!string.IsNullOrEmpty(bitText))   // 按位写：read-modify-write
        {
            if (!int.TryParse(bitText, out int bit) || bit < 0 || bit > 15) { WriteHint = "位应为 0~15"; return null; }
            bool set = valText is "1" or "true";
            return ($"按位写 @{addr}.bit{bit} = {(set ? 1 : 0)}", async ct =>
            {
                ushort cur = (await m.ReadHoldingRegistersAsync(addr, 1, ct).ConfigureAwait(false))[0];
                ushort nw = set ? (ushort)(cur | (1 << bit)) : (ushort)(cur & ~(1 << bit));
                await m.WriteSingleRegisterAsync(addr, nw, ct).ConfigureAwait(false);
            });
        }

        if (valText.Contains(','))            // 多字节：写多寄存器 FC16
        {
            var parts = valText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var vals = new ushort[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!ushort.TryParse(parts[i], out vals[i])) { WriteHint = "多值非法"; return null; }
            return ($"写多寄存器 @{addr} ×{vals.Length}", ct => m.WriteMultipleRegistersAsync(addr, vals, ct));
        }

        if (!ushort.TryParse(valText, out ushort vv)) { WriteHint = "值应为 0~65535 整数"; return null; }
        return ($"写寄存器 @{addr} = {vv}", ct => m.WriteSingleRegisterAsync(addr, vv, ct));
    }

    private static int ParseInt(string s, int dft) => int.TryParse(s.Trim(), out int v) ? v : dft;
}

// ===================== 采集快照（纯数据，后台线程只填它，不碰 VM）=====================
// 后台读到的原始寄存器/位数据先收进这些 record，再由 UI 线程的 ApplySnapshot 统一回填。
internal sealed record RegBlock(Category Cat, Area Area, ushort Start, ushort[] Regs);
internal sealed record BitBlock(Category Cat, Area Area, ushort Start, bool[] Bits);
internal sealed record ZoneSnapshot(Category Cat, bool Ok, string? Error, List<RegBlock> RegBlocks, List<BitBlock> BitBlocks);
internal sealed record PollSnapshot(ZoneSnapshot[] Zones);
