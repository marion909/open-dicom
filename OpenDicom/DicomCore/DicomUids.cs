namespace OpenDicom.DicomCore;

/// <summary>
/// Well-known SOP Class UIDs, Transfer Syntax UIDs, and other constants
/// needed for the DICOM network layer.
/// </summary>
public static class DicomUids
{
    // ── Transfer Syntaxes ────────────────────────────────────────────────
    public const string ExplicitVRLittleEndian = "1.2.840.10008.1.2.1";
    public const string ImplicitVRLittleEndian = "1.2.840.10008.1.2";

    // ── Meta / Verification ──────────────────────────────────────────────
    public const string VerificationSOPClass           = "1.2.840.10008.1.1";
    public const string ModalityWorklistFind           = "1.2.840.10008.5.1.4.31";

    // ── Implementation identifiers ───────────────────────────────────────
    public const string ImplementationClassUID         = "1.3.999.1.1.1";
    public const string ImplementationVersionName      = "OPENDICOM_1.0";

    // ── Common Storage SOP Classes ───────────────────────────────────────
    // Accept any SOP class whose UID starts with the Storage prefix
    // Any SOP class whose UID starts with "1.2.840.10008.5.1.4" is a storage class
    public static bool IsStorageSopClass(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return false;
        // covers all standard Storage Service Classes
        return uid.StartsWith("1.2.840.10008.5.1.4", StringComparison.Ordinal)
            && uid != ModalityWorklistFind;
    }
}
