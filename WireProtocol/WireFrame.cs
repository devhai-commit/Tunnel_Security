namespace WireProtocol
{
    /// <summary>Decoded logical frame — start/stop bytes and CRC are wire-format details the codec handles.</summary>
    public sealed record WireFrame(byte Command, byte NodeId, byte[] Payload);
}
