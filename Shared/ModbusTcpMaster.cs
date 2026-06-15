// ============================================================================
//  ModbusTcpMaster —— 自己手写的 Modbus TCP 主站（不依赖 NModbus）
//  目的：每一个字节都由我们自己拼/自己解析，从而能在界面上完整展示收发报文，
//        证明对 Modbus 数据格式(MBAP 头 + PDU)的理解。对应题目一.3。
//
//  Modbus TCP 帧 (ADU) 结构：
//   ┌──────────── MBAP 头 (7字节) ────────────┬──── PDU ────┐
//   │ 事务ID(2) 协议ID(2) 长度(2) 单元ID(1)   │ 功能码(1)+数据 │
//   └─────────────────────────────────────────┴─────────────┘
//   · 协议ID 固定 0；长度 = 其后字节数 = 单元ID(1)+PDU；多字节字段都是大端(BE)。
//   · TCP 已保证可靠，所以不像 RTU 那样带 CRC16。
// ============================================================================

using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Shared;

public enum FrameDir { Tx, Rx }   // 发送 / 接收

/// <summary>一条报文记录（供界面"报文查看器"显示）。</summary>
public class FrameLog
{
    public DateTime Time { get; init; }
    public FrameDir Dir { get; init; }
    public string TimeText => Time.ToString("HH:mm:ss.fff");
    public string DirText => Dir == FrameDir.Tx ? "发 →" : "← 收";
    public string Summary { get; init; } = "";   // 一行摘要
    public string Hex { get; init; } = "";        // 完整帧 hex
    public string Detail { get; init; } = "";     // 逐字段解码（多行，选中时显示）
    public bool IsWrite { get; init; }            // 写操作（界面高亮用）
}

