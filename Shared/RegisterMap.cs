namespace Shared;

// ============================================================================
//  综采设备 Modbus 数据模型（题目四的落地：把现实设备映射成 Modbus 寄存器）
//  这份文件被 SlaveSim(从站) 和 HmiApp(上位机) 共用，保证两边地址定义永远一致。
// ============================================================================

/// <summary>
/// Modbus 四类数据区。记忆口诀：线圈/离散输入是"位"，输入/保持寄存器是"字(16位)"；
/// 带"输入"的都只读，"线圈/保持"可写。
/// </summary>
public enum Area
{
    Coil,            // 线圈：可读写的"位"——开关量输出/控制命令（功能码 读01 / 写05,15）
    DiscreteInput,   // 离散输入：只读的"位"——开关量输入/状态/报警（功能码 读02）
    InputRegister,   // 输入寄存器：只读的16位"字"——模拟量采集，如压力/电流/温度（功能码 读04）
    HoldingRegister  // 保持寄存器：可读写的16位"字"——设定值/参数（功能码 读03 / 写06,16）
}

/// <summary>一个监测/控制点位的定义（综采设备数据模型的最小单元）。</summary>
public class RegPoint
{
    public string Device   { get; init; } = "";   // 所属设备
    public string Name     { get; init; } = "";   // 点位名称
    public Area   Area     { get; init; }          // 在哪类数据区
    public ushort Address  { get; init; }          // 地址
    public double Scale    { get; init; } = 1.0;   // 量纲系数：实际值 = 寄存器值 × Scale（位类型忽略）
    public string Unit     { get; init; } = "";    // 单位
    public bool   Writable { get; init; }          // 是否可写（线圈/保持寄存器为 true）

    /// <summary>是否是"位"类型（线圈/离散输入）。</summary>
    public bool IsBit => Area == Area.Coil || Area == Area.DiscreteInput;
}

/// <summary>综采设备寄存器地图（MVP 子集；Phase 4 再扩展成全套设备）。</summary>
public static class RegisterMap
{
    public const byte SlaveId = 1;     // 从站地址（Modbus 单元标识）
    public const int  Port    = 1502;  // 调试端口（标准是 502，但 <1024 在 Windows 需管理员权限，调试改用 1502）

    // 按设备分块：采煤机 base 0 / 液压支架 base 10 / 乳化泵站 base 60（每台留 10 地址便于扩展）
    public static readonly IReadOnlyList<RegPoint> Points = new List<RegPoint>
    {
        // ——— 采煤机（base 0）———
        new() { Device = "采煤机", Name = "位置/行程",       Area = Area.InputRegister,   Address = 0,  Scale = 0.1, Unit = "m" },
        new() { Device = "采煤机", Name = "牵引速度",        Area = Area.InputRegister,   Address = 1,  Scale = 0.1, Unit = "m/min" },
        new() { Device = "采煤机", Name = "左截割电机电流",  Area = Area.InputRegister,   Address = 4,  Scale = 1.0, Unit = "A" },
        new() { Device = "采煤机", Name = "牵引电机温度",    Area = Area.InputRegister,   Address = 6,  Scale = 1.0, Unit = "℃" },
        new() { Device = "采煤机", Name = "运行中",          Area = Area.DiscreteInput,   Address = 0 },
        new() { Device = "采煤机", Name = "故障",            Area = Area.DiscreteInput,   Address = 1 },
        new() { Device = "采煤机", Name = "牵引速度设定",    Area = Area.HoldingRegister, Address = 0,  Scale = 0.1, Unit = "m/min", Writable = true },
        new() { Device = "采煤机", Name = "启停命令",        Area = Area.Coil,            Address = 0,  Writable = true },

        // ——— 液压支架（base 10）———
        new() { Device = "液压支架", Name = "前柱压力",      Area = Area.InputRegister,   Address = 10, Scale = 0.1, Unit = "MPa" },
        new() { Device = "液压支架", Name = "后柱压力",      Area = Area.InputRegister,   Address = 11, Scale = 0.1, Unit = "MPa" },
        new() { Device = "液压支架", Name = "护帮板伸出",    Area = Area.DiscreteInput,   Address = 10 },
        new() { Device = "液压支架", Name = "升柱",          Area = Area.Coil,            Address = 10, Writable = true },
        new() { Device = "液压支架", Name = "降柱",          Area = Area.Coil,            Address = 11, Writable = true },
        new() { Device = "液压支架", Name = "移架",          Area = Area.Coil,            Address = 12, Writable = true },

        // ——— 乳化泵站（base 60）———
        new() { Device = "乳化泵站", Name = "出口压力",      Area = Area.InputRegister,   Address = 60, Scale = 0.1, Unit = "MPa" },
        new() { Device = "乳化泵站", Name = "液箱液位",      Area = Area.InputRegister,   Address = 62, Scale = 1.0, Unit = "%" },
        new() { Device = "乳化泵站", Name = "运行中",        Area = Area.DiscreteInput,   Address = 60 },
        new() { Device = "乳化泵站", Name = "低液位报警",    Area = Area.DiscreteInput,   Address = 61 },
        new() { Device = "乳化泵站", Name = "压力设定",      Area = Area.HoldingRegister, Address = 60, Scale = 0.1, Unit = "MPa", Writable = true },
        new() { Device = "乳化泵站", Name = "泵启停命令",    Area = Area.Coil,            Address = 60, Writable = true },
    };
}
