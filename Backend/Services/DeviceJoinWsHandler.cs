using System.Buffers.Binary;
using System.Net.WebSockets;
using Backend.Data;
using Backend.Hubs;
using Backend.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Services;

/// <summary>
/// WebSocket handler cho luồng JOIN thiết bị phần cứng.
///
/// JOIN_REQUEST frame (20 bytes, Device → Server):
///   [0]      START    = 0xAA
///   [1]      TYPE     = 0x20
///   [2–7]    MAC      = 6 bytes
///   [8–11]   HW_ID    = uint32 LE
///   [12]     FW_MAJ   = uint8
///   [13]     FW_MIN   = uint8
///   [14]     FW_PAT   = uint8
///   [15]     RESERVED = 0x00
///   [16–17]  CRC      = CRC-16/CCITT-FALSE over bytes [0–15]
///   [18]     RESERVED = 0x00
///   [19]     STOP     = 0xBB
///
/// JOIN_RESPONSE frame (8 bytes, Server → Device):
///   [0]      START    = 0xAA
///   [1]      TYPE     = 0x21
///   [2]      STATUS   = 0x01 (Accept) | 0x00 (Reject)
///   [3]      NODE_ID  = assigned NodeByteId, or 0x00 if rejected
///   [4–5]    CRC      = CRC-16/CCITT-FALSE over bytes [0–3]
///   [6]      RESERVED = 0x00
///   [7]      STOP     = 0xBB
/// </summary>
public static class DeviceJoinWsHandler
{
    private const byte FrameStart   = 0xAA;
    private const byte FrameStop    = 0xBB;
    private const byte TypeJoinReq  = 0x20;
    private const byte TypeJoinResp = 0x21;
    private const int  FrameSize    = 20;

    private static readonly TimeSpan JoinTimeout = TimeSpan.FromMinutes(5);

    public static async Task HandleAsync(
        WebSocket               ws,
        DeviceJoinRegistry      registry,
        TunnelDbContext         db,
        IHubContext<SensorHub>  hub,
        ILogger                 logger,
        CancellationToken       ct)
    {
        logger.LogInformation("[JoinWs] New connection");
        var buf   = new byte[256];
        var frame = new List<byte>(64);

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buf, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }

                frame.AddRange(buf[..result.Count]);

                // Xử lý tất cả frame đầy đủ trong buffer
                while (frame.Count >= FrameSize)
                {
                    int startIdx = frame.IndexOf(FrameStart);
                    if (startIdx < 0) { frame.Clear(); break; }
                    if (startIdx > 0) frame.RemoveRange(0, startIdx);
                    if (frame.Count < FrameSize) break;

                    var f = frame.GetRange(0, FrameSize).ToArray();
                    frame.RemoveRange(0, FrameSize);

                    if (f[1] != TypeJoinReq || f[19] != FrameStop)
                    {
                        logger.LogDebug("[JoinWs] Invalid TYPE or STOP byte — discarding");
                        continue;
                    }

                    ushort expectedCrc = Crc16Ccitt(f.AsSpan(0, 16));
                    ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(16));
                    if (expectedCrc != receivedCrc)
                    {
                        logger.LogWarning("[JoinWs] CRC mismatch expected=0x{Exp:X4} received=0x{Got:X4}",
                            expectedCrc, receivedCrc);
                        continue;
                    }

                    string mac   = $"{f[2]:X2}:{f[3]:X2}:{f[4]:X2}:{f[5]:X2}:{f[6]:X2}:{f[7]:X2}";
                    uint   hwId  = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(8));
                    string fwVer = $"{f[12]}.{f[13]}.{f[14]}";

                    logger.LogInformation("[JoinWs] JOIN_REQUEST mac={Mac} hwId={HwId} fw={Fw}", mac, hwId, fwVer);

                    // Lưu vào DB
                    var joinReq = new DevicePendingJoin
                    {
                        MacAddress      = mac,
                        HardwareId      = hwId,
                        FirmwareVersion = fwVer,
                        Status          = JoinRequestStatus.Pending,
                        RequestedAt     = DateTimeOffset.UtcNow
                    };
                    db.DevicePendingJoins.Add(joinReq);
                    await db.SaveChangesAsync(ct);

                    // Broadcast đến Station app qua SignalR
                    await hub.Clients.All.SendAsync("NewJoinRequest", new
                    {
                        joinReq.Id,
                        joinReq.MacAddress,
                        joinReq.HardwareId,
                        joinReq.FirmwareVersion,
                        RequestedAt = joinReq.RequestedAt.ToString("O")
                    }, ct);

                    // Đăng ký TCS và chờ quyết định của operator (timeout 5 phút)
                    var tcs = registry.Register(joinReq.Id);
                    JoinDecision decision;
                    try
                    {
                        using var timeout = new CancellationTokenSource(JoinTimeout);
                        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                        decision = await tcs.Task.WaitAsync(linked.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        joinReq.Status      = JoinRequestStatus.Expired;
                        joinReq.RespondedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(CancellationToken.None);

                        logger.LogInformation("[JoinWs] JOIN_REQUEST {Id} expired — sending reject", joinReq.Id);
                        await SendJoinResponseAsync(ws, false, 0, logger, CancellationToken.None);
                        continue;
                    }

                    await SendJoinResponseAsync(ws, decision.Accepted, decision.AssignedNodeByteId, logger, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (WebSocketException ex)
        {
            logger.LogWarning("[JoinWs] WS error: {Msg}", ex.Message);
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

            logger.LogInformation("[JoinWs] Connection closed");
        }
    }

    private static async Task SendJoinResponseAsync(
        WebSocket ws, bool accepted, byte nodeId, ILogger logger, CancellationToken ct)
    {
        var resp = new byte[8];
        resp[0] = FrameStart;
        resp[1] = TypeJoinResp;
        resp[2] = accepted ? (byte)0x01 : (byte)0x00;
        resp[3] = nodeId;

        ushort crc = Crc16Ccitt(resp.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt16LittleEndian(resp.AsSpan(4), crc);
        resp[6] = 0x00;
        resp[7] = FrameStop;

        await ws.SendAsync(resp, WebSocketMessageType.Binary, true, ct);
        logger.LogInformation("[JoinWs] JOIN_RESPONSE accepted={A} nodeId={N}", accepted, nodeId);
    }

    // CRC-16/CCITT-FALSE: seed=0xFFFF, poly=0x1021, no reflection
    private static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int j = 0; j < 8; j++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}
