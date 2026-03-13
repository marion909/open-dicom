using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDicom.DicomCore;
using OpenDicom.Gdt;

namespace OpenDicom.Storage;

/// <summary>
/// Thread-sicherer In-Memory-Speicher für Worklist-Einträge.
/// Einträge werden nach Ablauf der konfigurierten TTL automatisch bereinigt.
/// </summary>
public sealed class WorklistStore
{
    // Key = AccessionNumber
    private readonly ConcurrentDictionary<string, WorklistEntry> _entries = new();
    private readonly AppSettings _settings;
    private readonly ILogger<WorklistStore> _logger;
    private readonly Timer _cleanupTimer;

    public WorklistStore(IOptions<AppSettings> settings, ILogger<WorklistStore> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        // Bereinigung alle 15 Minuten
        _cleanupTimer = new Timer(Cleanup, null,
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(15));
    }

    /// <summary>Legt einen neuen Worklist-Eintrag aus einem GDT-Datensatz an.</summary>
    public WorklistEntry AddEntry(GdtRecord record)
    {
        var entry = new WorklistEntry(record, _settings.Worklist.DefaultModality);
        _entries[entry.AccessionNumber] = entry;
        _logger.LogInformation(
            "Worklist entry added: AccessionNumber={Acc} PatientId={PId} PatientName={Name}",
            entry.AccessionNumber, entry.PatientId, entry.PatientName);
        return entry;
    }

    /// <summary>
    /// Sucht Einträge anhand eines DICOM C-FIND-Query-Datasets.
    /// Unterstützt Wildcard-Matching (* und ?) auf PatientID und PatientName.
    /// </summary>
    public IEnumerable<WorklistEntry> FindEntries(DicomDataset query)
    {
        string? queryPatientId    = query.GetString(DicomTag.PatientID)?.Trim();
        string? queryPatientName  = query.GetString(DicomTag.PatientName)?.Trim();
        string? queryAccession    = query.GetString(DicomTag.AccessionNumber)?.Trim();

        // Date from ScheduledProcedureStepSequence
        string? queryDate = null;
        if (query.TryGetSequence(DicomTag.ScheduledProcedureStepSequence, out var items)
            && items.Count > 0)
        {
            queryDate = items[0].GetString(DicomTag.ScheduledProcedureStepStartDate)?.Trim();
        }

        return _entries.Values.Where(e =>
            !e.IsCompleted &&
            MatchesWildcard(e.PatientId, queryPatientId) &&
            MatchesWildcard(e.PatientName, queryPatientName) &&
            MatchesWildcard(e.AccessionNumber, queryAccession) &&
            MatchesDateRange(e.ScheduledDateTime, queryDate));
    }

    /// <summary>Sucht einen Eintrag über AccessionNumber (für C-STORE-Abgleich).</summary>
    public WorklistEntry? FindByAccession(string accessionNumber)
        => _entries.TryGetValue(accessionNumber, out var entry) ? entry : null;

    /// <summary>Sucht einen Eintrag anhand der PatientID (Fallback wenn keine AccessionNumber).</summary>
    public WorklistEntry? FindByPatientId(string patientId)
        => _entries.Values
            .Where(e => e.PatientId == patientId && !e.IsCompleted)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefault();

    private static string? GetQueryValue(DicomDataset dataset, DicomTag tag)
    {
        string? val = dataset.GetString(tag);
        return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
    }

    private static bool MatchesWildcard(string value, string? pattern)
    {
        if (pattern == null || pattern == "*") return true;
        // Einfaches Wildcard-Matching (* = beliebig viele, ? = ein Zeichen)
        string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(
            value, regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool MatchesDateRange(DateTime scheduledDate, string? queryDate)
    {
        if (string.IsNullOrWhiteSpace(queryDate)) return true;

        // DICOM-Datumsbereich: "yyyyMMdd" oder "yyyyMMdd-yyyyMMdd" oder "-yyyyMMdd" oder "yyyyMMdd-"
        if (queryDate.Contains('-'))
        {
            string[] parts = queryDate.Split('-');
            bool fromOk = string.IsNullOrEmpty(parts[0]) ||
                !DateTime.TryParseExact(parts[0], "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime from) ||
                scheduledDate.Date >= from.Date;

            bool toOk = parts.Length < 2 || string.IsNullOrEmpty(parts[1]) ||
                !DateTime.TryParseExact(parts[1], "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime to) ||
                scheduledDate.Date <= to.Date;

            return fromOk && toOk;
        }

        if (DateTime.TryParseExact(queryDate, "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out DateTime exact))
            return scheduledDate.Date == exact.Date;

        return true;
    }

    private void Cleanup(object? _)
    {
        TimeSpan ttl = TimeSpan.FromHours(_settings.Worklist.EntryTtlHours);
        DateTime cutoff = DateTime.UtcNow - ttl;
        int removed = 0;
        foreach (var kvp in _entries)
        {
            if (kvp.Value.CreatedAt < cutoff)
            {
                _entries.TryRemove(kvp.Key, out WorklistEntry? _);
                removed++;
            }
        }
        if (removed > 0)
            _logger.LogInformation("Worklist cleanup: {Count} expired entries removed.", removed);
    }
}
