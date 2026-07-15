namespace WireProtocol
{
    /// <summary>
    /// Một mục trong Payload dữ liệu cảm biến: Header(2) + Length(2) + Value(Length).
    /// Header byte 1 = <see cref="SensorTypeByte"/>/<see cref="ActuatorTypeByte"/>, byte 2 = số thứ tự thiết bị.
    /// </summary>
    public sealed record SensorTlvEntry(byte TypeByte, byte Seq, byte[] Value);
}
