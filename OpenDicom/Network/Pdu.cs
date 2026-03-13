namespace OpenDicom.Network;

/// <summary>
/// Reads and writes DICOM Upper Layer PDUs over a NetworkStream.
/// PDU structure: [type:1][reserved:1][length:4BE][data:length]
/// </summary>
internal static class Pdu
{
    // PDU type constants
    public const byte AssociateRq  = 0x01;
    public const byte AssociateAc  = 0x02;
    public const byte AssociateRj  = 0x03;
    public const byte PData        = 0x04;
    public const byte ReleaseRq    = 0x05;
    public const byte ReleaseRp    = 0x06;
    public const byte Abort        = 0x07;

    private const int MaxPduSize = 16 * 1024 * 1024; // 16 MB hard limit

    /// <summary>
    /// Read one PDU. Returns null on clean EOF.
    /// Throws IOException / InvalidDataException on protocol errors.
    /// </summary>
    public static async Task<(byte Type, byte[] Data)?> ReadAsync(
        Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[6];
        int read = await ReadExactAsync(stream, header, 0, 6, ct);
        if (read == 0) return null; // EOF

        byte type     = header[0];
        // header[1] reserved
        uint length   = (uint)((header[2] << 24) | (header[3] << 16) |
                                (header[4] << 8)  |  header[5]);

        if (length > MaxPduSize)
            throw new InvalidDataException($"PDU length {length} exceeds maximum {MaxPduSize}");

        byte[] data = length > 0 ? new byte[length] : Array.Empty<byte>();
        if (length > 0)
            await ReadExactAsync(stream, data, 0, (int)length, ct);

        return (type, data);
    }

    /// <summary>Write one PDU.</summary>
    public static async Task WriteAsync(Stream stream, byte type, byte[] data,
        CancellationToken ct = default)
    {
        int total = 6 + data.Length;
        byte[] buf = new byte[total];
        buf[0] = type;
        buf[1] = 0; // reserved
        uint len = (uint)data.Length;
        buf[2] = (byte)(len >> 24);
        buf[3] = (byte)(len >> 16);
        buf[4] = (byte)(len >> 8);
        buf[5] = (byte)(len);
        Array.Copy(data, 0, buf, 6, data.Length);
        await stream.WriteAsync(buf.AsMemory(0, total), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Read P-DATA-TF variable items from a PData PDU body.</summary>
    public static IEnumerable<(byte PresentationContextId, byte[] Fragment, bool IsCommand, bool IsLast)>
        ParsePDataItems(byte[] pduData)
    {
        int pos = 0;
        while (pos + 6 <= pduData.Length)
        {
            uint itemLength = (uint)((pduData[pos]     << 24) |
                                     (pduData[pos + 1] << 16) |
                                     (pduData[pos + 2] << 8)  |
                                      pduData[pos + 3]);
            pos += 4;
            if (pos + itemLength > pduData.Length) break;

            byte pcId      = pduData[pos];
            byte mhByte    = pduData[pos + 1];
            bool isCommand = (mhByte & 0x01) == 0x01;
            bool isLast    = (mhByte & 0x02) == 0x02;

            int fragLen = (int)itemLength - 2;
            byte[] fragment = new byte[fragLen];
            Array.Copy(pduData, pos + 2, fragment, 0, fragLen);
            pos += (int)itemLength;

            yield return (pcId, fragment, isCommand, isLast);
        }
    }

    /// <summary>Build a P-DATA-TF PDU for a single presentation context fragment.</summary>
    public static byte[] BuildPDataItem(byte pcId, byte[] dimseBytes, bool isCommand, bool isLast)
    {
        // item = [length:4BE][pcId:1][header:1][data:N]
        int itemLen = 2 + dimseBytes.Length;
        byte[] buf  = new byte[4 + itemLen];
        buf[0] = (byte)(itemLen >> 24);
        buf[1] = (byte)(itemLen >> 16);
        buf[2] = (byte)(itemLen >> 8);
        buf[3] = (byte)(itemLen);
        buf[4] = pcId;
        buf[5] = (byte)((isCommand ? 0x01 : 0x00) | (isLast ? 0x02 : 0x00));
        Array.Copy(dimseBytes, 0, buf, 6, dimseBytes.Length);
        return buf;
    }

    // ── Internal helpers ─────────────────────────────────────────────────

    private static async Task<int> ReadExactAsync(Stream s, byte[] buf, int offset,
        int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(offset + totalRead, count - totalRead), ct);
            if (n == 0) return totalRead; // EOF
            totalRead += n;
        }
        return totalRead;
    }
}
