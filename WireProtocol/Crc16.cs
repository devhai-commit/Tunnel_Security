namespace WireProtocol
{
    /// <summary>
    /// CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflect). The 2 PDF specs only say
    /// "checksum các byte trước nó, 2 byte" without naming an algorithm, so this is a documented
    /// assumption — publisher and subscriber must agree on it, which is why it lives here once
    /// and both sides reference the same WireProtocol project instead of reimplementing it.
    /// </summary>
    public static class Crc16
    {
        private const ushort Polynomial = 0x1021;
        private const ushort InitialValue = 0xFFFF;

        public static ushort Compute(ReadOnlySpan<byte> data)
        {
            var crc = InitialValue;

            foreach (var b in data)
            {
                crc ^= (ushort)(b << 8);
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x8000) != 0
                        ? (ushort)((crc << 1) ^ Polynomial)
                        : (ushort)(crc << 1);
                }
            }

            return crc;
        }
    }
}
