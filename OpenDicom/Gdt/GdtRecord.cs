namespace OpenDicom.Gdt;

/// <summary>
/// Repräsentiert einen eingehenden GDT 2.1 Datensatz (Satzart 6301 – Anforderung einer neuen Untersuchung).
/// </summary>
public sealed class GdtRecord
{
    /// <summary>Feldkennung 3000 – Patientennummer</summary>
    public string PatientId { get; set; } = string.Empty;

    /// <summary>Feldkennung 3101 – Patientenname (Nachname)</summary>
    public string PatientSurname { get; set; } = string.Empty;

    /// <summary>Feldkennung 3102 – Vorname</summary>
    public string PatientFirstName { get; set; } = string.Empty;

    /// <summary>Feldkennung 3103 – Geburtsdatum im Format TTMMJJJJ</summary>
    public string PatientBirthDateRaw { get; set; } = string.Empty;

    /// <summary>Feldkennung 3110 – Geschlecht: M / W / D</summary>
    public string PatientSex { get; set; } = string.Empty;

    /// <summary>Feldkennung 6200 – Untersuchungsdatum im Format TTMMJJJJ</summary>
    public string ExaminationDateRaw { get; set; } = string.Empty;

    // ---- Berechnete Felder ----

    public DateTime? PatientBirthDate => ParseGdtDate(PatientBirthDateRaw);
    public DateTime? ExaminationDate => ParseGdtDate(ExaminationDateRaw);

    /// <summary>Vollständiger DICOM-Patientenname im Format "Nachname^Vorname"</summary>
    public string DicomPatientName =>
        string.IsNullOrWhiteSpace(PatientFirstName)
            ? PatientSurname
            : $"{PatientSurname}^{PatientFirstName}";

    /// <summary>Geburtsdatum als DICOM DA-String (yyyyMMdd)</summary>
    public string DicomBirthDate => PatientBirthDate?.ToString("yyyyMMdd") ?? string.Empty;

    /// <summary>Geschlecht als DICOM CS-Wert (M / F / O)</summary>
    public string DicomSex => PatientSex.ToUpperInvariant() switch
    {
        "M" => "M",
        "W" => "F",
        "D" => "O",
        _ => "O"
    };

    private static DateTime? ParseGdtDate(string raw)
    {
        if (raw.Length != 8) return null;
        if (DateTime.TryParseExact(raw, "ddMMyyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out DateTime dt))
            return dt;
        return null;
    }
}
