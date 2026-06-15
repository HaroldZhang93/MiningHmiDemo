// ============================================================================
//  SlaveSim —— 综采设备"模拟从站"(Modbus TCP Server)
//  多从站分区版：按 4 个区各开一个 Modbus 从站端口，模拟井下"多控制器"拓扑。
//    综采三机 1502 / 运输系统 1503 / 供液系统 1504 / 供电系统 1505
//  每个区是独立的 Modbus 服务端(独立数据区)，上位机为每个区维护一条连接。
//  对应题目一(模拟服务端)。
// ============================================================================

using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Data;
using Shared;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var factory = new ModbusFactory();
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// 每个分区建一个独立从站(独立数据区) + 监听端口
var stores = new Dictionary<Category, DefaultSlaveDataStore>();
var listeners = new List<TcpListener>();
var listenTasks = new List<Task>();
foreach (Category cat in Enum.GetValues<Category>())
{
    var store = new DefaultSlaveDataStore();
    stores[cat] = store;
    var slave = factory.CreateSlave(RegisterMap.SlaveId, store);
    int port = RegisterMap.PortOf(cat);
    var listener = new TcpListener(IPAddress.Any, port);
    listener.Start();
    listeners.Add(listener);
    var network = factory.CreateSlaveNetwork(listener);
    network.AddSlave(slave);
    listenTasks.Add(network.ListenAsync(cts.Token));
    Console.WriteLine($"区[{RegisterMap.CategoryName(cat)}] Modbus 从站监听 0.0.0.0:{port}");
}

SeedInitialValues(stores);
var sim = Task.Run(() => SimulationLoop(stores, cts.Token));

Console.WriteLine("全部分区从站就绪，等待上位机连接。Ctrl+C 退出。");
try { await Task.WhenAll(listenTasks); }
catch (OperationCanceledException) { }
finally { foreach (var l in listeners) l.Stop(); }
await sim;
Console.WriteLine("已停止。");


// ---- 播种初值（写到对应分区的数据区）----
static void SeedInitialValues(Dictionary<Category, DefaultSlaveDataStore> st)
{
    var tm = st[Category.ThreeMachine];
    tm.HoldingRegisters.WritePoints(0,  new ushort[] { 60 });    // 采煤机牵引速度设定 6.0 m/min
    tm.HoldingRegisters.WritePoints(20, new ushort[] { 120 });   // 刮板机速度设定 1.20 m/s
    var grp = new ushort[RegisterMap.SupportGroupCount];          // 支架群 1#~8# 立柱压力设定
    for (int i = 0; i < grp.Length; i++) grp[i] = (ushort)(300 + i * 2);
    tm.HoldingRegisters.WritePoints(RegisterMap.SupportGroupStart, grp);

    var fl = st[Category.Fluid];
    fl.HoldingRegisters.WritePoints(60, new ushort[] { 320 });   // 乳化泵压力设定 32.0 MPa
    fl.HoldingRegisters.WritePoints(70, new ushort[] { 80 });    // 喷雾泵压力设定 8.0 MPa
    fl.InputRegisters.WritePoints(62,  new ushort[] { 85 });     // 乳化泵液位 85%
}

