// ============================================================================
//  HmiApp / MainWindow —— 综采设备 Modbus 上位机(主站) 的 MVP
//  对应题目二(WPF 客户端：实时监测 + 数据写入) + 题目一(客户端读写服务端)。
//
//  WPF 类比 Android：MainWindow.xaml ≈ layout.xml，本文件 ≈ Activity；
//  DispatcherTimer ≈ Handler.postDelayed；DataGrid 绑定 ObservableCollection ≈ RecyclerView。
//
//  设计要点（面试可讲）：
//   1) 按寄存器地图(Shared.RegisterMap)生成监测行，UI 与数据模型解耦；
//   2) DispatcherTimer 每 500ms 轮询，按"数据区"批量读(一次请求覆盖一段地址)，减少往返；
//   3) Tick 回调在 UI 线程，直接更新绑定属性安全；读异常即断开并提示。
// ============================================================================

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NModbus;
using Shared;

namespace HmiApp;

public partial class MainWindow : Window
{
    private readonly ModbusFactory _factory = new();
    private TcpClient? _tcp;
    private IModbusMaster? _master;
    private readonly DispatcherTimer _timer = new();

    private readonly ObservableCollection<MonitorRow> _rows = new();
    private readonly List<(RegPoint pt, MonitorRow row)> _bind = new();   // 点位定义 ↔ 显示行

    public MainWindow()
    {
        InitializeComponent();

        // 按寄存器地图构建监测行
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
            _bind.Add((p, row));
        }
        grid.ItemsSource = _rows;

