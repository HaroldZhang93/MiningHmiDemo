(* ============================================================================
   OpenPLC 程序：乳化泵启停联锁（Phase 5 / 题目三）
   在 OpenPLC Editor 新建 ST 程序，按下表声明带"位置"的变量，再写下面的逻辑。

   变量名     类型   位置      含义                上位机看到的 Modbus 地址
   StartReq   BOOL   %QX0.0   启泵请求(上位机下发)   线圈 0
   EStop      BOOL   %QX0.2   急停(上位机下发)       线圈 2
   Level      INT    %QW0     液位%(上位机写)        保持寄存器 0
   PumpRun    BOOL   %QX0.4   泵运行(PLC 输出)       线圈 4
   LowLevel   BOOL   %QX0.6   低液位(PLC 输出)       线圈 6

   地址规则：%QX 字.位 → 线圈号 = 字*8+位；%QW0 → 保持寄存器 0。
   必须与上位机 HmiApp/ViewModels/PlcViewModel.cs 的线圈号(0/2/4/6)与 HR(0) 一致。
   ============================================================================ *)

(* 低液位判定 *)
LowLevel := Level < 20;

(* 泵运行 = 请求启泵 且 无急停 且 不低液位
   低液位或急停时 PLC 强制停泵，上位机无法绕过 —— 这就是"控制" *)
PumpRun := StartReq AND NOT EStop AND NOT LowLevel;
