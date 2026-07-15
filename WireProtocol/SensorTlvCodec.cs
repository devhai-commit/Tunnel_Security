namespace WireProtocol
{
    /// <summary>
    /// Mã hóa/giải mã danh sách <see cref="SensorTlvEntry"/> bên trong Payload của khung 0xA7.
    /// Mỗi entry: Header(2 byte: type + seq) | Length(2 byte, uint16 LE) | Value(Length byte).
    /// </summary>
    public static class SensorTlvCodec
    {
        public static byte[] Encode(IEnumerable<SensorTlvEntry> entries)
        {
            using var stream = new MemoryStream();

            foreach (var entry in entries)
            {
                var length = (ushort)entry.Value.Length;
                stream.WriteByte(entry.TypeByte);
                stream.WriteByte(entry.Seq);
                stream.WriteByte((byte)(length & 0xFF));
                stream.WriteByte((byte)((length >> 8) & 0xFF));
                stream.Write(entry.Value, 0, entry.Value.Length);
            }

            return stream.ToArray();
        }

        public static IReadOnlyList<SensorTlvEntry> Parse(ReadOnlySpan<byte> payload)
        {
            var entries = new List<SensorTlvEntry>();
            var offset = 0;

            while (offset + 4 <= payload.Length)
            {
                var typeByte = payload[offset];
                var seq = payload[offset + 1];
                var length = (ushort)(payload[offset + 2] | (payload[offset + 3] << 8));
                offset += 4;

                if (offset + length > payload.Length)
                {
                    break; // Entry bị cắt cụt — bỏ phần còn lại thay vì throw, không làm sập subscriber loop.
                }

                entries.Add(new SensorTlvEntry(typeByte, seq, payload.Slice(offset, length).ToArray()));
                offset += length;
            }

            return entries;
        }
    }
}
