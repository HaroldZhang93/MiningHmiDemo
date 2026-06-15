// ============================================================================
//  DeviceInfoVm / DeviceInfoCatalog —— 「设备功能说明」tab 的数据
//  对应题目四：业务学习——综采工作面设备的作用、工作原理、关键日常监测量。
//  纯知识性静态内容(不连 Modbus)，按"系统"分组、卡片展示、点击展开详情。
//  内容依据：综采工作面通用工艺 + 煤矿安全规程要点(支架初撑力≈工作阻力70%~80%、
//  乳化液泵站压力≥30MPa/浓度3%~5%/液位≥2/3 等)，与 RegisterMap 的点位互相印证。
// ============================================================================
using CommunityToolkit.Mvvm.ComponentModel;

namespace HmiApp.ViewModels;

/// <summary>一条"关键监测量"：参数 + 典型范围/量程 + 为什么要监测。</summary>
public class KeyDatum
{
    public string Param { get; init; } = "";   // 参数名
    public string Range { get; init; } = "";   // 典型范围 / 量程 / 类型
    public string Why   { get; init; } = "";   // 监测意义
}

/// <summary>一台设备的功能说明（卡片）。IsExpanded 控制详情展开。</summary>
public partial class DeviceInfoVm : ObservableObject
{
    public string Name      { get; init; } = "";   // 中文名
    public string En        { get; init; } = "";   // 英文/型号别称
    public string Tagline   { get; init; } = "";   // 一句话定位（收起时也显示）
    public string Role      { get; init; } = "";   // 作用 / 在系统中的定位
    public string Principle { get; init; } = "";   // 工作原理
    public string Faults    { get; init; } = "";   // 常见故障 / 报警
    public string AccentHex { get; init; } = "#2A9FD6";   // 强调色（随所属系统）
    public IReadOnlyList<KeyDatum> KeyData { get; init; } = Array.Empty<KeyDatum>();

    public string KeyDataCountText => $"关键监测量 {KeyData.Count} 项";

    [ObservableProperty] private bool _isExpanded;
}

/// <summary>一组设备（按系统分组）。</summary>
public class DeviceInfoGroup
{
    public string Title     { get; init; } = "";
    public string Subtitle  { get; init; } = "";
    public string AccentHex { get; init; } = "#2A9FD6";
    public IReadOnlyList<DeviceInfoVm> Devices { get; init; } = Array.Empty<DeviceInfoVm>();
}

