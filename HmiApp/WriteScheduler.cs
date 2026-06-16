// ============================================================================
//  WriteScheduler —— 写入调度器（对应题目二.3）
//  支持：指定地址、指定字节位(按位写)、多字节(写多寄存器)、指定次数 + 周期性写入。
//  实现：一个 100ms 的 DispatcherTimer 驱动所有任务；每个任务记录"下次到点时间"
//        和"剩余次数"，到点就执行一次写、扣一次次数；次数为 0 即完成。
//  具体"写什么"由 MainWindow 传进来的委托(Execute)决定——调度器只管"何时、几次"。
// ============================================================================

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;

namespace HmiApp;

/// <summary>一条写入任务（绑定到任务列表 DataGrid；Remaining/Status 变化会通知界面）。</summary>
public class WriteTaskVm : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string Desc { get; init; } = "";        // 描述：写什么
    public int PeriodMs { get; init; }
    public Func<CancellationToken, Task> Execute { get; init; } = _ => Task.CompletedTask;

    public DateTime NextDue { get; set; }
    public bool Stopped { get; set; }

    private int _remaining;   // -1 = 无限
    public int Remaining { get => _remaining; set { _remaining = value; Notify(nameof(Remaining)); Notify(nameof(RemainText)); } }
    public string RemainText => _remaining < 0 ? "∞" : _remaining.ToString();

    private string _status = "运行";
    public string Status { get => _status; set { _status = value; Notify(nameof(Status)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

public class WriteScheduler
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public ObservableCollection<WriteTaskVm> Tasks { get; } = new();
    private int _seq;
    private int _ticking;                                  // 防重入闸门：上一拍的异步写还没完，本拍直接跳过
    private readonly CancellationTokenSource _cts = new(); // 关闭时取消正在 await 的写

    public WriteScheduler()
    {
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>新增一个周期写入任务。count&lt;=0 表示无限循环。</summary>
    public void Add(string desc, int count, int periodMs, Func<CancellationToken, Task> execute)
    {
        var t = new WriteTaskVm
        {
            Id = ++_seq,
            Desc = desc,
            PeriodMs = Math.Max(20, periodMs),
            Execute = execute,
            Remaining = count <= 0 ? -1 : count,
            NextDue = DateTime.Now,    // 立即首发
            Status = "运行",
        };
        Tasks.Insert(0, t);
    }

    // Tick 在 UI 线程触发，但写本身是 await 异步的——await 期间释放 UI 线程、IO 在后台跑，
    // 续体回到 UI 线程改 Status/Remaining（绑定 DataGrid，需在 UI 线程改）。Interlocked 闸门防止
    // 上一拍的写还没完成、下一拍又叠进来导致同一任务并发写。
    private async void OnTick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;   // 上轮未结束 → 跳过本拍
        try
        {
            var now = DateTime.Now;
            foreach (var t in Tasks.ToList())   // ToList 防遍历期间集合被改（新增/移除任务）
            {
                if (t.Stopped || t.Remaining == 0) continue;
                if (now < t.NextDue) continue;

                try { await t.Execute(_cts.Token); }
                catch (Exception ex) { t.Status = "错误:" + ex.Message; t.Stopped = true; continue; }

                if (t.Remaining > 0) t.Remaining--;
                t.NextDue = DateTime.Now.AddMilliseconds(t.PeriodMs);
                if (t.Remaining == 0) { t.Status = "完成"; t.Stopped = true; }
            }
        }
        finally { Interlocked.Exchange(ref _ticking, 0); }
    }

    public void Stop(WriteTaskVm t) { t.Stopped = true; if (t.Remaining != 0) t.Status = "已停止"; }
    public void StopAll() { foreach (var t in Tasks) Stop(t); }

    /// <summary>关闭时停表并取消正在 await 的写。</summary>
    public void Shutdown() { _timer.Stop(); _cts.Cancel(); }
}
