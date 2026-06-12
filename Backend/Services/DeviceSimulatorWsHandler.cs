using System.Buffers.Binary;
using System.Net.WebSockets;

namespace Backend.Services;

/// <summary>
/// WebSocket server endpoint handler – accepts simulator connections and decodes
/// the 32-byte binary frame:
///
///  Byte  0      : START   = 0xAA
///  Byte  1      : HEADER  = 0x01
///  Bytes  2– 5  : float32 LE – Nhiệt độ  (°C)
///  Bytes  6– 9  : float32 LE – Độ ẩm    (%)
///  Bytes 10–13  : float32 LE – Ánh sáng  (lux)
///  Bytes 14–17  : float32 LE – Khoảng cách Radar (m)
///  Bytes 18–21  : float32 LE – Tốc độ Radar (m/s)
///  Bytes 22–25  : uint32  LE – Năng lượng Radar
///  Bytes 26–27  : uint16  LE – Độ cao VL53 (mm)
///  Bytes 28–29  : uint16  LE – CRC-16/CCITT-FALSE (over bytes 0–27)
///  Byte 30      : Reserved = 0x00
///  Byte 31      : STOP    = 0xBB
/// </summary>
public static class DeviceSimulatorWsHandler
{
    private const byte FrameStart = 0xAA;
    private const byte FrameStop  = 0xBB;
    private const int  FrameSize  = 32;

    public static async Task HandleAsync(
        WebSocket        ws,
        string           nodeId,
        SensorBroadcaster broadcaster,
        ILogger          logger,
        CancellationToken ct)
    {
        logger.LogInformation("[WsSim] Device connected  nodeId={NodeId}", nodeId);

        var receiveBuffer = new byte[4096];
        var frameBuffer   = new List<byte>(64);

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(receiveBuffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }

                frameBuffer.AddRange(receiveBuffer[..result.Count]);
                await DrainFramesAsync(frameBuffer, nodeId, broadcaster, logger, ct);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (WebSocketException ex)
        {
            logger.LogWarning("[WsSim] {NodeId} connection error: {Msg}", nodeId, ex.Message);
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

            logger.LogInformation("[WsSim] Device disconnected  nodeId={NodeId}", nodeId);
        }
    }

    private static async Task DrainFramesAsync(
        List<byte>        buf,
        string            nodeId,
        SensorBroadcaster broadcaster,
        ILogger           logger,
        CancellationToken ct)
    {
        while (buf.Count >= FrameSize)
        {
            // Sync to next START byte
            int startIdx = buf.IndexOf(FrameStart);
            if (startIdx < 0) { buf.Clear(); return; }
            if (startIdx > 0) buf.RemoveRange(0, startIdx);
            if (buf.Count < FrameSize) return;

            var frame = buf.GetRange(0, FrameSize).ToArray();
            buf.RemoveRange(0, FrameSize);

            if (frame[31] != FrameStop)
            {
                logger.LogDebug("[WsSim] {NodeId} bad STOP byte — frame discarded", nodeId);
                continue;
            }

            // CRC-16/CCITT-FALSE over bytes [0..27]
            ushort expectedCrc = Crc16Ccitt(frame.AsSpan(0, 28));
            ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(28));

            if (expectedCrc != receivedCrc)
            {
                logger.LogWarning("[WsSim] {NodeId} CRC mismatch expected=0x{Exp:X4} received=0x{Got:X4}",
                    nodeId, expectedCrc, receivedCrc);
                continue;
            }

            float  temp   = BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(2));
            float  hum    = BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(6));
            float  light  = BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(10));
            float  dist   = BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(14));
            float  speed  = BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(18));
            uint   energy = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(22));
            ushort height = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(26));

            logger.LogInformation(
                "[WsSim] {NodeId} frame OK  T={T:F1}°C  H={H:F1}%  L={L:F0}lx  " +
                "D={D:F2}m  S={S:F2}m/s  E={E}  Ht={Ht}mm  CRC=0x{CRC:X4}",
                nodeId, temp, hum, light, dist, speed, energy, height, receivedCrc);

            await Task.WhenAll(
                broadcaster.ProcessReadingAsync($"{nodeId}-TEMP",  temp,   ct),
                broadcaster.ProcessReadingAsync($"{nodeId}-HUM",   hum,    ct),
                broadcaster.ProcessReadingAsync($"{nodeId}-LIGHT", light,  ct),
                broadcaster.ProcessReadingAsync($"{nodeId}-RADAR", dist,   ct),
                broadcaster.ProcessReadingAsync($"{nodeId}-VIB",   speed,  ct),
                broadcaster.ProcessReadingAsync($"{nodeId}-WATER", height, ct)
            );
        }
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
