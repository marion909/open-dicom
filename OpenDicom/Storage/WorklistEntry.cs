using OpenDicom.DicomCore;
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
        StudyInstanceUid = DicomUidGenerator.Generate();
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

        var spss = new DicomDataset();
        spss.Add(DicomTag.ScheduledStationAETitle,         DicomVR.AE, aeTitle);
        spss.Add(DicomTag.ScheduledProcedureStepStartDate, DicomVR.DA, dateStr);
        spss.Add(DicomTag.ScheduledProcedureStepStartTime, DicomVR.TM, timeStr);
        spss.Add(DicomTag.Modality,                        DicomVR.CS, Modality);
        spss.Add(DicomTag.ScheduledProcedureStepID,        DicomVR.SH, AccessionNumber);
        spss.Add(DicomTag.ScheduledProcedureStepDescription, DicomVR.LO, string.Empty);
        spss.Add(DicomTag.ScheduledPerformingPhysicianName,  DicomVR.PN, string.Empty);

        var ds = new DicomDataset();
        ds.Add(DicomTag.SpecificCharacterSet,           DicomVR.CS, "ISO_IR 192");
        ds.Add(DicomTag.PatientID,                      DicomVR.LO, PatientId);
        ds.Add(DicomTag.PatientName,                    DicomVR.PN, PatientName);
        ds.Add(DicomTag.PatientBirthDate,               DicomVR.DA, PatientBirthDate);
        ds.Add(DicomTag.PatientSex,                     DicomVR.CS, PatientSex);
        ds.Add(DicomTag.PatientWeight,                  DicomVR.DS, string.Empty);
        ds.Add(DicomTag.MedicalAlerts,                  DicomVR.LO, string.Empty);
        ds.AddUS(DicomTag.PregnancyStatus,              0);
        ds.Add(DicomTag.AccessionNumber,                DicomVR.SH, AccessionNumber);
        ds.Add(DicomTag.StudyInstanceUID,               DicomVR.UI, StudyInstanceUid);
        ds.Add(DicomTag.RequestedProcedureID,           DicomVR.SH, AccessionNumber);
        ds.Add(DicomTag.RequestedProcedureDescription,  DicomVR.LO, string.Empty);
        ds.Add(DicomTag.StudyDate,                      DicomVR.DA, dateStr);
        ds.Add(DicomTag.StudyTime,                      DicomVR.TM, timeStr);
        ds.AddSequence(DicomTag.ScheduledProcedureStepSequence, new[] { spss });
        ds.AddEmptySequence(DicomTag.ReferencedStudySequence);
        ds.AddEmptySequence(DicomTag.ReferencedPatientSequence);
        return ds;
    }

    // Generiert eine 8-stellige, für den Tag eindeutige Kennnummer
    private static string GenerateAccessionNumber()
    {
        long ticks = DateTime.UtcNow.Ticks;
        // Modulo um auf 8 Stellen zu kürzen
        return (ticks % 100_000_000L).ToString("D8");
    }
}
