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
| `Shared` | 综采设备寄存器地图（数据模型），从站/上位机共用 |
| `SlaveSim` | 自写 Modbus TCP 从站（设备模拟器），数据会动、响应控制 |
| `HmiApp` | WPF 上位机（Modbus 主站）：实时监测 + 写入 |

## 运行（开两个终端）
```powershell
# 终端1：先启动模拟从站（监听 127.0.0.1:1502）
dotnet run --project SlaveSim

# 终端2：启动上位机
dotnet run --project HmiApp
```
或在 VS2026：打开 `MiningHmiDemo.sln`，分别把 `SlaveSim`、`HmiApp` 设为启动项运行（可设多启动项）。

## 操作
1. 上位机点「连接」（默认 127.0.0.1:1502）→ DataGrid 开始每 500ms 刷新。
2. 在底部「写入点位」选 `采煤机·启停命令`，值填 `1`，点「写入」→ 看采煤机「运行中」变 ON、电流/位置开始变化。
3. 选 `乳化泵站·泵启停命令` 写 `1` → 压力升到设定值、液位开始下降；选 `采煤机·牵引速度设定` 写如 `80` → 牵引速度跟随。

## 端口说明
标准 Modbus 端口是 502，但 <1024 在 Windows 需管理员权限，本 demo 调试统一用 **1502**（见 `Shared/RegisterMap.cs`）。

## 进度
- [x] Phase 2 — MVP：从站 + 上位机端到端跑通（监测 + 写入）
- [ ] Phase 3 — 报文字节查看器 + 周期/按位写入调度器 + 趋势图
- [ ] Phase 4 — 寄存器地图扩成全套综采设备
- [ ] Phase 5 — 接 OpenPLC 真 PLC