        // 可写点位填入下拉框（线圈 + 保持寄存器）
        cboWrite.ItemsSource = RegisterMap.Points.Where(p => p.Writable).Select(p => new WriteItem(p)).ToList();
        if (cboWrite.Items.Count > 0) cboWrite.SelectedIndex = 0;

        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += OnTick;
    }

    // ---- 连接 / 断开 ----
    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_master != null) { Disconnect(); return; }

        try
        {
            _tcp = new TcpClient();
            _tcp.Connect(txtIp.Text.Trim(), int.Parse(txtPort.Text.Trim()));
            _master = _factory.CreateMaster(_tcp);          // 在已连接的 TcpClient 上创建 Modbus 主站
            _timer.Start();
            btnConnect.Content = "断开";
            btnWrite.IsEnabled = true;
            txtStatus.Text = "已连接";
            txtStatus.Foreground = Brushes.Green;
        }
        catch (Exception ex)
        {
            txtStatus.Text = "连接失败：" + ex.Message;
            txtStatus.Foreground = Brushes.Red;
            Disconnect();
        }
    }

    private void Disconnect()
    {
        _timer.Stop();
        (_master as IDisposable)?.Dispose();
        _tcp?.Close();
        _master = null;
        _tcp = null;
        if (btnConnect != null) btnConnect.Content = "连接";
        if (btnWrite != null) btnWrite.IsEnabled = false;
        if (txtStatus != null) { txtStatus.Text = "未连接"; txtStatus.Foreground = Brushes.Gray; }
    }

    // ---- 定时轮询 ----
    private void OnTick(object? sender, EventArgs e)
    {
        if (_master == null) return;
        try
        {
            // 按数据区批量读：一次请求覆盖 [min..max] 一段地址，再按地址回填到各行
            UpdateRegisters(Area.InputRegister,   (a, c) => _master!.ReadInputRegisters(RegisterMap.SlaveId, a, c));
            UpdateRegisters(Area.HoldingRegister, (a, c) => _master!.ReadHoldingRegisters(RegisterMap.SlaveId, a, c));
            UpdateBits(Area.DiscreteInput,        (a, c) => _master!.ReadInputs(RegisterMap.SlaveId, a, c));
            UpdateBits(Area.Coil,                 (a, c) => _master!.ReadCoils(RegisterMap.SlaveId, a, c));
            txtStatus.Text = $"已连接 · 刷新 {DateTime.Now:HH:mm:ss}";
            txtStatus.Foreground = Brushes.Green;
        }
        catch (Exception ex)
        {
            txtStatus.Text = "读取异常：" + ex.Message;
            txtStatus.Foreground = Brushes.Red;
            Disconnect();
        }
    }

    private void UpdateRegisters(Area area, Func<ushort, ushort, ushort[]> read)
    {
        var pts = _bind.Where(b => b.pt.Area == area).ToList();
        if (pts.Count == 0) return;
        ushort min = pts.Min(b => b.pt.Address);
        ushort max = pts.Max(b => b.pt.Address);
        ushort[] data = read(min, (ushort)(max - min + 1));
        foreach (var (pt, row) in pts)
        {
            ushort raw = data[pt.Address - min];
            double val = raw * pt.Scale;                                  // 量纲换算：实际值 = 寄存器值 × Scale
            row.Value = pt.Scale == 1.0 ? raw.ToString() : val.ToString("F1");
        }
    }

    private void UpdateBits(Area area, Func<ushort, ushort, bool[]> read)
    {
        var pts = _bind.Where(b => b.pt.Area == area).ToList();
        if (pts.Count == 0) return;
        ushort min = pts.Min(b => b.pt.Address);
        ushort max = pts.Max(b => b.pt.Address);
        bool[] data = read(min, (ushort)(max - min + 1));
        foreach (var (pt, row) in pts)
            row.Value = data[pt.Address - min] ? "ON" : "OFF";
    }

    // ---- 写入（题目二.2）----
    private void OnWriteClick(object sender, RoutedEventArgs e)
    {
        if (_master == null || cboWrite.SelectedItem is not WriteItem item) return;
        var p = item.Point;
        string text = txtWriteVal.Text.Trim();
        try
        {
            if (p.Area == Area.Coil)
            {
                bool on = text is "1" or "true" or "ON" or "on";
                _master.WriteSingleCoil(RegisterMap.SlaveId, p.Address, on);   // 功能码 05
                txtStatus.Text = $"已写线圈 {p.Name} = {(on ? "ON" : "OFF")}";
            }
            else if (p.Area == Area.HoldingRegister)
            {
                if (!ushort.TryParse(text, out ushort v)) { txtStatus.Text = "值非法（应为 0~65535 整数）"; txtStatus.Foreground = Brushes.Red; return; }
                _master.WriteSingleRegister(RegisterMap.SlaveId, p.Address, v); // 功能码 06
                txtStatus.Text = $"已写寄存器 {p.Name} = {v}";
            }
            txtStatus.Foreground = Brushes.Green;
        }
        catch (Exception ex)
        {
            txtStatus.Text = "写入异常：" + ex.Message;
            txtStatus.Foreground = Brushes.Red;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Disconnect();
        base.OnClosed(e);
    }

    private static string AreaText(Area a) => a switch
    {
        Area.Coil            => "线圈(RW位)",
        Area.DiscreteInput   => "离散输入(RO位)",
        Area.InputRegister   => "输入寄存器(RO字)",
        Area.HoldingRegister => "保持寄存器(RW字)",
        _ => "",
    };
}

/// <summary>DataGrid 的一行；Value 变化时通知界面刷新（INotifyPropertyChanged）。</summary>
public class MonitorRow : INotifyPropertyChanged
{
    public string Device { get; set; } = "";
    public string Name { get; set; } = "";
    public string AreaText { get; set; } = "";
    public string AddressText { get; set; } = "";
    public string Unit { get; set; } = "";

    private string _value = "--";
    public string Value
    {
        get => _value;
        set { if (_value != value) { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>写入下拉框的条目（包一个可写点位）。</summary>
public class WriteItem
{
    public RegPoint Point { get; }
    public WriteItem(RegPoint p) => Point = p;
    public string WriteLabel => $"{Point.Device} · {Point.Name}  ({(Point.Area == Area.Coil ? "线圈" : "保持寄存器")} @{Point.Address})";
}
