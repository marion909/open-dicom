namespace OpenDicom.DicomCore;

/// <summary>DICOM tag = (group, element) pair.</summary>
public readonly struct DicomTag : IEquatable<DicomTag>
{
    public readonly ushort Group;
    public readonly ushort Element;

    public DicomTag(ushort group, ushort element)
    {
        Group   = group;
        Element = element;
    }

    public bool Equals(DicomTag other) => Group == other.Group && Element == other.Element;
    public override bool Equals(object? obj) => obj is DicomTag t && Equals(t);
    public override int GetHashCode() => HashCode.Combine(Group, Element);
    public static bool operator ==(DicomTag a, DicomTag b) => a.Equals(b);
    public static bool operator !=(DicomTag a, DicomTag b) => !a.Equals(b);
    public override string ToString() => $"({Group:X4},{Element:X4})";

    // ── DIMSE Command Group (0000) ─────────────────────────────────────
    public static readonly DicomTag AffectedSOPClassUID             = new(0x0000, 0x0002);
    public static readonly DicomTag CommandField                    = new(0x0000, 0x0100);
    public static readonly DicomTag MessageID                       = new(0x0000, 0x0110);
    public static readonly DicomTag MessageIDBeingRespondedTo       = new(0x0000, 0x0120);
    public static readonly DicomTag CommandDataSetType              = new(0x0000, 0x0800);
    public static readonly DicomTag Status                          = new(0x0000, 0x0900);
    public static readonly DicomTag AffectedSOPInstanceUID          = new(0x0000, 0x1000);

    // ── Meta (0002) ──────────────────────────────────────────────────────
    public static readonly DicomTag FileMetaInformationGroupLength  = new(0x0002, 0x0000);
    public static readonly DicomTag FileMetaInformationVersion      = new(0x0002, 0x0001);
    public static readonly DicomTag MediaStorageSOPClassUID         = new(0x0002, 0x0002);
    public static readonly DicomTag MediaStorageSOPInstanceUID      = new(0x0002, 0x0003);
    public static readonly DicomTag TransferSyntaxUID               = new(0x0002, 0x0010);
    public static readonly DicomTag ImplementationClassUID          = new(0x0002, 0x0012);
    public static readonly DicomTag ImplementationVersionName       = new(0x0002, 0x0013);

    // ── Identification ───────────────────────────────────────────────────
    public static readonly DicomTag SpecificCharacterSet            = new(0x0008, 0x0005);
    public static readonly DicomTag SOPClassUID                     = new(0x0008, 0x0016);
    public static readonly DicomTag SOPInstanceUID                  = new(0x0008, 0x0018);
    public static readonly DicomTag StudyDate                       = new(0x0008, 0x0020);
    public static readonly DicomTag SeriesDate                      = new(0x0008, 0x0021);
    public static readonly DicomTag ContentDate                     = new(0x0008, 0x0023);
    public static readonly DicomTag StudyTime                       = new(0x0008, 0x0030);
    public static readonly DicomTag SeriesTime                      = new(0x0008, 0x0031);
    public static readonly DicomTag ContentTime                     = new(0x0008, 0x0033);
    public static readonly DicomTag Modality                        = new(0x0008, 0x0060);

    // ── Patient ──────────────────────────────────────────────────────────
    public static readonly DicomTag PatientName                     = new(0x0010, 0x0010);
    public static readonly DicomTag PatientID                       = new(0x0010, 0x0020);
    public static readonly DicomTag PatientBirthDate                = new(0x0010, 0x0030);
    public static readonly DicomTag PatientSex                      = new(0x0010, 0x0040);
    public static readonly DicomTag PatientWeight                   = new(0x0010, 0x1030);
    public static readonly DicomTag MedicalAlerts                   = new(0x0010, 0x2000);
    public static readonly DicomTag PregnancyStatus                 = new(0x0010, 0x21C0);

    // ── Study ────────────────────────────────────────────────────────────
    public static readonly DicomTag StudyInstanceUID                = new(0x0020, 0x000D);
    public static readonly DicomTag SeriesInstanceUID               = new(0x0020, 0x000E);
    public static readonly DicomTag InstanceNumber                  = new(0x0020, 0x0013);

    // ── Worklist / Requested Procedure ──────────────────────────────────
    public static readonly DicomTag AccessionNumber                 = new(0x0008, 0x0050);
    public static readonly DicomTag RequestedProcedureID            = new(0x0040, 0x1001);
    public static readonly DicomTag RequestedProcedureDescription   = new(0x0032, 0x1060);
    public static readonly DicomTag ReferencedStudySequence         = new(0x0008, 0x1110);
    public static readonly DicomTag ReferencedPatientSequence       = new(0x0008, 0x1120);
    public static readonly DicomTag ScheduledProcedureStepSequence  = new(0x0040, 0x0100);
    public static readonly DicomTag ScheduledStationAETitle         = new(0x0040, 0x0001);
    public static readonly DicomTag ScheduledProcedureStepStartDate = new(0x0040, 0x0002);
    public static readonly DicomTag ScheduledProcedureStepStartTime = new(0x0040, 0x0003);
    public static readonly DicomTag ScheduledProcedureStepID        = new(0x0040, 0x0009);
    public static readonly DicomTag ScheduledProcedureStepDescription = new(0x0040, 0x0007);
    public static readonly DicomTag ScheduledPerformingPhysicianName  = new(0x0040, 0x0006);

    // ── Image ────────────────────────────────────────────────────────────
    public static readonly DicomTag SamplesPerPixel                 = new(0x0028, 0x0002);
    public static readonly DicomTag PhotometricInterpretation       = new(0x0028, 0x0004);
    public static readonly DicomTag PlanarConfiguration             = new(0x0028, 0x0006);
    public static readonly DicomTag Rows                            = new(0x0028, 0x0010);
    public static readonly DicomTag Columns                         = new(0x0028, 0x0011);
    public static readonly DicomTag BitsAllocated                   = new(0x0028, 0x0100);
    public static readonly DicomTag BitsStored                      = new(0x0028, 0x0101);
    public static readonly DicomTag HighBit                         = new(0x0028, 0x0102);
    public static readonly DicomTag PixelRepresentation             = new(0x0028, 0x0103);
    public static readonly DicomTag PixelData                       = new(0x7FE0, 0x0010);
}
