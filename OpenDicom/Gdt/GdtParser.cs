using System.Text;

namespace OpenDicom.Gdt;

/// <summary>
/// Parst GDT 2.1 Dateien (Satzart 6301).
///
/// Zeilenformat:
///   [3 Byte Gesamtlänge][4 Byte Feldkennung][Inhalt][CR LF]
///   Die Gesamtlänge schließt die 3 Längen-Bytes, die 4 FK-Bytes, den Inhalt und die 2 CRLF-Bytes ein.
/// </summary>
public static class GdtParser
{
    // GDT verwendet Windows-1252 (Latin-1/ANSI)
    private static readonly Encoding GdtEncoding = Encoding.GetEncoding(1252);

    public static GdtRecord Parse(string filePath)
    {
        string[] lines = File.ReadAllLines(filePath, GdtEncoding);
        return ParseLines(lines);
    }

    public static GdtRecord ParseLines(IEnumerable<string> lines)
    {
        var record = new GdtRecord();
        string? satzart = null;

        foreach (string rawLine in lines)
        {
            // Jede Zeile: erste 3 Zeichen = Länge, nächste 4 = Feldkennung, Rest = Inhalt
            if (rawLine.Length < 7) continue;

            string fk = rawLine.Substring(3, 4);
            string value = rawLine.Length > 7 ? rawLine[7..].TrimEnd('\r', '\n') : string.Empty;

            switch (fk)
            {
                case "8000":
                    satzart = value;
                    break;
                case "3000":
                    record.PatientId = value;
                    break;
                case "3101":
                    record.PatientSurname = value;
                    break;
                case "3102":
                    record.PatientFirstName = value;
                    break;
                case "3103":
                    record.PatientBirthDateRaw = value;
                    break;
                case "3110":
                    record.PatientSex = value;
                    break;
                case "6200":
                    record.ExaminationDateRaw = value;
                    break;
            }
        }

        if (satzart != "6301")
            throw new InvalidDataException(
                $"Ungültige GDT-Satzart '{satzart}'. Erwartet: 6301.");

        if (string.IsNullOrWhiteSpace(record.PatientId))
            throw new InvalidDataException("GDT-Pflichtfeld 3000 (PatientId) fehlt oder ist leer.");

        return record;
    }
}
