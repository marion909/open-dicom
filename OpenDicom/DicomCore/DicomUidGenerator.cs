namespace OpenDicom.DicomCore;

/// <summary>
/// Generates DICOM UIDs based on UUID (ITU-T X.667 / ISO/IEC 9834-8).
/// Format: 2.25.{decimal-representation-of-128-bit-UUID}
/// Compatible with fo-dicom's DicomUID.Generate() and dcm4che.
/// </summary>
public static class DicomUidGenerator
{
    public static string Generate()
    {
        // Convert 128-bit Guid to unsigned decimal (big integer arithmetic)
        byte[] bytes = Guid.NewGuid().ToByteArray();

        // Guid bytes are mixed-endian; convert to big-endian byte array for
        // a straightforward big-decimal conversion
        // Parts as per RFC 4122 / .NET Guid layout
        uint  a = BitConverter.ToUInt32(bytes, 0);
        ushort b = BitConverter.ToUInt16(bytes, 4);
        ushort c = BitConverter.ToUInt16(bytes, 6);
        // bytes 8-15 are already big-endian
        ulong hi = ((ulong)a << 32) | ((ulong)b << 16) | c;
        ulong lo = 0;
        for (int i = 8; i < 16; i++) lo = (lo << 8) | bytes[i];

        string decimal128 = ToDecimal128(hi, lo);
        return "2.25." + decimal128;
    }

    // Converts a 128-bit value (stored as two 64-bit halves) to a decimal string
    private static string ToDecimal128(ulong hi, ulong lo)
    {
        if (hi == 0) return lo.ToString();

        // We do long-division by 10 on the 128-bit number
        char[] buf = new char[39]; // max 39 decimal digits for 128-bit
        int pos = buf.Length;

        while (hi != 0 || lo != 0)
        {
            (hi, lo) = DivRem128(hi, lo, 10, out uint rem);
            buf[--pos] = (char)('0' + rem);
        }

        return new string(buf, pos, buf.Length - pos);
    }

    private static (ulong newHi, ulong newLo) DivRem128(ulong hi, ulong lo, ulong divisor, out uint remainder)
    {
        ulong remH = hi % divisor;
        ulong newHi = hi / divisor;

        // Combine remainder from hi with lo for second division
        // lo128 = remH * 2^64 + lo
        // We split into high 64 bits (remH) and low 64 bits (lo)
        ulong mid  = (remH << 32) | (lo >> 32);
        ulong qMid = mid / divisor;
        ulong rMid = mid % divisor;

        ulong low  = (rMid << 32) | (lo & 0xFFFFFFFF);
        ulong qLow = low / divisor;

        remainder = (uint)(low % divisor);
        ulong newLo = (qMid << 32) | qLow;
        return (newHi, newLo);
    }
}
