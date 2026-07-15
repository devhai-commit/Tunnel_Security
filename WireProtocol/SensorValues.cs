using System.Buffers.Binary;

namespace WireProtocol
{
    /// <summary>Value của cảm biến ánh sáng: 4 byte float, đơn vị lx.</summary>
    public readonly record struct LightSensorValue(float Lux)
    {
        public byte[] ToBytes()
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(bytes, Lux);
            return bytes;
        }

        public static LightSensorValue FromBytes(ReadOnlySpan<byte> data) =>
            new(BinaryPrimitives.ReadSingleLittleEndian(data));
    }

    /// <summary>Value của cảm biến mực nước: 4 byte float, đơn vị m (độ sâu).</summary>
    public readonly record struct WaterLevelValue(float DepthMeters)
    {
        public byte[] ToBytes()
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(bytes, DepthMeters);
            return bytes;
        }

        public static WaterLevelValue FromBytes(ReadOnlySpan<byte> data) =>
            new(BinaryPrimitives.ReadSingleLittleEndian(data));
    }

    /// <summary>Value của cảm biến nhiệt độ/độ ẩm: 8 byte = 2 float (°C, %).</summary>
    public readonly record struct TemperatureHumidityValue(float TemperatureC, float HumidityPercent)
    {
        public byte[] ToBytes()
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0, 4), TemperatureC);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4, 4), HumidityPercent);
            return bytes;
        }

        public static TemperatureHumidityValue FromBytes(ReadOnlySpan<byte> data) => new(
            BinaryPrimitives.ReadSingleLittleEndian(data[..4]),
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(4, 4)));
    }

    /// <summary>Một đối tượng radar phát hiện được: x, y, z (m), vận tốc (m/s), cự ly (m), xác suất báo động lầm (%).</summary>
    public readonly record struct RadarObject(
        float X, float Y, float Z, float VelocityMps, float DistanceM, float FalseAlarmProbabilityPercent);

    /// <summary>Value của cảm biến radar: 1 byte số đối tượng (n) + n * 24 byte (6 float mỗi đối tượng).</summary>
    public sealed record RadarValue(IReadOnlyList<RadarObject> Objects)
    {
        private const int BytesPerObject = 24;

        public byte[] ToBytes()
        {
            var bytes = new byte[1 + Objects.Count * BytesPerObject];
            bytes[0] = (byte)Objects.Count;

            var offset = 1;
            foreach (var obj in Objects)
            {
                WriteFloat(bytes, ref offset, obj.X);
                WriteFloat(bytes, ref offset, obj.Y);
                WriteFloat(bytes, ref offset, obj.Z);
                WriteFloat(bytes, ref offset, obj.VelocityMps);
                WriteFloat(bytes, ref offset, obj.DistanceM);
                WriteFloat(bytes, ref offset, obj.FalseAlarmProbabilityPercent);
            }

            return bytes;
        }

        public static RadarValue FromBytes(ReadOnlySpan<byte> data)
        {
            if (data.Length < 1) return new RadarValue(Array.Empty<RadarObject>());

            var count = data[0];
            var objects = new List<RadarObject>(count);
            var offset = 1;

            for (var i = 0; i < count && offset + BytesPerObject <= data.Length; i++)
            {
                var x = ReadFloat(data, ref offset);
                var y = ReadFloat(data, ref offset);
                var z = ReadFloat(data, ref offset);
                var velocity = ReadFloat(data, ref offset);
                var distance = ReadFloat(data, ref offset);
                var falseAlarmProbability = ReadFloat(data, ref offset);
                objects.Add(new RadarObject(x, y, z, velocity, distance, falseAlarmProbability));
            }

            return new RadarValue(objects);
        }

        private static void WriteFloat(byte[] bytes, ref int offset, float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset, 4), value);
            offset += 4;
        }

        private static float ReadFloat(ReadOnlySpan<byte> data, ref int offset)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset, 4));
            offset += 4;
            return value;
        }
    }
}
