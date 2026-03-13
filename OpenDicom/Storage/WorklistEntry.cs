using FellowOakDicom;
using OpenDicom.Gdt;

namespace OpenDicom.Storage;

/// <summary>
/// Repräsentiert einen Worklist-Eintrag, der aus einem GDT-6301-Datensatz erzeugt wurde.
/// </summary>
public sealed class WorklistEntry
{
    public WorklistEntry(GdtRecord gdt, string defaultModality)
    {
        PatientId = gdt.PatientId;
        PatientName = gdt.DicomPatientName;
        PatientBirthDate = gdt.DicomBirthDate;
        PatientSex = gdt.DicomSex;
        Modality = defaultModality == "*" ? "OT" : defaultModality;
        AccessionNumber = GenerateAccessionNumber();
        StudyInstanceUid = DicomUID.Generate().UID;
        ScheduledDateTime = gdt.ExaminationDate?.Date ?? DateTime.Today;
        CreatedAt = DateTime.UtcNow;
        SourceGdtRecord = gdt;
    }

    // ---- Patienten-Daten ----
    public string PatientId { get; }
    public string PatientName { get; }
    public string PatientBirthDate { get; }
    public string PatientSex { get; }

    // ---- Untersuchungs-Daten ----
    public string AccessionNumber { get; }
    public string StudyInstanceUid { get; }
    public string Modality { get; }
    public DateTime ScheduledDateTime { get; }

    // ---- Verwaltung ----
    public DateTime CreatedAt { get; }
    public bool IsCompleted { get; private set; }
    public string? CompletedDicomFilePath { get; private set; }

    /// <summary>Originaler GDT-Datensatz (für GDT-Antwortschreibung).</summary>
    public GdtRecord SourceGdtRecord { get; }

    public void MarkCompleted(string dicomFilePath)
    {
        IsCompleted = true;
        CompletedDicomFilePath = dicomFilePath;
    }

    /// <summary>
    /// Erstellt das DICOM-Dataset für eine C-FIND-Worklist-Antwort.
    /// </summary>
    public DicomDataset ToWorklistDataset(string aeTitle)
    {
        string dateStr = ScheduledDateTime.ToString("yyyyMMdd");
        string timeStr = ScheduledDateTime.ToString("HHmmss");

        var spss = new DicomDataset
        {
            { DicomTag.ScheduledStationAETitle, aeTitle },
            { DicomTag.ScheduledProcedureStepStartDate, dateStr },
            { DicomTag.ScheduledProcedureStepStartTime, timeStr },
            { DicomTag.Modality, Modality },
            { DicomTag.ScheduledProcedureStepID, AccessionNumber },
            { DicomTag.ScheduledProcedureStepDescription, string.Empty },
            { DicomTag.ScheduledPerformingPhysicianName, string.Empty },
        };

        return new DicomDataset
        {
            { DicomTag.SpecificCharacterSet, "ISO_IR 192" },
            { DicomTag.PatientID, PatientId },
            { DicomTag.PatientName, PatientName },
            { DicomTag.PatientBirthDate, PatientBirthDate },
            { DicomTag.PatientSex, PatientSex },
            { DicomTag.PatientWeight, string.Empty },
            { DicomTag.MedicalAlerts, string.Empty },
            { DicomTag.PregnancyStatus, (ushort)0 },
            { DicomTag.AccessionNumber, AccessionNumber },
            { DicomTag.StudyInstanceUID, StudyInstanceUid },
            { DicomTag.RequestedProcedureID, AccessionNumber },
            { DicomTag.RequestedProcedureDescription, string.Empty },
            { DicomTag.StudyDate, dateStr },
            { DicomTag.StudyTime, timeStr },
            new DicomSequence(DicomTag.ScheduledProcedureStepSequence, spss),
            new DicomSequence(DicomTag.ReferencedStudySequence),
            new DicomSequence(DicomTag.ReferencedPatientSequence),
        };
    }

    // Generiert eine 8-stellige, für den Tag eindeutige Kennnummer
    private static string GenerateAccessionNumber()
    {
        long ticks = DateTime.UtcNow.Ticks;
        // Modulo um auf 8 Stellen zu kürzen
        return (ticks % 100_000_000L).ToString("D8");
    }
}