public class ModbusTcpMaster : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly byte _unitId;
    private readonly string _tag;   // 来源标签（如 "PLC"），加在报文摘要前以区分不同连接
    private readonly object _lock = new();
    private ushort _tid;   // 事务ID，每次请求自增（响应里会原样带回，可用于匹配）

    /// <summary>每收发一帧就触发一次，界面订阅它来显示报文。</summary>
    public event Action<FrameLog>? FrameLogged;

    public ModbusTcpMaster(string ip, int port, byte unitId, string tag = "")
    {
        _unitId = unitId;
        _tag = tag;
        _tcp = new TcpClient();
        _tcp.Connect(ip, port);
        _stream = _tcp.GetStream();
    }

    // ---- 读 ----
    public ushort[] ReadInputRegisters(ushort start, ushort qty)   => ReadRegisters(0x04, start, qty);
    public ushort[] ReadHoldingRegisters(ushort start, ushort qty) => ReadRegisters(0x03, start, qty);
    public bool[]   ReadDiscreteInputs(ushort start, ushort qty)   => ReadBits(0x02, start, qty);
    public bool[]   ReadCoils(ushort start, ushort qty)            => ReadBits(0x01, start, qty);

    private ushort[] ReadRegisters(byte fc, ushort start, ushort qty)
    {
        byte[] pdu = { fc, Hi(start), Lo(start), Hi(qty), Lo(qty) };
        byte[] rpdu = Transact(pdu);                 // 响应 PDU = 功能码 + 字节数 + 数据
        var result = new ushort[qty];
        for (int i = 0; i < qty; i++)
            result[i] = (ushort)((rpdu[2 + i * 2] << 8) | rpdu[2 + i * 2 + 1]);   // 寄存器是大端
        return result;
    }

    private bool[] ReadBits(byte fc, ushort start, ushort qty)
    {
        byte[] pdu = { fc, Hi(start), Lo(start), Hi(qty), Lo(qty) };
        byte[] rpdu = Transact(pdu);                 // 响应 PDU = 功能码 + 字节数 + 位打包数据
        var result = new bool[qty];
        for (int i = 0; i < qty; i++)
            result[i] = (rpdu[2 + i / 8] & (1 << (i % 8))) != 0;   // 每字节低位在前
        return result;
    }

    // ---- 写 ----
    public void WriteSingleCoil(ushort addr, bool on)
    {
        byte[] pdu = { 0x05, Hi(addr), Lo(addr), (byte)(on ? 0xFF : 0x00), 0x00 };  // ON=0xFF00 OFF=0x0000
        Transact(pdu, isWriteHint: true);
    }

    public void WriteSingleRegister(ushort addr, ushort val)
    {
        byte[] pdu = { 0x06, Hi(addr), Lo(addr), Hi(val), Lo(val) };
        Transact(pdu, isWriteHint: true);
    }

    public void WriteMultipleRegisters(ushort start, ushort[] vals)
    {
        int n = vals.Length;
        var pdu = new List<byte> { 0x10, Hi(start), Lo(start), Hi((ushort)n), Lo((ushort)n), (byte)(n * 2) };
        foreach (var v in vals) { pdu.Add(Hi(v)); pdu.Add(Lo(v)); }
        Transact(pdu.ToArray(), isWriteHint: true);
    }

    // ---- 核心：拼 ADU、发送、接收、解析、记录报文 ----
    private byte[] Transact(byte[] pdu, bool isWriteHint = false)
    {
        lock (_lock)   // 串行化：一个请求一个响应，避免事务交错
        {
            ushort tid = ++_tid;
            int len = 1 + pdu.Length;                 // 长度字段 = 单元ID + PDU
            var adu = new byte[7 + pdu.Length];
            adu[0] = Hi(tid); adu[1] = Lo(tid);       // 事务ID
            adu[2] = 0;       adu[3] = 0;             // 协议ID = 0
            adu[4] = Hi((ushort)len); adu[5] = Lo((ushort)len);  // 长度
            adu[6] = _unitId;                         // 单元ID(从站地址)
            Array.Copy(pdu, 0, adu, 7, pdu.Length);

            Emit(FrameDir.Tx, adu, isWriteHint);
            _stream.Write(adu, 0, adu.Length);

            // 收响应：先读 6 字节(事务ID+协议ID+长度)，据"长度"再读剩余
            byte[] head = ReadExactly(6);
            int rlen = (head[4] << 8) | head[5];
            byte[] rest = ReadExactly(rlen);          // 单元ID + 响应PDU
            var radu = new byte[6 + rlen];
            Array.Copy(head, 0, radu, 0, 6);
            Array.Copy(rest, 0, radu, 6, rlen);

            Emit(FrameDir.Rx, radu, isWriteHint);

            byte rfc = radu[7];
            if ((rfc & 0x80) != 0)                    // 最高位置1 = 异常响应
            {
                byte ex = radu[8];
                throw new IOException($"Modbus异常: 功能码0x{rfc:X2} 异常码0x{ex:X2} ({ExName(ex)})");
            }

            var rpdu = new byte[rlen - 1];            // 去掉单元ID，返回纯 PDU
            Array.Copy(radu, 7, rpdu, 0, rlen - 1);
            return rpdu;
        }
    }

    private byte[] ReadExactly(int n)
    {
        var buf = new byte[n];
        int off = 0;
        while (off < n)
        {
            int r = _stream.Read(buf, off, n - off);
            if (r <= 0) throw new IOException("连接已关闭");
            off += r;
        }
        return buf;
    }

    private void Emit(FrameDir dir, byte[] adu, bool isWriteHint)
    {
        var (summary, detail, isWrite) = Decode(adu, dir);
        FrameLogged?.Invoke(new FrameLog
        {
            Time = DateTime.Now,
            Dir = dir,
            Summary = string.IsNullOrEmpty(_tag) ? summary : $"[{_tag}] {summary}",
            Hex = HexAll(adu),
            Detail = detail,
            IsWrite = isWrite || isWriteHint,
        });
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
    }

    // ================== 报文解码（逐字段，教学用） ==================
    private static (string summary, string detail, bool isWrite) Decode(byte[] a, FrameDir dir)
    {
        var sb = new StringBuilder();
        ushort tid = U16(a, 0), pid = U16(a, 2), len = U16(a, 4);
        byte uid = a[6], fc = a[7];

        sb.AppendLine($"── MBAP 头 ──");
        sb.AppendLine($"事务ID  : {Bytes(a,0,2)}  = {tid}");
        sb.AppendLine($"协议ID  : {Bytes(a,2,2)}  = {pid}  (Modbus 固定 0)");
        sb.AppendLine($"长度    : {Bytes(a,4,2)}  = {len}  (其后字节数=单元ID+PDU)");
        sb.AppendLine($"单元ID  : {Bytes(a,6,1)}  = {uid}  (从站地址)");
        sb.AppendLine($"── PDU ──");

        if ((fc & 0x80) != 0)
        {
            byte ex = a[8];
            sb.AppendLine($"功能码  : {Bytes(a,7,1)}  = 异常(原0x{(byte)(fc & 0x7F):X2})");
            sb.AppendLine($"异常码  : {Bytes(a,8,1)}  = {ExName(ex)}");
            return ($"⚠ 异常响应 FC0x{(byte)(fc & 0x7F):X2} 码0x{ex:X2}", sb.ToString(), false);
        }

        string name = FcName(fc);
        sb.AppendLine($"功能码  : {Bytes(a,7,1)}  = 0x{fc:X2} {name}");
        string summary;
        bool isWrite = false;

        switch (fc)
        {
            case 0x01: case 0x02: case 0x03: case 0x04:
                if (dir == FrameDir.Tx)
                {
                    ushort start = U16(a, 8), qty = U16(a, 10);
                    sb.AppendLine($"起始地址: {Bytes(a,8,2)}  = {start}");
                    sb.AppendLine($"数量    : {Bytes(a,10,2)}  = {qty}");
                    summary = $"{name} 起始{start} 数量{qty}";
                }
                else
                {
                    byte bc = a[8];
                    sb.AppendLine($"字节数  : {Bytes(a,8,1)}  = {bc}");
                    sb.AppendLine($"数据    : {Bytes(a,9,bc)}");
                    summary = $"{name}·响应 字节数{bc}";
                }
                break;

            case 0x05:
                {
                    ushort addr = U16(a, 8), val = U16(a, 10);
                    sb.AppendLine($"线圈地址: {Bytes(a,8,2)}  = {addr}");
                    sb.AppendLine($"值      : {Bytes(a,10,2)}  = {(val == 0xFF00 ? "ON(0xFF00)" : val == 0 ? "OFF(0x0000)" : val.ToString())}");
                    summary = $"写单线圈 地址{addr} ={(val == 0xFF00 ? "ON" : "OFF")}";
                    isWrite = true;
                }
                break;

            case 0x06:
                {
                    ushort addr = U16(a, 8), val = U16(a, 10);
                    sb.AppendLine($"寄存器址: {Bytes(a,8,2)}  = {addr}");
                    sb.AppendLine($"值      : {Bytes(a,10,2)}  = {val}");
                    summary = $"写单寄存器 地址{addr} ={val}";
                    isWrite = true;
                }
                break;

            case 0x10:
                if (dir == FrameDir.Tx)
                {
                    ushort start = U16(a, 8), qty = U16(a, 10);
                    byte bc = a[12];
                    sb.AppendLine($"起始地址: {Bytes(a,8,2)}  = {start}");
                    sb.AppendLine($"数量    : {Bytes(a,10,2)}  = {qty}");
                    sb.AppendLine($"字节数  : {Bytes(a,12,1)}  = {bc}");
                    sb.AppendLine($"数据    : {Bytes(a,13,bc)}");
                    summary = $"写多寄存器 起始{start} 数量{qty}";
                }
                else
                {
                    ushort start = U16(a, 8), qty = U16(a, 10);
                    sb.AppendLine($"起始地址: {Bytes(a,8,2)}  = {start}");
                    sb.AppendLine($"数量    : {Bytes(a,10,2)}  = {qty}");
                    summary = $"写多寄存器·响应 起始{start} 数量{qty}";
                }
                isWrite = true;
                break;

            default:
                summary = name;
                break;
        }
        return (summary, sb.ToString(), isWrite);
    }

    // ---- 小工具 ----
    private static byte Hi(ushort v) => (byte)(v >> 8);
    private static byte Lo(ushort v) => (byte)(v & 0xFF);
    private static ushort U16(byte[] a, int i) => (ushort)((a[i] << 8) | a[i + 1]);
    private static string HexAll(byte[] a) => string.Join(' ', a.Select(b => b.ToString("X2")));
    private static string Bytes(byte[] a, int off, int count)
    {
        if (count <= 0 || off >= a.Length) return "(空)";
        int end = Math.Min(off + count, a.Length);
        return string.Join(' ', a.Skip(off).Take(end - off).Select(b => b.ToString("X2")));
    }

    private static string FcName(byte fc) => fc switch
    {
        0x01 => "读线圈",
        0x02 => "读离散输入",
        0x03 => "读保持寄存器",
        0x04 => "读输入寄存器",
        0x05 => "写单线圈",
        0x06 => "写单寄存器",
        0x0F => "写多线圈",
        0x10 => "写多寄存器",
        _ => $"FC0x{fc:X2}",
    };

    private static string ExName(byte ex) => ex switch
    {
        0x01 => "非法功能",
        0x02 => "非法数据地址",
        0x03 => "非法数据值",
        0x04 => "从站设备故障",
        _ => $"0x{ex:X2}",
    };
}
