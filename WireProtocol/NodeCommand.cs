namespace WireProtocol
{
    /// <summary>Mã lệnh — mục III "GIAO THỨC TRUYỀN TIN NODE-GATEWAY".</summary>
    public enum NodeCommand : byte
    {
        JoinRequest = 0xA1,
        ReadConfig = 0xA2,
        WriteConfig = 0xA3,
        KeepAlive = 0xA4,
        Reset = 0xA5,
        Control = 0xA6,
        SensorData = 0xA7
    }
}
