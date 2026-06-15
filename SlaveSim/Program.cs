// ============================================================================
//  SlaveSim —— 综采设备"模拟从站"(Modbus TCP Server)，Phase 4 全套设备版
//  数据会"活"起来 + 响应上位机控制写入。对应题目一(模拟服务端)。
// ============================================================================

using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Data;
using Shared;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"综采设备模拟从站启动... 端口={RegisterMap.Port} 从站地址={RegisterMap.SlaveId}");

var factory = new ModbusFactory();
var store = new DefaultSlaveDataStore();
SeedInitialValues(store);

var slave = factory.CreateSlave(RegisterMap.SlaveId, store);
var listener = new TcpListener(IPAddress.Any, RegisterMap.Port);
listener.Start();
var network = factory.CreateSlaveNetwork(listener);
network.AddSlave(slave);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var sim = Task.Run(() => SimulationLoop(store, cts.Token));

Console.WriteLine("从站就绪，等待上位机连接。Ctrl+C 退出。");
try { await network.ListenAsync(cts.Token); }
catch (OperationCanceledException) { }
finally { listener.Stop(); }
await sim;
Console.WriteLine("已停止。");


// ---- 播种初值（设定值 + 一些初始遥测）----
static void SeedInitialValues(DefaultSlaveDataStore s)
{
    s.HoldingRegisters.WritePoints(0,  new ushort[] { 60 });    // 采煤机牵引速度设定 6.0 m/min
    s.HoldingRegisters.WritePoints(20, new ushort[] { 120 });   // 刮板机速度设定 1.20 m/s
    s.HoldingRegisters.WritePoints(60, new ushort[] { 320 });   // 乳化泵压力设定 32.0 MPa
    s.HoldingRegisters.WritePoints(70, new ushort[] { 80 });    // 喷雾泵压力设定 8.0 MPa
    s.InputRegisters.WritePoints(62,  new ushort[] { 85 });     // 乳化泵液位 85%
    // 支架群 1#~8# 立柱压力设定（连续 HR200..207）初值
    var grp = new ushort[RegisterMap.SupportGroupCount];
    for (int i = 0; i < grp.Length; i++) grp[i] = (ushort)(300 + i * 2);
    s.HoldingRegisters.WritePoints(RegisterMap.SupportGroupStart, grp);
}

// ---- 仿真循环：每 500ms 刷新一次 ----
static async Task SimulationLoop(DefaultSlaveDataStore s, CancellationToken token)
{
    var rng = new Random();
    double pos = 0;
    int tick = 0;

    // 局部小工具
    void IR(ushort a, int v) => s.InputRegisters.WritePoints(a, new ushort[] { (ushort)Math.Max(0, v) });
    void DI(ushort a, bool b) => s.CoilInputs.WritePoints(a, new bool[] { b });
    bool Coil(ushort a) => s.CoilDiscretes.ReadPoints(a, 1)[0];
    ushort HR(ushort a) => s.HoldingRegisters.ReadPoints(a, 1)[0];
    int N(int b, int spread) => b + rng.Next(-spread, spread + 1);

    try
    {
        while (!token.IsCancellationRequested)
        {
            tick++;
            // 读控制
            bool shearer = Coil(0), afc = Coil(20), bsl = Coil(30), crusher = Coil(40), belt = Coil(50);
            bool emul = Coil(60), spray = Coil(70), guard = Coil(10);

            // —— 采煤机 ——
            if (shearer) { pos += 3; if (pos > 2000) pos = 0; }
            IR(0, (int)pos);
            IR(1, shearer ? HR(0) : 0);
            IR(2, shearer ? N(180, 5) : 180);  IR(3, shearer ? N(175, 5) : 175);  // 左右滚筒高度
            IR(4, shearer ? N(80, 6) : 0);     IR(5, shearer ? N(78, 6) : 0);     // 左右电流
            IR(6, shearer ? N(55, 3) : 25);                                       // 温度
            DI(0, shearer);  DI(1, false);

            // —— 液压支架 ——
            int frontP = N(300, 8);
            IR(10, frontP);  IR(11, N(290, 8));  IR(12, N(60, 3));
            DI(10, guard);              // 护帮板伸出 = 升柱命令
            DI(11, frontP < 255);       // 立柱卸压报警(压力过低)

            // —— 刮板输送机 AFC ——
            IR(20, afc ? N(120, 8) : 0);  IR(21, afc ? N(50, 3) : 25);
            IR(22, afc ? HR(20) : 0);     IR(23, afc ? N(80, 6) : 0);
            DI(20, afc);  DI(21, afc && rng.Next(100) < 4);   // 堆煤偶发

            // —— 转载机 BSL ——
            IR(30, bsl ? N(60, 5) : 0);  IR(31, bsl ? N(45, 3) : 25);  DI(30, bsl);

            // —— 破碎机 ——
            IR(40, crusher ? N(90, 6) : 0);  IR(41, crusher ? N(55, 3) : 25);
            IR(42, crusher ? N(200, 30) : 0);  DI(40, crusher);

            // —— 皮带 ——
            IR(50, belt ? N(315, 8) : 0);  IR(51, belt ? N(70, 5) : 0);
            DI(50, belt);
            DI(51, belt && rng.Next(100) < 3);   // 跑偏
            DI(52, belt && rng.Next(100) < 3);   // 打滑
            DI(53, belt && rng.Next(100) < 4);   // 堆煤

            // —— 乳化泵站 ——
            ushort level = s.InputRegisters.ReadPoints(62, 1)[0];
            if (emul && level > 0) level = (ushort)(level - 1);
            else if (!emul && level < 100) level = (ushort)(level + 1);
            IR(60, emul ? HR(60) : 0);  IR(61, emul ? N(200, 15) : 0);
            IR(62, level);              IR(63, N(50, 2));     // 浓度 5.0%
            DI(60, emul);  DI(61, level < 20);

            // —— 喷雾泵站 ——
            IR(70, spray ? HR(70) : 0);  IR(71, spray ? N(150, 12) : 0);  DI(70, spray);

            // —— 移变 ——
            int loadCount = (shearer ? 1 : 0) + (afc ? 1 : 0) + (bsl ? 1 : 0) + (crusher ? 1 : 0) + (belt ? 1 : 0) + (emul ? 1 : 0);
            IR(80, N(1140, 6));                 // 进线电压
            IR(81, loadCount * N(45, 5));       // 进线电流随负载
            IR(82, loadCount * N(50, 6));       // 功率
            IR(83, N(40, 4) + loadCount * 2);   // 温度
            DI(80, false);                      // 漏电(demo 默认无)

            // —— 组合开关 4 回路 ——
            for (int i = 0; i < 4; i++)
            {
                bool on = Coil((ushort)(90 + i));
                IR((ushort)(90 + i), on ? N(80, 30) : 0);
                DI((ushort)(90 + i), on);
            }

            if (tick % 2 == 0)
                Console.WriteLine($"采煤机={(shearer ? "运" : "停")} 刮板={(afc ? "运" : "停")} 皮带={(belt ? "运" : "停")} | " +
                                  $"乳化泵={(emul ? "运" : "停")} 压力={(emul ? HR(60) : 0) * 0.1:F1}MPa 液位={level}% | 负载{loadCount}路");

            await Task.Delay(500, token);
        }
    }
    catch (OperationCanceledException) { }
}
