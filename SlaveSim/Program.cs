// ============================================================================
//  SlaveSim —— 自写的综采设备"模拟从站"(Modbus TCP Server)
//  作用：替代 Modbus Slave 工具，但数据会"活"起来 + 能响应上位机的控制写入。
//  对应题目一(模拟服务端)。运行后等 HmiApp(上位机) 来连。
//
//  面试怎么讲：从站持有 4 个数据区(线圈/离散输入/输入寄存器/保持寄存器)，
//  NModbus 帮我处理 TCP 收包、MBAP 头解析、功能码分发；我只管在数据区里
//  按综采设备的物理规律刷新数值、并根据上位机写下来的线圈/设定值做出反应。
// ============================================================================

using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Data;
using Shared;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"综采设备模拟从站启动... 端口={RegisterMap.Port} 从站地址={RegisterMap.SlaveId}");

var factory = new ModbusFactory();

// DefaultSlaveDataStore 内含 4 个数据区：
//   HoldingRegisters(保持寄存器,RW字)  InputRegisters(输入寄存器,RO字)
//   CoilDiscretes(线圈,RW位)           CoilInputs(离散输入,RO位)
var store = new DefaultSlaveDataStore();
SeedInitialValues(store);   // 播种初值，避免一上来全是 0

var slave = factory.CreateSlave(RegisterMap.SlaveId, store);
var listener = new TcpListener(IPAddress.Any, RegisterMap.Port);
listener.Start();
var network = factory.CreateSlaveNetwork(listener);
network.AddSlave(slave);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// 后台仿真循环：让数据"活"起来 + 响应控制写入（与 ListenAsync 并行跑）
var sim = Task.Run(() => SimulationLoop(store, cts.Token));

Console.WriteLine("从站就绪，等待上位机连接。Ctrl+C 退出。");
try { await network.ListenAsync(cts.Token); }
catch (OperationCanceledException) { }
finally { listener.Stop(); }
await sim;
Console.WriteLine("已停止。");


// ---- 播种初值 ----
static void SeedInitialValues(DefaultSlaveDataStore store)
{
    store.HoldingRegisters.WritePoints(0,  new ushort[] { 60 });   // 采煤机牵引速度设定 = 6.0 m/min
    store.HoldingRegisters.WritePoints(60, new ushort[] { 320 });  // 乳化泵压力设定 = 32.0 MPa
    store.InputRegisters.WritePoints(62,  new ushort[] { 85 });    // 乳化泵液位 = 85%
    store.InputRegisters.WritePoints(10,  new ushort[] { 300 });   // 支架前柱压力 = 30.0 MPa
    store.InputRegisters.WritePoints(11,  new ushort[] { 290 });   // 支架后柱压力 = 29.0 MPa
}

// ---- 仿真循环：每 500ms 刷新一次 ----
static async Task SimulationLoop(DefaultSlaveDataStore store, CancellationToken token)
{
    var rng = new Random();
    double pos = 0;   // 采煤机位置累加器（寄存器单位，×0.1m）

    try
    {
        while (!token.IsCancellationRequested)
        {
            // —— 读上位机下发的控制位/设定值 ——（根据命令决定设备行为）
            bool   shearerRun = store.CoilDiscretes.ReadPoints(0, 1)[0];     // Coil0 采煤机启停
            bool   pumpRun    = store.CoilDiscretes.ReadPoints(60, 1)[0];    // Coil60 泵启停
            bool   guardCmd   = store.CoilDiscretes.ReadPoints(10, 1)[0];    // Coil10 升柱（示意护帮板）
            ushort speedSet   = store.HoldingRegisters.ReadPoints(0, 1)[0];  // HR0 牵引速度设定
            ushort presSet    = store.HoldingRegisters.ReadPoints(60, 1)[0]; // HR60 压力设定

            // —— 采煤机 ——
            if (shearerRun) { pos += 3; if (pos > 2000) pos = 0; }           // 运行时位置推进，到头折返
            store.InputRegisters.WritePoints(0, new ushort[] { (ushort)pos });
            store.InputRegisters.WritePoints(1, new ushort[] { shearerRun ? speedSet : (ushort)0 });
            store.InputRegisters.WritePoints(4, new ushort[] { (ushort)(shearerRun ? 80 + rng.Next(-5, 6) : 0) });   // 电流
            store.InputRegisters.WritePoints(6, new ushort[] { (ushort)(shearerRun ? 55 + rng.Next(-3, 4) : 25) });  // 温度
            store.CoilInputs.WritePoints(0, new bool[] { shearerRun });      // DI0 运行中
            store.CoilInputs.WritePoints(1, new bool[] { false });           // DI1 故障（demo 暂不触发）

            // —— 液压支架（压力小幅波动）——
            store.InputRegisters.WritePoints(10, new ushort[] { (ushort)(300 + rng.Next(-8, 9)) });
            store.InputRegisters.WritePoints(11, new ushort[] { (ushort)(290 + rng.Next(-8, 9)) });
            store.CoilInputs.WritePoints(10, new bool[] { guardCmd });       // DI10 护帮板伸出 = 升柱命令

            // —— 乳化泵站 ——
            ushort pres  = (ushort)(pumpRun ? presSet : 0);                  // 停泵则压力归零
            ushort level = store.InputRegisters.ReadPoints(62, 1)[0];
            if (pumpRun && level > 0)         level = (ushort)(level - 1);   // 运行耗液，液位缓降
            else if (!pumpRun && level < 100) level = (ushort)(level + 1);   // 停泵回补
            store.InputRegisters.WritePoints(60, new ushort[] { pres });
            store.InputRegisters.WritePoints(62, new ushort[] { level });
            store.CoilInputs.WritePoints(60, new bool[] { pumpRun });        // DI60 运行
            store.CoilInputs.WritePoints(61, new bool[] { level < 20 });     // DI61 低液位报警

            Console.WriteLine(
                $"采煤机={(shearerRun ? "运行" : "停 ")} 位置={pos * 0.1:F1}m | " +
                $"泵={(pumpRun ? "运行" : "停 ")} 压力={pres * 0.1:F1}MPa 液位={level}%");

            await Task.Delay(500, token);
        }
    }
    catch (OperationCanceledException) { }
}