/// <summary>综采设备功能说明目录（静态内容）。</summary>
public static class DeviceInfoCatalog
{
    public static IReadOnlyList<DeviceInfoGroup> Build()
    {
        const string C三机 = "#2A9FD6";   // 青
        const string C运输 = "#4FB477";   // 绿
        const string C供液 = "#46B8C8";   // 蓝青
        const string C供电 = "#E0A100";   // 琥珀
        const string C控制 = "#9A86E8";   // 紫

        var groups = new List<DeviceInfoGroup>
        {
            new DeviceInfoGroup
            {
                Title = "综采三机", Subtitle = "割煤 · 支护 · 工作面运输（综采的核心）", AccentHex = C三机,
                Devices = new[]
                {
                    new DeviceInfoVm
                    {
                        Name = "采煤机", En = "Shearer · 双滚筒采煤机", AccentHex = C三机,
                        Tagline = "工作面割煤的核心，骑在刮板机上沿煤壁往返截割落煤。",
                        Role = "综采三机之一。靠左右两个截割滚筒旋转切割煤壁、把煤截落到刮板输送机上；机身骑在刮板机上、靠牵引部沿销轨往返行走，实现连续割煤、自开缺口、斜切进刀。",
                        Principle = "截割电机经摇臂减速箱驱动滚筒；牵引多为交流变频电牵引（无链牵引），行走轮与销排啮合行进；左右摇臂可独立升降调采高；机载控制器与支架电液控制、刮板机联动，实现记忆截割与自动跟机。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="左/右截割电机电流", Range="0~满载(数百A)", Why="反映割煤负荷与堵转，过流即截割阻力过大或卡硬物" },
                            new KeyDatum{ Param="牵引电机/截割电机温度", Range="<≈120℃", Why="过热保护，预示散热不良或长时间过载" },
                            new KeyDatum{ Param="牵引速度", Range="0~约15 m/min", Why="决定割煤速度与产量，须与煤质、支架跟机匹配" },
                            new KeyDatum{ Param="左/右滚筒(摇臂)高度", Range="按采高", Why="控制采高、防止割顶板/割底板" },
                            new KeyDatum{ Param="机身位置/行程", Range="全工作面长", Why="跟机自动化、记忆截割、防碰撞的基准" },
                            new KeyDatum{ Param="运行/故障状态", Range="开关量", Why="启停、急停、冷却/喷雾/电气故障告警" },
                        },
                        Faults = "常见：截割电机过流/过热、内外喷雾水压不足、摇臂渗油、行走轮与销排磨损、机身倾角超限。",
                    },
                    new DeviceInfoVm
                    {
                        Name = "液压支架", En = "Powered Roof Support", AccentHex = C三机,
                        Tagline = "支撑顶板、推移设备、护帮防片帮，为人和设备撑起安全空间。",
                        Role = "综采三机之一，沿工作面成排布置（上百架）。承担支护顶板、随采煤机推进自动移架、推移刮板机、护帮等功能，是工作面顶板安全的核心。",
                        Principle = "立柱是双伸缩液压千斤顶，乳化液泵站供约31.5MPa高压液经电液控制阀进入立柱建立支撑；『初撑力』是升柱接顶时的初始支撑力（约为额定工作阻力的70%~80%），顶板下沉使立柱受压升至『工作阻力』，达安全阀整定值即卸载；推移千斤顶以刮板机为支点完成移架/推溜。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="立柱压力(前柱/后柱)", Range="0~额定工作阻力(大架常达1万~1.4万kN)", Why="反映支护强度与顶板压力：过低=支护不足，骤升=顶板来压" },
                            new KeyDatum{ Param="初撑力", Range="≈工作阻力70%~80%", Why="接顶质量关键指标，不足则顶板易离层下沉" },
                            new KeyDatum{ Param="推移行程", Range="按截深(常0.6~0.8m)", Why="移架/推溜是否到位，跟机自动化判据" },
                            new KeyDatum{ Param="护帮板/各控制阀状态", Range="开关量", Why="防片帮伤人，动作闭锁与安全" },
                            new KeyDatum{ Param="立柱卸压/窜液报警", Range="开关量", Why="密封失效、安全阀频繁开启的预警" },
                        },
                        Faults = "常见：立柱自动卸载/窜液(密封圈失效)、初撑力不足、电液控制阀卡涩、管路爆管漏液。",
                    },
                    new DeviceInfoVm
                    {
                        Name = "刮板输送机", En = "AFC · Armored Face Conveyor（刮板运输机）", AccentHex = C三机,
                        Tagline = "工作面唯一运煤通道，同时是采煤机的『轨道』、支架的『支点』。",
                        Role = "综采三机之一。承接采煤机割落的煤并运至机头转载机；其销排是采煤机牵引行走的轨道、其溜槽是液压支架推移的支点——三机由它机械耦合在一起。",
                        Principle = "机头/机尾电机经减速器驱动链轮，带动圆环链+刮板在中部槽内刮煤运行；多为双中链/双边链、机头机尾双驱；用CST或液力耦合器软启动并均衡多电机功率；张紧装置维持链张力。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="电机电流", Range="0~满载", Why="负荷、堆煤、过载、断链保护" },
                            new KeyDatum{ Param="电机/减速器温度", Range="<≈120℃", Why="过载与散热故障预警" },
                            new KeyDatum{ Param="链速", Range="约0.9~1.4 m/s", Why="运输能力，须与采煤机产量匹配" },
                            new KeyDatum{ Param="链张力", Range="整定范围", Why="过松易掉链/卡链，过紧加速磨损直至断链" },
                            new KeyDatum{ Param="堆煤/堵塞报警", Range="开关量", Why="机头堆煤、过载停机保护" },
                            new KeyDatum{ Param="运行/故障状态", Range="开关量", Why="启停闭锁(须先于采煤机启动、后于其停机)" },
                        },
                        Faults = "常见：断链/掉链、链条过松卡阻、机头堆煤过载、减速器/联轴器故障、中部槽磨穿。",
                    },
                },
            },

            new DeviceInfoGroup
            {
                Title = "运输系统", Subtitle = "工作面 → 顺槽 → 主运（把煤运出去）", AccentHex = C运输,
                Devices = new[]
                {
                    new DeviceInfoVm
                    {
                        Name = "转载机", En = "BSL · Beam Stage Loader（桥式转载机）", AccentHex = C运输,
                        Tagline = "把工作面刮板机的煤『架桥』抬升、转运到顺槽带式输送机上。",
                        Role = "运输系统起点。结构类似加长加高的刮板机，跨在带式输送机机尾上方，将刮板机来煤抬升转载到皮带上，并随工作面推进整体迈步前移。",
                        Principle = "原理同刮板机(链轮+刮板)，但布置成『桥式』形成抬升落差；常与破碎机串联——先破碎大块再上皮带；机身由迈步自移装置整体移动。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="电机电流/温度", Range="0~满载", Why="负荷、过载、搭接段堆煤保护" },
                            new KeyDatum{ Param="链速/链张力", Range="整定", Why="与刮板机一致的链系监测" },
                            new KeyDatum{ Param="运行/故障状态", Range="开关量", Why="与破碎机、皮带的启停联锁" },
                        },
                        Faults = "常见：搭接段堆煤、链条故障、与皮带机尾搭接错位、迈步装置卡阻。",
                    },
                    new DeviceInfoVm
                    {
                        Name = "破碎机", En = "Crusher（破碎滚筒/锤式）", AccentHex = C运输,
                        Tagline = "把大块煤矸破碎到皮带可运的粒度，保护下游皮带不被卡坏。",
                        Role = "通常与转载机一体布置。将转载机来煤中的大块煤、矸石破碎到合格粒度，防止大块卡带、撕带、砸坏胶带。",
                        Principle = "高速旋转的破碎滚筒/锤头将物料击碎，并强制通过篦条/破碎板的间隙控制出料粒度；遇硬物(铁器)可能短时过载或堵转。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="电机电流", Range="0~满载", Why="破碎负荷，卡硬物堵转/过载保护" },
                            new KeyDatum{ Param="电机温度", Range="<≈120℃", Why="过载与散热故障" },
                            new KeyDatum{ Param="振动", Range="监测值", Why="锤头/轴承磨损、转子不平衡预警" },
                            new KeyDatum{ Param="运行/故障状态", Range="开关量", Why="启停联锁(须先于皮带、与转载机协调)" },
                        },
                        Faults = "常见：卡大块/铁器堵转、锤头磨损脱落、轴承过热振动、篦条堵塞。",
                    },
                    new DeviceInfoVm
                    {
                        Name = "带式输送机", En = "Belt Conveyor（可伸缩带式输送机 / 皮带 · 运输机）", AccentHex = C运输,
                        Tagline = "顺槽主运设备，把煤连续长距离运出工作面区段。",
                        Role = "运输系统主力(题目中的『运输机/皮带』)。承接转载机来煤，沿顺槽长距离连续输送；用可伸缩储带装置随工作面推进调节带长。",
                        Principle = "驱动滚筒靠摩擦带动胶带循环运行，机尾/储带仓张紧；大功率多电机用CST/变频软启动与功率平衡；沿线布置跑偏、打滑、堆煤、烟雾、温度、纵撕、急停拉绳等综合保护。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="带速", Range="约2~4 m/s", Why="运输能力；打滑时带速骤降" },
                            new KeyDatum{ Param="张力/张紧状态", Range="整定", Why="防打滑与断带" },
                            new KeyDatum{ Param="跑偏报警", Range="开关量", Why="跑偏跑边→撕带、撒煤，首要保护" },
                            new KeyDatum{ Param="打滑报警", Range="开关量", Why="主从滚筒速差，预示过载或张力不足" },
                            new KeyDatum{ Param="堆煤/烟雾/温度", Range="开关量", Why="堆煤埋人、摩擦起火等综合防护" },
                            new KeyDatum{ Param="沿线急停拉绳", Range="开关量", Why="任意点紧急停车" },
                        },
                        Faults = "常见：跑偏撕带、打滑、堆煤、滚筒/托辊摩擦发热起火、张紧失效、胶带纵向撕裂。",
                    },
                },
            },

            new DeviceInfoGroup
            {
                Title = "供液系统", Subtitle = "为支架供高压乳化液 · 为降尘供高压水", AccentHex = C供液,
                Devices = new[]
                {
                    new DeviceInfoVm
                    {
                        Name = "乳化液泵站", En = "Emulsion Pump Station（乳化泵）", AccentHex = C供液,
                        Tagline = "为液压支架提供约31.5MPa高压乳化液动力——支架的『心脏泵』。",
                        Role = "供液系统核心。向全工作面液压支架的立柱、千斤顶提供高压乳化液；泵站停=支架无法升柱移架、顶板失控，因此是安全关键设备。",
                        Principle = "多柱塞泵由电机驱动产生高压液；乳化液由清水按比例混入乳化油(浓度3%~5%)自动配比；蓄能器稳压、卸载阀按压力自动加/卸载；通常多泵『一备一用』或『多用一备』。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="出口压力", Range="额定≈31.5MPa(不低于30MPa)", Why="直接决定支架初撑力，过低则支护不足" },
                            new KeyDatum{ Param="乳化液浓度", Range="3%~5%", Why="过低=防锈润滑差、密封锈蚀；过高=成本高、乳化不良" },
                            new KeyDatum{ Param="液箱液位", Range="≥箱高2/3", Why="过低吸空损泵、断供" },
                            new KeyDatum{ Param="流量", Range="额定范围", Why="反映支架动作用液量与系统泄漏" },
                            new KeyDatum{ Param="运行/故障 · 低液位报警", Range="开关量", Why="启停、缺液、超温、过载保护" },
                        },
                        Faults = "常见：低液位停泵、浓度不达标、柱塞/密封磨损泄压、卸载阀失灵、过滤器堵塞。",
                    },
                    new DeviceInfoVm
                    {
                        Name = "喷雾泵站", En = "Spray Pump Station（喷雾/清水泵）", AccentHex = C供液,
                        Tagline = "提供高压清水做降尘灭火喷雾，保护呼吸、防煤尘爆炸。⚠ 题目『液化泵』实指此。",
                        Role = "供液系统的降尘分支。为采煤机内外喷雾、支架移架/放煤喷雾、转载点喷雾提供高压水；抑制煤尘(防尘肺、防煤尘爆炸)并冷却截齿。【注：『液化泵』非标准术语，综采泵站是乳化泵站+喷雾/清水泵站，作业时可主动点出此笔误。】",
                        Principle = "高压柱塞泵供清水经管路到各喷雾点；按位置/动作联动开启(如采煤机割煤随动喷雾)；需配水质过滤。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="出口压力", Range="高压(典型十余MPa)", Why="雾化效果与降尘率" },
                            new KeyDatum{ Param="流量", Range="额定", Why="供水能力；喷嘴堵塞时下降" },
                            new KeyDatum{ Param="运行/缺水状态", Range="开关量", Why="与采煤机/放煤联动、缺水保护" },
                        },
                        Faults = "常见：喷嘴堵塞、水质差磨损、缺水空转、管路泄漏致压力不足。",
                    },
                },
            },

            new DeviceInfoGroup
            {
                Title = "供电系统", Subtitle = "工作面动力电的降压与分配保护（隔爆）", AccentHex = C供电,
                Devices = new[]
                {
                    new DeviceInfoVm
                    {
                        Name = "移动变电站", En = "Mobile Substation（移变）", AccentHex = C供电,
                        Tagline = "把顺槽高压电降到工作面设备用电等级，可随工作面迈步搬移。",
                        Role = "供电系统核心。将顺槽高压(如6/10kV)降为工作面动力电压(常见1140V，也有3300V/660V)，为采煤机、刮板机、泵站等大功率设备供电；隔爆、可整体移动。",
                        Principle = "干式/油浸隔爆变压器 + 高压负荷开关 + 低压馈出，集成漏电、过流、短路、绝缘监测保护；橇装可由支架/绞车牵引迈步前移。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="进/出线电压", Range="高压侧6/10kV，低压侧1140V等", Why="电压稳定与跌落监测" },
                            new KeyDatum{ Param="进/出线电流", Range="0~额定", Why="负荷、过流/短路保护" },
                            new KeyDatum{ Param="功率", Range="kW", Why="工作面总负荷监视" },
                            new KeyDatum{ Param="变压器温度", Range="<整定(如≤100~120℃)", Why="过载/散热故障预警" },
                            new KeyDatum{ Param="漏电/绝缘", Range="报警", Why="隔爆供电的人身与防爆安全核心" },
                        },
                        Faults = "常见：漏电跳闸、过流/短路、绝缘下降、变压器过温、移动中电缆受损。",
                    },
                    new DeviceInfoVm
                    {
                        Name = "组合开关", En = "Explosion-proof Combination Switch（隔爆组合开关）", AccentHex = C供电,
                        Tagline = "一台隔爆箱集中分配并保护多路低压供电——工作面的『配电盘』。",
                        Role = "供电系统的配电与保护设备。接受移变低压侧电源，在一个隔爆箱内集成多路(常见4~8路)真空馈电开关，分别为采煤机、刮板机、泵站、转载/破碎等供电并独立保护。",
                        Principle = "每路含真空接触器 + 隔离换向 + 综合保护装置(过流/短路/漏电/断相)；具远控、闭锁、可视化通断状态；隔爆外壳防瓦斯煤尘爆炸。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="各回路电流", Range="0~额定", Why="分路负荷、过流/短路保护" },
                            new KeyDatum{ Param="各回路通断/分合闸", Range="开关量", Why="远程合分闸、闭锁、故障跳闸状态" },
                            new KeyDatum{ Param="漏电/保护动作", Range="报警", Why="接地漏电、断相、欠压保护" },
                        },
                        Faults = "常见：某路过流/漏电跳闸、真空接触器触头烧蚀、保护拒动/误动、闭锁逻辑异常。",
                    },
                },
            },

            new DeviceInfoGroup
            {
                Title = "控制与感知", Subtitle = "自动化集中控制 + 视频可视化（无人化少人化的基础）", AccentHex = C控制,
                Devices = new[]
                {
                    new DeviceInfoVm
                    {
                        Name = "控制器", En = "综采集中控制 / 电液控制系统（SAC/PMC 类）", AccentHex = C控制,
                        Tagline = "把三机、泵站、供电的状态与动作集中起来，实现自动跟机与一键启停。",
                        Role = "控制与感知层核心。含液压支架电液控制系统(每架控制器 + 顺槽主机)与工作面集中控制系统；采集各设备状态、执行联锁与顺序启停、实现采煤机记忆截割与支架自动跟机，数据上传顺槽/地面。【本演示系统的上位机 + OpenPLC 正对应这一层。】",
                        Principle = "电液控制器经本安总线(如CAN)级联各支架，主机协调动作；集控主机汇集三机/泵站/供电的PLC与传感数据，按工艺逻辑联锁(启停顺序、闭锁、急停链)；经工业环网/光纤上传地面。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="各设备运行/故障汇总", Range="开关量", Why="全工作面状态总览与联锁判据" },
                            new KeyDatum{ Param="启停顺序/闭锁状态", Range="逻辑量", Why="防误操作，保证设备先后启停" },
                            new KeyDatum{ Param="急停/闭锁链", Range="开关量", Why="任意点急停全线响应(安全联锁，毫秒级)" },
                            new KeyDatum{ Param="通信状态/在线率", Range="状态", Why="控制器在线、丢包、节点掉线监测" },
                        },
                        Faults = "常见：总线节点掉线、传感器漂移/失效、联锁误闭锁、通信中断导致跟机停止。",
                    },
                    new DeviceInfoVm
                    {
                        Name = "摄像头", En = "Explosion-proof Camera / 工作面视频监控", AccentHex = C控制,
                        Tagline = "让顺槽/地面『看见』工作面，支撑无人化少人化与远程干预。",
                        Role = "控制与感知层。在采煤机机身、机头机尾、转载/破碎点、泵站等关键部位布置隔爆摄像仪，实时回传视频；是『记忆截割+视频复核』、远程监控、无人化的眼睛。",
                        Principle = "隔爆/本安型工业摄像仪经工业以太环网/光纤把H.264/H.265视频流传至顺槽与地面；配防尘喷淋清洗、低照度/红外补光；可与采煤机位置联动自动切换跟拍画面。",
                        KeyData = new[]
                        {
                            new KeyDatum{ Param="在线/视频流状态", Range="状态", Why="摄像头在线、码流正常、丢帧监测" },
                            new KeyDatum{ Param="画面质量/遮挡", Range="状态", Why="镜头被煤尘/水雾遮挡需清洗" },
                            new KeyDatum{ Param="关键部位画面", Range="视频", Why="机头堆煤、滚筒割煤、转载点等可视复核" },
                        },
                        Faults = "常见：镜头积尘水雾遮挡、补光不足、网络丢流、隔爆腔进水。",
                    },
                },
            },
        };

        return groups;
    }
}
