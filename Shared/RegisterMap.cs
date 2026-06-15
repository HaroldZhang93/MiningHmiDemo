namespace Shared;

// ============================================================================
//  综采设备 Modbus 数据模型（题目四的落地：把现实设备映射成 Modbus 寄存器）
//  这份文件被 SlaveSim(从站) 和 HmiApp(上位机) 共用，保证两边地址定义永远一致。
//  Phase 4：扩成全套综采设备 + 分类(分 tab) + 一段连续保持寄存器(测多寄存器写)。
// ============================================================================

/// <summary>
/// Modbus 四类数据区。记忆口诀：线圈/离散输入是"位"，输入/保持寄存器是"字(16位)"；
/// 带"输入"的都只读，"线圈/保持"可写。
/// </summary>
public enum Area
{
    Coil,            // 线圈：可读写的"位"——开关量输出/控制命令（功能码 读01 / 写05,15）
    DiscreteInput,   // 离散输入：只读的"位"——开关量输入/状态/报警（功能码 读02）
    InputRegister,   // 输入寄存器：只读的16位"字"——模拟量采集（功能码 读04）
    HoldingRegister  // 保持寄存器：可读写的16位"字"——设定值/参数（功能码 读03 / 写06,16）
}

/// <summary>设备分类（用于上位机分 tab / 分组展示）。</summary>
public enum Category
{
    ThreeMachine,   // 综采三机：采煤机 / 刮板输送机 / 液压支架
    Transport,      // 运输系统：转载机 / 破碎机 / 皮带
    Fluid,          // 供液系统：乳化泵 / 喷雾泵
    Power           // 供电系统：移变 / 组合开关
}

/// <summary>一个监测/控制点位的定义（综采设备数据模型的最小单元）。</summary>
public class RegPoint
{
    public string   Device   { get; init; } = "";   // 所属设备
    public string   Name     { get; init; } = "";   // 点位名称
    public Category Category { get; init; }          // 所属系统（分 tab 用）
    public Area     Area     { get; init; }          // 在哪类数据区
    public ushort   Address  { get; init; }          // 地址
    public double   Scale    { get; init; } = 1.0;   // 量纲系数：实际值 = 寄存器值 × Scale（位类型忽略）
    public string   Unit     { get; init; } = "";    // 单位
    public bool     Writable { get; init; }          // 是否可写（线圈/保持寄存器为 true）
    public bool     AlarmHigh { get; init; }         // true=该位为1时算"报警"（用于总览报警灯/列表）

    /// <summary>是否是"位"类型（线圈/离散输入）。</summary>
    public bool IsBit => Area == Area.Coil || Area == Area.DiscreteInput;
}

/// <summary>综采设备寄存器地图（全套）。</summary>
public static class RegisterMap
{
    public const byte SlaveId = 1;     // 从站地址（Modbus 单元标识）
    public const int  Port    = 1502;  // 综采三机区端口（也是默认/探针用端口）

    // 多从站分区：每个区一个 Modbus 从站端口（模拟井下多控制器拓扑）
    public static int PortOf(Category c) => c switch
    {
        Category.ThreeMachine => 1502,
        Category.Transport    => 1503,
        Category.Fluid        => 1504,
        Category.Power        => 1505,
        _ => 1502,
    };

    // 连续保持寄存器测试块：支架群 1#~8# 立柱压力设定（一段连续 HR，专供 FC16 多寄存器写）
    public const ushort SupportGroupStart = 200;
    public const ushort SupportGroupCount = 8;

    public static readonly IReadOnlyList<RegPoint> Points = Build();

