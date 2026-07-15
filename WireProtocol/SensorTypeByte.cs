namespace WireProtocol
{
    /// <summary>
    /// Header byte 1 khi 0x01-0x7F ("Cấu trúc dữ liệu của các cảm biến, đối tượng điều khiển").
    /// </summary>
    public enum SensorTypeByte : byte
    {
        Light = 0x01,
        WaterLevel = 0x02,
        TemperatureHumidity = 0x03,
        Radar = 0x04
    }

    /// <summary>Header byte 1 khi 0x80-0xFF (thiết bị ngoại vi, không phải cảm biến).</summary>
    public enum ActuatorTypeByte : byte
    {
        Speaker = 0x80,
        Light = 0x81,
        DoorLock = 0x82,
        Pump = 0x83
    }
}
