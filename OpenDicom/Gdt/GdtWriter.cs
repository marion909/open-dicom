using System.Text;

namespace OpenDicom.Gdt;

/// <summary>
/// Schreibt GDT 2.1 Antwortdateien (Satzart 6310 – Übertragung von Befunddaten).
/// </summary>
public static class GdtWriter
{
    private static readonly Encoding GdtEncoding = Encoding.GetEncoding(1252);

    /// <summary>
    ///  Schreibt eine GDT-6310-Datei in den angegebenen Ausgabeordner.
    ///  Dateiname: YYYYMMDD_HHmmss_{PatientId}.gdt
    /// </summary>
    /// <param name="record">Originaler GDT-Eingabedatensatz (6301).</param>
    /// <param name="dicomFilePath">Pfad zur gespeicherten DICOM-Datei.</param>
    /// <param name="outputFolder">Zielordner für die GDT-Antwortdatei.</param>
    /// <param name="aeTitle">AE-Title des Servers (wird als GerätName genutzt).</param>
    /// <returns>Pfad zur geschriebenen GDT-Datei.</returns>
    public static string Write(
        GdtRecord record,
        string dicomFilePath,
        string outputFolder,
        string aeTitle)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string safePatientId = SanitizeFilename(record.PatientId);
        string fileName = $"{timestamp}_{safePatientId}.gdt";
        string outputPath = Path.Combine(outputFolder, fileName);

        var fields = BuildFieldList(record, dicomFilePath, aeTitle);
        WriteGdtFile(outputPath, fields);
        return outputPath;
    }

    private static List<(string Fk, string Value)> BuildFieldList(
        GdtRecord record, string dicomFilePath, string aeTitle)
    {
        string examinationDate = DateTime.Now.ToString("ddMMyyyy");
        string examinationTime = DateTime.Now.ToString("HHmmss");

        var fields = new List<(string Fk, string Value)>
        {
            ("8000", "6310"),                         // Satzart
            ("8100", string.Empty),                   // Zeilenanzahl – wird unten gesetzt
            ("3000", record.PatientId),               // Patientennummer
            ("3101", record.PatientSurname),          // Nachname
            ("3102", record.PatientFirstName),        // Vorname
            ("3103", record.PatientBirthDateRaw),     // Geburtsdatum (TTMMJJJJ)
            ("6200", examinationDate),                // Untersuchungsdatum (TTMMJJJJ)
            ("6210", examinationTime),                // Untersuchungszeit (HHMMSS)
            ("8202", aeTitle),                        // Gerätename / AE-Title
            ("8132", dicomFilePath),                  // Anbindung externe DICOM-Datei
        };

        // 8100 = Gesamtzahl der Zeilen im Datensatz (inkl. 8000 und 8100)
        fields[1] = ("8100", fields.Count.ToString());
        return fields;
    }

    /// <summary>
    /// Schreibt den GDT-Datensatz zeilenweise mit explizitem CR LF (0x0D 0x0A).
    ///
    /// Zeilenformat gemäß GDT 2.1:
    ///   [3 Byte Gesamtlänge][4 Byte FK][Inhalt (Win-1252)][CR LF]
    ///   Länge (Bytes) = 3 + 4 + GetByteCount(Inhalt) + 2
    /// </summary>
    private static void WriteGdtFile(string path, List<(string Fk, string Value)> fields)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        foreach (var (fk, value) in fields)
        {
            int byteLen  = GdtEncoding.GetByteCount(value);
            int totalLen = 3 + 4 + byteLen + 2;          // +2 = CR LF
            byte[] lineBytes = GdtEncoding.GetBytes($"{totalLen:D3}{fk}{value}");
            fs.Write(lineBytes, 0, lineBytes.Length);
            fs.WriteByte(0x0D);   // CR
            fs.WriteByte(0x0A);   // LF
        }

    }

    private static string SanitizeFilename(string input)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(input.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