// ---- 仿真循环：每 500ms 刷新一次，各设备写到所属分区数据区 ----
static async Task SimulationLoop(Dictionary<Category, DefaultSlaveDataStore> st, CancellationToken token)
{
    var rng = new Random();
    double pos = 0;
    int tick = 0;

    void IR(DefaultSlaveDataStore s, ushort a, int v) => s.InputRegisters.WritePoints(a, new ushort[] { (ushort)Math.Max(0, v) });
    void DI(DefaultSlaveDataStore s, ushort a, bool b) => s.CoilInputs.WritePoints(a, new bool[] { b });
    bool Coil(DefaultSlaveDataStore s, ushort a) => s.CoilDiscretes.ReadPoints(a, 1)[0];
    ushort HR(DefaultSlaveDataStore s, ushort a) => s.HoldingRegisters.ReadPoints(a, 1)[0];
    int N(int b, int spread) => b + rng.Next(-spread, spread + 1);

    var tm = st[Category.ThreeMachine];
    var tr = st[Category.Transport];
    var fl = st[Category.Fluid];
    var pw = st[Category.Power];

    try
    {
        while (!token.IsCancellationRequested)
        {
            tick++;
            bool shearer = Coil(tm, 0), afc = Coil(tm, 20), guard = Coil(tm, 10);
            bool bsl = Coil(tr, 30), crusher = Coil(tr, 40), belt = Coil(tr, 50);
            bool emul = Coil(fl, 60), spray = Coil(fl, 70);

            // —— 综采三机区 (1502) ——
            if (shearer) { pos += 3; if (pos > 2000) pos = 0; }
            IR(tm, 0, (int)pos);
            IR(tm, 1, shearer ? HR(tm, 0) : 0);
            IR(tm, 2, shearer ? N(180, 5) : 180);  IR(tm, 3, shearer ? N(175, 5) : 175);
            IR(tm, 4, shearer ? N(80, 6) : 0);     IR(tm, 5, shearer ? N(78, 6) : 0);
            IR(tm, 6, shearer ? N(55, 3) : 25);
            DI(tm, 0, shearer);  DI(tm, 1, false);

            int frontP = N(300, 8);
            IR(tm, 10, frontP);  IR(tm, 11, N(290, 8));  IR(tm, 12, N(60, 3));
            DI(tm, 10, guard);  DI(tm, 11, frontP < 255);

            IR(tm, 20, afc ? N(120, 8) : 0);  IR(tm, 21, afc ? N(50, 3) : 25);
            IR(tm, 22, afc ? HR(tm, 20) : 0); IR(tm, 23, afc ? N(80, 6) : 0);
            DI(tm, 20, afc);  DI(tm, 21, afc && rng.Next(100) < 4);

            // —— 运输系统区 (1503) ——
            IR(tr, 30, bsl ? N(60, 5) : 0);  IR(tr, 31, bsl ? N(45, 3) : 25);  DI(tr, 30, bsl);
            IR(tr, 40, crusher ? N(90, 6) : 0);  IR(tr, 41, crusher ? N(55, 3) : 25);
            IR(tr, 42, crusher ? N(200, 30) : 0);  DI(tr, 40, crusher);
            IR(tr, 50, belt ? N(315, 8) : 0);  IR(tr, 51, belt ? N(70, 5) : 0);
            DI(tr, 50, belt);
            DI(tr, 51, belt && rng.Next(100) < 3);   // 跑偏
            DI(tr, 52, belt && rng.Next(100) < 3);   // 打滑
            DI(tr, 53, belt && rng.Next(100) < 4);   // 堆煤

            // —— 供液系统区 (1504) ——
            ushort level = fl.InputRegisters.ReadPoints(62, 1)[0];
            if (emul && level > 0) level = (ushort)(level - 1);
            else if (!emul && level < 100) level = (ushort)(level + 1);
            IR(fl, 60, emul ? HR(fl, 60) : 0);  IR(fl, 61, emul ? N(200, 15) : 0);
            IR(fl, 62, level);  IR(fl, 63, N(50, 2));
            DI(fl, 60, emul);  DI(fl, 61, level < 20);
            IR(fl, 70, spray ? HR(fl, 70) : 0);  IR(fl, 71, spray ? N(150, 12) : 0);  DI(fl, 70, spray);

            // —— 供电系统区 (1505) ——
            int loadCount = (shearer ? 1 : 0) + (afc ? 1 : 0) + (bsl ? 1 : 0) + (crusher ? 1 : 0) + (belt ? 1 : 0) + (emul ? 1 : 0);
            IR(pw, 80, N(1140, 6));  IR(pw, 81, loadCount * N(45, 5));
            IR(pw, 82, loadCount * N(50, 6));  IR(pw, 83, N(40, 4) + loadCount * 2);
            DI(pw, 80, false);
            for (int i = 0; i < 4; i++)
            {
                bool on = Coil(pw, (ushort)(90 + i));
                IR(pw, (ushort)(90 + i), on ? N(80, 30) : 0);
                DI(pw, (ushort)(90 + i), on);
            }

            if (tick % 2 == 0)
                Console.WriteLine($"三机[采煤={(shearer ? "运" : "停")} 刮板={(afc ? "运" : "停")}] 运输[皮带={(belt ? "运" : "停")}] " +
                                  $"供液[泵={(emul ? "运" : "停")} 液位={level}%] 供电[负载{loadCount}路]");

            await Task.Delay(500, token);
        }
    }
    catch (OperationCanceledException) { }
}
