# 综采设备 Modbus TCP 监控与控制上位机原型

中煤科工面试作业 demo。把面试官的 4 道题串成一个系统：
**WPF 上位机（Modbus 主站）⇄ Modbus TCP ⇄ 模拟从站 / OpenPLC（综采设备数据模型）**。

> 设计、术语、寄存器地图、面试讲法见 Obsidian 笔记 `找工作/中煤科工/中煤科工综采上位机Demo-*.md`。

## 环境
- .NET 8 SDK（`dotnet --version` 应为 8.0.x）
- Windows（WPF 仅限 Windows）

## 项目结构
| 项目 | 说明 |
|---|---|
| `Shared` | 综采设备寄存器地图(`RegisterMap.cs`) + **手写 Modbus TCP 主站**(`ModbusTcpMaster.cs`，自己拼/解析每个字节) |
| `SlaveSim` | 自写 Modbus TCP 从站（设备模拟器，用 NModbus），数据会动、响应控制 |
| `HmiApp` | WPF 上位机，**MVVM**(CommunityToolkit.Mvvm) + **LiveCharts2** 图表 + 深色 SCADA 主题。`ViewModels/`(MainViewModel/PointVm) · `Views/`(6 个 tab) · `Themes/DarkTheme.xaml` · `WriteScheduler.cs` |

> 主站特意**手写**（不用库）：MBAP 头 + PDU 每字节都由我们构造/解析，报文查看器能逐字段拆解——用来证明对 Modbus 数据格式的理解。从站用 NModbus 当"标准服务端"反向校验手写帧的正确性。

### HmiApp 架构（MVVM）
- `ViewModels/MainViewModel.cs`：连接/轮询/报文/趋势/写入调度，绑定驱动界面（几乎零 code-behind）。
- `ViewModels/PointVm.cs`：单个监测点（值变化自动通知 UI）。
- `Views/`：`OverviewView`(总览) `DeviceControlView`(设备控制台) `ThreeMachineView` `TransportView` `PowerFluidView` `PlcControlView`(PLC) `FrameView`(报文) `WriteView`(写入调度)。
- `ViewModels/DeviceControlVm.cs`：单台设备控制卡（启停/设定值/动作按钮），写操作经 SlaveSim 连接下发。
- `Themes/DarkTheme.xaml`：深蓝/青 工业 SCADA 配色，App.xaml 全局合并。
- 图表：LiveCharts2（趋势折线 + 组合开关回路电流柱状 + 支架群压力柱状）。

## 运行（开两个终端）
```powershell
# 终端1：先启动模拟从站（监听 127.0.0.1:1502）
dotnet run --project SlaveSim

# 终端2：启动上位机
dotnet run --project HmiApp
```
或在 VS2026：打开 `MiningHmiDemo.sln`，分别把 `SlaveSim`、`HmiApp` 设为启动项运行（可设多启动项）。

## 操作（顶部连接栏 + 8 个 tab）
1. 顶部点「连接」（默认 127.0.0.1:1502）→ 各 tab 每 800ms 刷新；顶部 KPI 显示 设备/运行/报警 数。
2. **总览大屏**：KPI 卡 + 实时趋势（可选监测点）+ 实时报警列表。
3. **设备控制**（工业 HMI 风格控制台）：每台设备一张卡——运行指示灯 + 关键读数 + **启动/停止开关**、**设定值滑块+输入框+下发**、液压支架的**升柱/降柱/移架**、组合开关的**合闸/分闸**、支架群压力**一键 FC16 群写**。直观控制，不用手填寄存器。
4. **PLC 控制**：独立连 OpenPLC(:502)，演示乳化泵启停联锁（PLC 否决上位机指令）。
3. **综采三机 / 运输系统 / 供液·供电**：分系统设备表（报警行红底）；供液供电页含「组合开关回路电流」「支架群压力」柱状图。
4. **收发报文**（核心）：每次读/写出现"发→/←收"两帧，点某帧 → 右侧「逐字段解码」显示 MBAP+PDU 每字节含义；写操作高亮、可只看写/暂停/清空。
5. **写入/调度**（题目二.3）：
   - *按位写*：选保持寄存器，「位」填 `3`、值 `1` → 只改第 3 位（read-modify-write，报文里看到"先读后写"）。
   - *多字节*：值填逗号如 `100,200` → 写多寄存器(FC16)。
   - *一键 FC16 群写*：点「一键写支架群压力设定」→ 一帧写连续 8 个寄存器(HR200..207)，供液供电页柱状图同步变化。
   - *次数+周期*：次数 `5` 周期 `1000` → 每秒写一次共 5 次，任务表显示剩余。

## 端口说明
标准 Modbus 端口是 502，但 <1024 在 Windows 需管理员权限，本 demo 调试统一用 **1502**（见 `Shared/RegisterMap.cs`）。

## 进度
- [x] Phase 2 — MVP：从站 + 上位机端到端跑通（监测 + 写入）
- [x] Phase 3 — 手写主站 + 报文字节查看器(逐字段解码) + 趋势图 + 周期/按位/多字节写入调度器
- [x] Phase 4 — 全套综采设备 + MVVM + LiveCharts2 + 深色 SCADA 多 tab 监控盘 + FC16 连续块群写
- [x] Phase 5 — 「PLC 控制」tab：独立连 OpenPLC(:502)，演示乳化泵启停联锁（梯形图否决上位机指令）。PLC 程序见 `plc/emulsion_interlock.st`，安装/配置见 vault 内《OpenPLC 接入指南》

> Phase 5 说明：上位机代码已就绪（PLC 控制 tab），端到端演示需你在本机装 OpenPLC 并加载梯形图——步骤见《中煤科工综采上位机Demo-OpenPLC接入指南.md》。