    private static List<RegPoint> Build()
    {
        var L = new List<RegPoint>
        {
            // ============ 综采三机 ============
            // —— 采煤机 base 0 ——
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="位置/行程",     Area=Area.InputRegister,   Address=0,  Scale=0.1, Unit="m" },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="牵引速度",       Area=Area.InputRegister,   Address=1,  Scale=0.1, Unit="m/min" },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="左滚筒高度",     Area=Area.InputRegister,   Address=2,  Scale=1.0, Unit="cm" },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="右滚筒高度",     Area=Area.InputRegister,   Address=3,  Scale=1.0, Unit="cm" },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="左截割电机电流", Area=Area.InputRegister,   Address=4,  Scale=1.0, Unit="A" },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="右截割电机电流", Area=Area.InputRegister,   Address=5,  Scale=1.0, Unit="A" },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="牵引电机温度",   Area=Area.InputRegister,   Address=6,  Scale=1.0, Unit="℃" },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="运行中",         Area=Area.DiscreteInput,   Address=0 },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="故障",           Area=Area.DiscreteInput,   Address=1, AlarmHigh=true },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="牵引速度设定",   Area=Area.HoldingRegister, Address=0,  Scale=0.1, Unit="m/min", Writable=true },
            new() { Device="采煤机", Category=Category.ThreeMachine, Name="启停命令",       Area=Area.Coil,            Address=0,  Writable=true },

            // —— 液压支架 base 10 ——
            new() { Device="液压支架", Category=Category.ThreeMachine, Name="前柱压力",     Area=Area.InputRegister,   Address=10, Scale=0.1, Unit="MPa" },
            new() { Device="液压支架", Category=Category.ThreeMachine, Name="后柱压力",     Area=Area.InputRegister,   Address=11, Scale=0.1, Unit="MPa" },
            new() { Device="液压支架", Category=Category.ThreeMachine, Name="推移行程",     Area=Area.InputRegister,   Address=12, Scale=1.0, Unit="cm" },
            new() { Device="液压支架", Category=Category.ThreeMachine, Name="护帮板伸出",   Area=Area.DiscreteInput,   Address=10 },
            new() { Device="液压支架", Category=Category.ThreeMachine, Name="立柱卸压报警", Area=Area.DiscreteInput,   Address=11, AlarmHigh=true },
            new() { Device="液压支架", Category=Category.ThreeMachine, Name="升柱",         Area=Area.Coil,            Address=10, Writable=true },
            new() { Device="液压支架", Category=Category.ThreeMachine, Name="降柱",         Area=Area.Coil,            Address=11, Writable=true },
            new() { Device="液压支架", Category=Category.ThreeMachine, Name="移架",         Area=Area.Coil,            Address=12, Writable=true },

            // —— 刮板输送机 AFC base 20 ——
            new() { Device="刮板输送机", Category=Category.ThreeMachine, Name="电机电流",   Area=Area.InputRegister,   Address=20, Scale=1.0,  Unit="A" },
            new() { Device="刮板输送机", Category=Category.ThreeMachine, Name="电机温度",   Area=Area.InputRegister,   Address=21, Scale=1.0,  Unit="℃" },
            new() { Device="刮板输送机", Category=Category.ThreeMachine, Name="链速",       Area=Area.InputRegister,   Address=22, Scale=0.01, Unit="m/s" },
            new() { Device="刮板输送机", Category=Category.ThreeMachine, Name="链张力",     Area=Area.InputRegister,   Address=23, Scale=1.0,  Unit="kN" },
            new() { Device="刮板输送机", Category=Category.ThreeMachine, Name="运行中",     Area=Area.DiscreteInput,   Address=20 },
            new() { Device="刮板输送机", Category=Category.ThreeMachine, Name="堆煤报警",   Area=Area.DiscreteInput,   Address=21, AlarmHigh=true },
            new() { Device="刮板输送机", Category=Category.ThreeMachine, Name="速度设定",   Area=Area.HoldingRegister, Address=20, Scale=0.01, Unit="m/s", Writable=true },
            new() { Device="刮板输送机", Category=Category.ThreeMachine, Name="启停命令",   Area=Area.Coil,            Address=20, Writable=true },

            // ============ 运输系统 ============
            // —— 转载机 BSL base 30 ——
            new() { Device="转载机", Category=Category.Transport, Name="电机电流", Area=Area.InputRegister, Address=30, Scale=1.0, Unit="A" },
            new() { Device="转载机", Category=Category.Transport, Name="电机温度", Area=Area.InputRegister, Address=31, Scale=1.0, Unit="℃" },
            new() { Device="转载机", Category=Category.Transport, Name="运行中",   Area=Area.DiscreteInput, Address=30 },
            new() { Device="转载机", Category=Category.Transport, Name="启停命令", Area=Area.Coil,          Address=30, Writable=true },

            // —— 破碎机 base 40 ——
            new() { Device="破碎机", Category=Category.Transport, Name="电机电流", Area=Area.InputRegister, Address=40, Scale=1.0,  Unit="A" },
            new() { Device="破碎机", Category=Category.Transport, Name="电机温度", Area=Area.InputRegister, Address=41, Scale=1.0,  Unit="℃" },
            new() { Device="破碎机", Category=Category.Transport, Name="振动",     Area=Area.InputRegister, Address=42, Scale=0.01, Unit="mm/s" },
            new() { Device="破碎机", Category=Category.Transport, Name="运行中",   Area=Area.DiscreteInput, Address=40 },
            new() { Device="破碎机", Category=Category.Transport, Name="启停命令", Area=Area.Coil,          Address=40, Writable=true },

            // —— 皮带(可伸缩胶带输送机) base 50 ——
            new() { Device="皮带输送机", Category=Category.Transport, Name="带速",     Area=Area.InputRegister, Address=50, Scale=0.01, Unit="m/s" },
            new() { Device="皮带输送机", Category=Category.Transport, Name="张力",     Area=Area.InputRegister, Address=51, Scale=1.0,  Unit="kN" },
            new() { Device="皮带输送机", Category=Category.Transport, Name="运行中",   Area=Area.DiscreteInput, Address=50 },
            new() { Device="皮带输送机", Category=Category.Transport, Name="跑偏报警", Area=Area.DiscreteInput, Address=51, AlarmHigh=true },
            new() { Device="皮带输送机", Category=Category.Transport, Name="打滑报警", Area=Area.DiscreteInput, Address=52, AlarmHigh=true },
            new() { Device="皮带输送机", Category=Category.Transport, Name="堆煤报警", Area=Area.DiscreteInput, Address=53, AlarmHigh=true },
            new() { Device="皮带输送机", Category=Category.Transport, Name="启停命令", Area=Area.Coil,          Address=50, Writable=true },

            // ============ 供液系统 ============
            // —— 乳化泵站 base 60 ——
            new() { Device="乳化泵站", Category=Category.Fluid, Name="出口压力",   Area=Area.InputRegister,   Address=60, Scale=0.1, Unit="MPa" },
            new() { Device="乳化泵站", Category=Category.Fluid, Name="流量",       Area=Area.InputRegister,   Address=61, Scale=1.0, Unit="L/min" },
            new() { Device="乳化泵站", Category=Category.Fluid, Name="液箱液位",   Area=Area.InputRegister,   Address=62, Scale=1.0, Unit="%" },
            new() { Device="乳化泵站", Category=Category.Fluid, Name="乳化液浓度", Area=Area.InputRegister,   Address=63, Scale=0.1, Unit="%" },
            new() { Device="乳化泵站", Category=Category.Fluid, Name="运行中",     Area=Area.DiscreteInput,   Address=60 },
            new() { Device="乳化泵站", Category=Category.Fluid, Name="低液位报警", Area=Area.DiscreteInput,   Address=61, AlarmHigh=true },
            new() { Device="乳化泵站", Category=Category.Fluid, Name="压力设定",   Area=Area.HoldingRegister, Address=60, Scale=0.1, Unit="MPa", Writable=true },
            new() { Device="乳化泵站", Category=Category.Fluid, Name="泵启停命令", Area=Area.Coil,            Address=60, Writable=true },

            // —— 喷雾泵站(题目"液化泵"实为此) base 70 ——
            new() { Device="喷雾泵站", Category=Category.Fluid, Name="出口压力",   Area=Area.InputRegister,   Address=70, Scale=0.1, Unit="MPa" },
            new() { Device="喷雾泵站", Category=Category.Fluid, Name="流量",       Area=Area.InputRegister,   Address=71, Scale=1.0, Unit="L/min" },
            new() { Device="喷雾泵站", Category=Category.Fluid, Name="运行中",     Area=Area.DiscreteInput,   Address=70 },
            new() { Device="喷雾泵站", Category=Category.Fluid, Name="压力设定",   Area=Area.HoldingRegister, Address=70, Scale=0.1, Unit="MPa", Writable=true },
            new() { Device="喷雾泵站", Category=Category.Fluid, Name="泵启停命令", Area=Area.Coil,            Address=70, Writable=true },

            // ============ 供电系统 ============
            // —— 移变(移动变电站) base 80 ——
            new() { Device="移变", Category=Category.Power, Name="进线电压", Area=Area.InputRegister, Address=80, Scale=1.0, Unit="V" },
            new() { Device="移变", Category=Category.Power, Name="进线电流", Area=Area.InputRegister, Address=81, Scale=1.0, Unit="A" },
            new() { Device="移变", Category=Category.Power, Name="功率",     Area=Area.InputRegister, Address=82, Scale=1.0, Unit="kW" },
            new() { Device="移变", Category=Category.Power, Name="温度",     Area=Area.InputRegister, Address=83, Scale=1.0, Unit="℃" },
            new() { Device="移变", Category=Category.Power, Name="漏电报警", Area=Area.DiscreteInput, Address=80, AlarmHigh=true },
        };

        // —— 组合开关 base 90：4 路供电（电流 + 通断 + 分合闸）——
        for (int i = 0; i < 4; i++)
        {
            L.Add(new() { Device="组合开关", Category=Category.Power, Name=$"回路{i + 1}电流", Area=Area.InputRegister, Address=(ushort)(90 + i), Scale=1.0, Unit="A" });
            L.Add(new() { Device="组合开关", Category=Category.Power, Name=$"回路{i + 1}通断", Area=Area.DiscreteInput, Address=(ushort)(90 + i) });
            L.Add(new() { Device="组合开关", Category=Category.Power, Name=$"回路{i + 1}分合闸", Area=Area.Coil, Address=(ushort)(90 + i), Writable=true });
        }

        // —— 液压支架群 1#~8# 立柱压力设定：连续保持寄存器(HR200..207)，专供 FC16 多寄存器写 ——
        for (int i = 0; i < SupportGroupCount; i++)
        {
            L.Add(new() { Device="液压支架群", Category=Category.ThreeMachine, Name=$"{i + 1}#立柱压力设定",
                          Area=Area.HoldingRegister, Address=(ushort)(SupportGroupStart + i), Scale=0.1, Unit="MPa", Writable=true });
        }

        return L;
    }

    public static string CategoryName(Category c) => c switch
    {
        Category.ThreeMachine => "综采三机",
        Category.Transport    => "运输系统",
        Category.Fluid        => "供液系统",
        Category.Power        => "供电系统",
        _ => c.ToString(),
    };
}
