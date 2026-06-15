// ============================================================================
//  HmiApp / MainWindow (Phase 3)
//  集成：实时监测(DataGrid) + 趋势图(TrendChart) + 收发报文查看器 + 写入/周期调度。
//  主站用自己手写的 ModbusTcpMaster（每帧字节可见），从站仍是 SlaveSim。
//
//  数据流：DispatcherTimer 每 800ms 轮询 → 手写主站收发(触发 FrameLogged) →
//          更新监测行 + 趋势；写入/调度同样走手写主站 → 报文区可见每一帧。
// ============================================================================

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shared;

namespace HmiApp;

public partial class MainWindow : Window
{
    private ModbusTcpMaster? _master;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(800) };

    private readonly ObservableCollection<MonitorRow> _rows = new();
    private readonly Dictionary<RegPoint, MonitorRow> _rowOf = new();

    private readonly ObservableCollection<FrameLog> _frames = new();
    private const int FrameCap = 300;

    private readonly WriteScheduler _scheduler = new();

    public MainWindow()
    {
        InitializeComponent();

        // 监测行（按寄存器地图）
        foreach (var p in RegisterMap.Points)
        {
            var row = new MonitorRow
            {
                Device = p.Device,
                Name = p.Name,
                AreaText = AreaText(p.Area),
                AddressText = p.Address.ToString(),
                Unit = p.IsBit ? "" : p.Unit,
            };
            _rows.Add(row);
            _rowOf[p] = row;
        }
        grid.ItemsSource = _rows;

        // 趋势点位下拉（只列模拟量：输入/保持寄存器）
        cboTrend.ItemsSource = RegisterMap.Points
            .Where(p => !p.IsBit)
            .Select(p => new PointItem(p)).ToList();

        // 可写点位下拉（线圈 + 保持寄存器）
        cboWrite.ItemsSource = RegisterMap.Points
            .Where(p => p.Writable)
            .Select(p => new WriteItem(p)).ToList();
        if (cboWrite.Items.Count > 0) cboWrite.SelectedIndex = 0;

        dgFrames.ItemsSource = _frames;
        dgTasks.ItemsSource = _scheduler.Tasks;

        _poll.Tick += OnPoll;
    }

    // ===================== 连接 / 断开 =====================
    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_master != null) { Disconnect(); return; }
        try
        {
            _master = new ModbusTcpMaster(txtIp.Text.Trim(), int.Parse(txtPort.Text.Trim()), RegisterMap.SlaveId);
            _master.FrameLogged += OnFrameLogged;
            if (cboTrend.SelectedItem is PointItem pi) trend.Reset(pi.Point.Name, pi.Point.Unit);
            _poll.Start();
            btnConnect.Content = "断开";
            SetStatus("已连接", true);
        }
        catch (Exception ex)
        {
            SetStatus("连接失败：" + ex.Message, false);
            Disconnect();
        }
    }

    private void Disconnect()
    {
        _poll.Stop();
        if (_master != null) { _master.FrameLogged -= OnFrameLogged; _master.Dispose(); _master = null; }
        if (btnConnect != null) btnConnect.Content = "连接";
        SetStatus("未连接", false);
    }

    // ===================== 轮询监测 =====================
    private void OnPoll(object? sender, EventArgs e)
    {
        if (_master == null) return;
        try
        {
            UpdateRegs(Area.InputRegister,   (a, c) => _master!.ReadInputRegisters(a, c));
            UpdateRegs(Area.HoldingRegister, (a, c) => _master!.ReadHoldingRegisters(a, c));
            UpdateBits(Area.DiscreteInput,   (a, c) => _master!.ReadDiscreteInputs(a, c));
            UpdateBits(Area.Coil,            (a, c) => _master!.ReadCoils(a, c));

            // 趋势：把当前选中点位的实际值推进图里
            if (cboTrend.SelectedItem is PointItem pi && _rowOf.TryGetValue(pi.Point, out var r))
                trend.Add(r.Numeric);

            SetStatus($"已连接 · 刷新 {DateTime.Now:HH:mm:ss}", true);
        }
        catch (Exception ex)
        {
            SetStatus("读取异常：" + ex.Message, false);
            Disconnect();
        }
    }

    private void UpdateRegs(Area area, Func<ushort, ushort, ushort[]> read)
    {
        var pts = RegisterMap.Points.Where(p => p.Area == area).ToList();
        if (pts.Count == 0) return;
        ushort min = pts.Min(p => p.Address), max = pts.Max(p => p.Address);
        ushort[] data = read(min, (ushort)(max - min + 1));
        foreach (var p in pts)
        {
            ushort raw = data[p.Address - min];
            double val = raw * p.Scale;
            var row = _rowOf[p];
            row.Numeric = val;
            row.Value = p.Scale == 1.0 ? raw.ToString() : val.ToString("F1");
        }
    }

    private void UpdateBits(Area area, Func<ushort, ushort, bool[]> read)
    {
        var pts = RegisterMap.Points.Where(p => p.Area == area).ToList();
        if (pts.Count == 0) return;
        ushort min = pts.Min(p => p.Address), max = pts.Max(p => p.Address);
        bool[] data = read(min, (ushort)(max - min + 1));
        foreach (var p in pts)
        {
            bool on = data[p.Address - min];
            var row = _rowOf[p];
            row.Numeric = on ? 1 : 0;
            row.Value = on ? "ON" : "OFF";
        }
    }

    private void OnTrendPointChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cboTrend.SelectedItem is PointItem pi) trend.Reset(pi.Point.Name, pi.Point.Unit);
    }

    // ===================== 报文查看器 =====================
    private void OnFrameLogged(FrameLog f)
    {
        if (btnPause.IsChecked == true) return;                       // 暂停：冻结视图
        if (!f.IsWrite && chkLogPoll.IsChecked == false) return;      // 不记录轮询读报文

        _frames.Insert(0, f);                                         // 最新在最上
        while (_frames.Count > FrameCap) _frames.RemoveAt(_frames.Count - 1);
    }

    private void OnFrameSelected(object sender, SelectionChangedEventArgs e)
    {
        if (dgFrames.SelectedItem is FrameLog f)
            txtDetail.Text = $"完整帧 ({f.DirText}):\r\n{f.Hex}\r\n\r\n{f.Detail}";
    }

    private void OnClearFrames(object sender, RoutedEventArgs e) => _frames.Clear();

    // ===================== 写入 / 调度 =====================
    private void OnWriteOnce(object sender, RoutedEventArgs e)
    {
        var built = BuildWrite();
        if (built == null) return;
        try { built.Value.action(); txtWriteHint.Text = ""; }
        catch (Exception ex) { txtWriteHint.Text = "写入失败：" + ex.Message; }
    }

    private void OnAddTask(object sender, RoutedEventArgs e)
    {
        var built = BuildWrite();
        if (built == null) return;
        int count = ParseInt(txtCount.Text, 1);
        int period = ParseInt(txtPeriod.Text, 1000);
        _scheduler.Add(built.Value.desc, count, period, built.Value.action);
        txtWriteHint.Text = "";
    }

    private void OnStopAll(object sender, RoutedEventArgs e) => _scheduler.StopAll();

    private void OnStopTask(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WriteTaskVm t }) _scheduler.Stop(t);
    }

    /// <summary>根据界面输入构造一次写操作（含描述）。返回 null 表示输入非法。</summary>
    private (string desc, Action action)? BuildWrite()
    {
        if (_master == null) { txtWriteHint.Text = "未连接"; return null; }
        if (cboWrite.SelectedItem is not WriteItem item) { txtWriteHint.Text = "未选点位"; return null; }

        var p = item.Point;
        string valText = txtVal.Text.Trim();
        string bitText = txtBit.Text.Trim();
        ushort addr = p.Address;

        // 线圈：写 0/1
        if (p.Area == Area.Coil)
        {
            bool on = valText is "1" or "true" or "ON" or "on";
            return ($"写线圈 @{addr} = {(on ? "ON" : "OFF")}",
                    () => _master!.WriteSingleCoil(addr, on));
        }

        // 保持寄存器 —— 三种写法：
        // 1) 按位写：填了"位" → read-modify-write，只改该 bit
        if (!string.IsNullOrEmpty(bitText))
        {
            if (!int.TryParse(bitText, out int bit) || bit < 0 || bit > 15) { txtWriteHint.Text = "位应为 0~15"; return null; }
            bool set = valText is "1" or "true";
            return ($"按位写 @{addr}.bit{bit} = {(set ? 1 : 0)}",
                    () =>
                    {
                        ushort cur = _master!.ReadHoldingRegisters(addr, 1)[0];          // 读
                        ushort nw = set ? (ushort)(cur | (1 << bit)) : (ushort)(cur & ~(1 << bit));  // 改
                        _master!.WriteSingleRegister(addr, nw);                          // 写回
                    });
        }

        // 2) 多字节(多寄存器)：值含逗号 → 写多寄存器
        if (valText.Contains(','))
        {
            var parts = valText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var vals = new ushort[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!ushort.TryParse(parts[i], out vals[i])) { txtWriteHint.Text = "多值非法"; return null; }
            return ($"写多寄存器 @{addr} ×{vals.Length}",
                    () => _master!.WriteMultipleRegisters(addr, vals));
        }

        // 3) 单寄存器
        if (!ushort.TryParse(valText, out ushort v)) { txtWriteHint.Text = "值应为 0~65535 整数"; return null; }
        return ($"写寄存器 @{addr} = {v}",
                () => _master!.WriteSingleRegister(addr, v));
    }

    // ===================== 杂项 =====================
    private void SetStatus(string text, bool ok)
    {
        txtStatus.Text = text;
        txtStatus.Foreground = ok ? Brushes.LightGreen : new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7F));
    }

    private static int ParseInt(string s, int dft) => int.TryParse(s.Trim(), out int v) ? v : dft;

    protected override void OnClosed(EventArgs e) { Disconnect(); base.OnClosed(e); }

    private static string AreaText(Area a) => a switch
    {
        Area.Coil => "线圈(RW位)",
        Area.DiscreteInput => "离散输入(RO位)",
        Area.InputRegister => "输入寄存器(RO字)",
        Area.HoldingRegister => "保持寄存器(RW字)",
        _ => "",
    };
}

/// <summary>监测表一行；Value 变化通知界面刷新。</summary>
public class MonitorRow : INotifyPropertyChanged
{
    public string Device { get; set; } = "";
    public string Name { get; set; } = "";
    public string AreaText { get; set; } = "";
    public string AddressText { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Numeric { get; set; }   // 最新数值（趋势图用）

    private string _value = "--";
    public string Value
    {
        get => _value;
        set { if (_value != value) { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>趋势下拉条目。</summary>
public class PointItem
{
    public RegPoint Point { get; }
    public PointItem(RegPoint p) => Point = p;
    public string Label => $"{Point.Device} · {Point.Name}" + (string.IsNullOrEmpty(Point.Unit) ? "" : $" ({Point.Unit})");
}

/// <summary>写入下拉条目。</summary>
public class WriteItem
{
    public RegPoint Point { get; }
    public WriteItem(RegPoint p) => Point = p;
    public string WriteLabel => $"{Point.Device} · {Point.Name}  ({(Point.Area == Area.Coil ? "线圈" : "保持寄存器")} @{Point.Address})";
}
