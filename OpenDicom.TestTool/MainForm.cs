using System.Drawing;
using System.Text;
using System.Windows.Forms;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenDicom.TestTool;

public sealed class MainForm : Form
{
    // ── Verbindung ──
    private readonly TextBox _txtHost   = new() { Text = "127.0.0.1", Width = 140 };
    private readonly TextBox _txtPort   = new() { Text = "11112",     Width = 70  };
    private readonly TextBox _txtServerAE  = new() { Text = "OPENDICOM", Width = 120 };
    private readonly TextBox _txtCallingAE = new() { Text = "TESTTOOL",  Width = 120 };

    // ── Pfade ──
    private readonly TextBox _txtGdtIn  = new() { Text = @"C:\OpenDicom\data\gdt_in",  Width = 320 };
    private readonly TextBox _txtGdtOut = new() { Text = @"C:\OpenDicom\data\gdt_out", Width = 320 };

    // ── Patient ──
    private readonly TextBox _txtPatientId   = new() { Width = 120 };
    private readonly TextBox _txtSurname     = new() { Text = "Testpatient", Width = 150 };
    private readonly TextBox _txtFirstname   = new() { Text = "Erika",       Width = 120 };
    private readonly TextBox _txtBirthDate   = new() { Text = "15051980",    Width = 90  };
    private readonly ComboBox _cbxSex;

    // ── Bild ──
    private readonly TextBox _txtImagePath = new() { Text = "", Width = 260,
        PlaceholderText = "(leer = Testbild 64×64)" };

    // ── Steuerung ──
    private readonly Button _btnRun    = new() { Text = "▶  Test starten", Width = 160, Height = 36 };
    private readonly Button _btnClear  = new() { Text = "Löschen",         Width = 80,  Height = 36 };
    private readonly RichTextBox _log  = new() { ReadOnly = true, BackColor = Color.FromArgb(18, 18, 24),
                                                  ForeColor = Color.White, Font = new Font("Consolas", 9f),
                                                  Dock = DockStyle.Fill, WordWrap = false };
    private readonly StatusStrip  _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Bereit." };

    private CancellationTokenSource? _cts;

    public MainForm()
    {
        // Sex-Combobox
        _cbxSex = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
        _cbxSex.Items.AddRange(new object[] { "W", "M", "D" });
        _cbxSex.SelectedIndex = 0;

        // Zufällige Patient-ID vorbelegen
        _txtPatientId.Text = $"TEST{DateTime.Now:HHmmssff}";

        Text            = "OpenDicom – Integrations-Testtool";
        MinimumSize     = new Size(820, 660);
        Size            = new Size(900, 720);
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9f);
        BackColor       = Color.FromArgb(30, 30, 36);
        ForeColor       = Color.FromArgb(220, 220, 220);

        BuildLayout();

        _btnRun.Click   += OnRunClick;
        _btnClear.Click += (_, _) => _log.Clear();
    }

    // =====================================================================
    //  Layout (rein programmatisch, kein Designer)
    // =====================================================================
    private void BuildLayout()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3, ColumnCount = 1,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(30, 30, 36),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // ── Obere Zeile: Verbindung + Pfade + Patient ──
        var topPanel = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, Dock = DockStyle.Top,
            BackColor = Color.Transparent,
        };
        topPanel.Controls.Add(GroupBox("Verbindung", BuildConnectionPanel()));
        topPanel.Controls.Add(GroupBox("Pfade", BuildPathsPanel()));
        topPanel.Controls.Add(GroupBox("Patient", BuildPatientPanel()));

        // ── Buttons ──
        StyleButton(_btnRun,   Color.FromArgb(0, 122, 204), Color.White);
        StyleButton(_btnClear, Color.FromArgb(70, 70, 80),  Color.White);
        var btnPanel = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 4),
            BackColor = Color.Transparent,
        };
        btnPanel.Controls.Add(_btnRun);
        btnPanel.Controls.Add(_btnClear);

        // ── Log ──
        var logGroup = new GroupBox
        {
            Text = "Ausgabe", Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(180, 180, 180),
            Padding = new Padding(4),
        };
        logGroup.Controls.Add(_log);

        // ── Status ──
        _status.BackColor = Color.FromArgb(20, 20, 26);
        _status.Items.Add(_statusLabel);

        outer.Controls.Add(topPanel,   0, 0);
        outer.Controls.Add(btnPanel,   0, 1);
        outer.Controls.Add(logGroup,   0, 2);

        Controls.Add(outer);
        Controls.Add(_status);
    }

    private Panel BuildConnectionPanel()
    {
        var p = AutoPanel();
        AddRow(p, 0,  "Host:",          _txtHost);
        AddRow(p, 1,  "Port:",          _txtPort);
        AddRow(p, 2,  "Server AE:",     _txtServerAE);
        AddRow(p, 3,  "Calling AE:",    _txtCallingAE);
        return p;
    }

    private Panel BuildPathsPanel()
    {
        var p = AutoPanel();
        AddRowBrowse(p, 0, "GDT Input:",  _txtGdtIn,  browse: true);
        AddRowBrowse(p, 1, "GDT Output:", _txtGdtOut, browse: false);
        AddRowFilePicker(p, 2, "Test-Bild:", _txtImagePath,
            "Bilddateien|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Alle Dateien|*.*");
        return p;
    }

    private Panel BuildPatientPanel()
    {
        var p = AutoPanel();
        AddRow(p, 0, "Patient-ID:", _txtPatientId);
        AddRow(p, 1, "Nachname:",   _txtSurname);
        AddRow(p, 2, "Vorname:",    _txtFirstname);
        AddRow(p, 3, "Geb.-Datum:", _txtBirthDate);
        AddRow(p, 4, "Geschlecht:", _cbxSex);
        return p;
    }

    // =====================================================================
    //  Test-Logik
    // =====================================================================
    private async void OnRunClick(object? sender, EventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }

        _cts = new CancellationTokenSource();
        _btnRun.Text      = "■  Abbrechen";
        StyleButton(_btnRun, Color.FromArgb(190, 50, 50), Color.White);
        _log.Clear();

        // Patient-ID für diesen Lauf aktualisieren wenn leer
        if (string.IsNullOrWhiteSpace(_txtPatientId.Text))
            _txtPatientId.Text = $"TEST{DateTime.Now:HHmmssff}";

        var cfg = new TestConfig(
            GdtInputFolder:  _txtGdtIn.Text.Trim(),
            GdtOutputFolder: _txtGdtOut.Text.Trim(),
            Host:            _txtHost.Text.Trim(),
            Port:            int.TryParse(_txtPort.Text, out int p) ? p : 11112,
            ServerAE:        _txtServerAE.Text.Trim(),
            CallingAE:       _txtCallingAE.Text.Trim(),
            PatientId:       _txtPatientId.Text.Trim(),
            Surname:         _txtSurname.Text.Trim(),
            Firstname:       _txtFirstname.Text.Trim(),
            BirthDate:       _txtBirthDate.Text.Trim(),
            Sex:             _cbxSex.Text,
            ImagePath:       _txtImagePath.Text.Trim()
        );

        try
        {
            await RunTestAsync(cfg, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("⚪ Abgebrochen.", Color.Gray);
        }
        catch (Exception ex)
        {
            Log($"✗ Unerwarteter Fehler: {ex.Message}", Color.Red);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _btnRun.Text = "▶  Test starten";
            StyleButton(_btnRun, Color.FromArgb(0, 122, 204), Color.White);
            SetStatus("Fertig.");
        }

        // Neue Patient-ID für nächsten Lauf
        _txtPatientId.Text = $"TEST{DateTime.Now:HHmmssff}";
    }

    private async Task RunTestAsync(TestConfig cfg, CancellationToken ct)
    {
        int exitCode = 0;
        string? foundAccession = null;
        string? foundStudyUid  = null;

        // ── Schritt 1: GDT schreiben ──
        LogHeader("Schritt 1/4 – GDT-Datei schreiben (Satzart 6301)");
        SetStatus("Schreibe GDT...");
        try
        {
            Directory.CreateDirectory(cfg.GdtInputFolder);
            string gdtPath = Path.Combine(cfg.GdtInputFolder, $"test_{cfg.PatientId}.gdt");
            WriteGdt6301(gdtPath, cfg);
            LogOk($"Geschrieben: {gdtPath}");
        }
        catch (Exception ex) { LogFail($"Fehler: {ex.Message}"); return; }

        // ── Schritt 2: C-FIND ──
        LogHeader("Schritt 2/4 – Worklist per C-FIND prüfen");
        SetStatus("Warte auf Worklist-Eintrag...");
        using var cfindCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cfindCts.CancelAfter(TimeSpan.FromSeconds(15));

        while (!cfindCts.IsCancellationRequested)
        {
            try
            {
                var hits = await QueryWorklistAsync(cfg, cfindCts.Token);
                if (hits.Count > 0)
                {
                    (foundAccession, foundStudyUid) = hits[0];
                    string shortUid = foundStudyUid.Length > 30 ? foundStudyUid[..30] + "…" : foundStudyUid;
                    LogOk($"Eintrag gefunden – {cfg.Surname}^{cfg.Firstname}");
                    LogOk($"AccessionNumber:  {foundAccession}");
                    LogOk($"StudyInstanceUID: {shortUid}");
                    break;
                }
                Log("  → Kein Eintrag gefunden, neuer Versuch in 1 s …", Color.DarkGray);
                await Task.Delay(1000, cfindCts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogFail($"C-FIND Fehler: {ex.Message}"); exitCode = 1; break; }
        }

        if (foundAccession == null && exitCode == 0)
        {
            LogFail("Worklist-Eintrag nicht gefunden (Timeout 15 s). Läuft der Server?");
            exitCode = 1;
        }

        // ── Schritt 3: C-STORE ──
        LogHeader("Schritt 3/4 – Test-DICOM-Bild via C-STORE senden");
        if (exitCode != 0) { LogSkip("Übersprungen."); }
        else
        {
            SetStatus("Sende DICOM-Bild...");
            ct.ThrowIfCancellationRequested();
            try
            {
                bool hasRealImage = !string.IsNullOrWhiteSpace(cfg.ImagePath) && File.Exists(cfg.ImagePath);
                if (hasRealImage) Log($"  Bild: {Path.GetFileName(cfg.ImagePath)}", Color.FromArgb(200, 200, 200));
                else              Log("  Bild: Testmuster 64×64 (kein Bild gewählt)", Color.DarkGray);

                DicomFile dcmFile = BuildTestDicomFile(cfg, foundAccession!, foundStudyUid!);
                bool ok = await SendCStoreAsync(cfg, dcmFile, ct);
                if (ok) LogOk("C-STORE erfolgreich (DicomStatus.Success).");
                else  { LogFail("C-STORE abgelehnt."); exitCode = 1; }
            }
            catch (Exception ex) { LogFail($"C-STORE Fehler: {ex.Message}"); exitCode = 1; }
        }

        // ── Schritt 4: GDT-Antwort ──
        LogHeader("Schritt 4/4 – GDT-Antwort prüfen (Satzart 6310)");
        if (exitCode != 0) { LogSkip("Übersprungen."); }
        else
        {
            SetStatus("Warte auf GDT-Antwort...");
            using var gdtCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            gdtCts.CancelAfter(TimeSpan.FromSeconds(15));
            bool gdtFound = false;

            while (!gdtCts.IsCancellationRequested)
            {
                try
                {
                    if (Directory.Exists(cfg.GdtOutputFolder))
                    {
                        var files = Directory.GetFiles(cfg.GdtOutputFolder, $"*{cfg.PatientId}*.gdt");
                        if (files.Length > 0)
                        {
                            string gdtResponsePath = files[0];
                            gdtFound = true;
                            LogOk($"GDT-Antwort gefunden: {Path.GetFileName(gdtResponsePath)}");
                            ParseAndLogGdt6310(gdtResponsePath);
                            break;
                        }
                    }
                    Log("  → Warte auf GDT-Antwort …", Color.DarkGray);
                    await Task.Delay(1000, gdtCts.Token);
                }
                catch (OperationCanceledException) { break; }
            }

            if (!gdtFound) { LogFail("Keine GDT-Antwort (Timeout 15 s)."); exitCode = 1; }
        }

        // ── Zusammenfassung ──
        Log("", Color.White);
        Log(new string('─', 55), Color.FromArgb(80, 80, 90));
        if (exitCode == 0) LogOk("Alle 4 Schritte erfolgreich abgeschlossen.");
        else               LogFail("Test fehlgeschlagen.");
    }

    // =====================================================================
    //  DICOM / GDT Helpers
    // =====================================================================
    private static void WriteGdt6301(string path, TestConfig cfg)
    {
        // GDT 2.1 – Satzart 6301 «Anforderung einer neuen Untersuchung»
        // Zeilenformat: [3-stellige Gesamtlänge][4-stellige FK][Inhalt][CR LF]
        // Länge in Bytes = 3 + 4 + GetByteCount(Inhalt) + 2 (CR LF)
        var enc = Encoding.GetEncoding(1252);

        var fields = new List<(string Fk, string Val)>
        {
            ("8000", "6301"),
            ("8100", string.Empty),                           // Zeilenanzahl – wird unten gesetzt
            ("3000", cfg.PatientId),
            ("3101", cfg.Surname),
            ("3102", cfg.Firstname),
            ("3103", cfg.BirthDate),                          // TTMMJJJJ
            ("3110", cfg.Sex),
            ("6200", DateTime.Today.ToString("ddMMyyyy")),    // TTMMJJJJ
        };
        fields[1] = ("8100", fields.Count.ToString());        // 8100 = Gesamtzahl Zeilen

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        foreach (var (fk, val) in fields)
        {
            int byteLen  = enc.GetByteCount(val);
            int totalLen = 3 + 4 + byteLen + 2;              // +2 = CR LF
            byte[] lineBytes = enc.GetBytes($"{totalLen:D3}{fk}{val}");
            fs.Write(lineBytes, 0, lineBytes.Length);
            fs.WriteByte(0x0D);   // CR
            fs.WriteByte(0x0A);   // LF
        }
    }

    private static async Task<List<(string acc, string uid)>> QueryWorklistAsync(
        TestConfig cfg, CancellationToken ct)
    {
        var results = new List<(string, string)>();
        var request = DicomCFindRequest.CreateWorklistQuery(patientId: cfg.PatientId);
        if (!request.Dataset.Contains(DicomTag.AccessionNumber))
            request.Dataset.Add(DicomTag.AccessionNumber, "");
        if (!request.Dataset.Contains(DicomTag.StudyInstanceUID))
            request.Dataset.Add(DicomTag.StudyInstanceUID, "");

        request.OnResponseReceived = (_, resp) =>
        {
            if (resp.Status == DicomStatus.Pending && resp.Dataset != null)
            {
                string acc = SafeGet(resp.Dataset, DicomTag.AccessionNumber);
                string uid = SafeGet(resp.Dataset, DicomTag.StudyInstanceUID);
                if (!string.IsNullOrEmpty(acc)) results.Add((acc, uid));
            }
        };

        var client = DicomClientFactory.Create(cfg.Host, cfg.Port, false, cfg.CallingAE, cfg.ServerAE);
        await client.AddRequestAsync(request);
        await client.SendAsync(ct);
        return results;
    }

    // GDT date DDMMYYYY → DICOM date YYYYMMDD
    private static string GdtDateToDicom(string gdtDate)
        => gdtDate.Length == 8
            ? gdtDate[4..8] + gdtDate[2..4] + gdtDate[0..2]
            : gdtDate;

    private static DicomFile BuildTestDicomFile(TestConfig cfg, string accNumber, string studyUid)
    {
        string dateStr = DateTime.Today.ToString("yyyyMMdd");
        string timeStr = DateTime.Now.ToString("HHmmss");

        var dataset = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian);
        dataset.Add(DicomTag.SpecificCharacterSet,      "ISO_IR 192");
        dataset.Add(DicomTag.SOPClassUID,               DicomUID.SecondaryCaptureImageStorage);
        dataset.Add(DicomTag.SOPInstanceUID,            DicomUID.Generate());
        dataset.Add(DicomTag.PatientID,                 cfg.PatientId);
        dataset.Add(DicomTag.PatientName,               $"{cfg.Surname}^{cfg.Firstname}");
        dataset.Add(DicomTag.PatientBirthDate,          GdtDateToDicom(cfg.BirthDate));
        dataset.Add(DicomTag.PatientSex,                cfg.Sex);
        dataset.Add(DicomTag.AccessionNumber,           accNumber);
        dataset.Add(DicomTag.StudyInstanceUID,          studyUid);
        dataset.Add(DicomTag.SeriesInstanceUID,         DicomUID.Generate());
        dataset.Add(DicomTag.InstanceNumber,            "1");
        dataset.Add(DicomTag.Modality,                  "OT");
        dataset.Add(DicomTag.StudyDate,                 dateStr);
        dataset.Add(DicomTag.StudyTime,                 timeStr);
        dataset.Add(DicomTag.SeriesDate,                dateStr);
        dataset.Add(DicomTag.SeriesTime,                timeStr);
        dataset.Add(DicomTag.ContentDate,               dateStr);
        dataset.Add(DicomTag.ContentTime,               timeStr);

        // ── Pixeldaten – echtes Bild oder 64×64-Testmuster ──
        if (!string.IsNullOrWhiteSpace(cfg.ImagePath) && File.Exists(cfg.ImagePath))
            EmbedImageIntoDicom(dataset, cfg.ImagePath);
        else
            EmbedGradientFallback(dataset);

        return new DicomFile(dataset);
    }

    /// <summary>
    /// Lädt ein beliebiges Bild (JPG/PNG/BMP/…) und bettet es als RGB-Pixeldaten in den DICOM-Datensatz.
    /// </summary>
    private static void EmbedImageIntoDicom(DicomDataset dataset, string imagePath)
    {
        using var srcBmp = new System.Drawing.Bitmap(imagePath);
        // In 24bpp RGB konvertieren (normiert, ohne Alpha-Kanal)
        using var bmp = srcBmp.Clone(
            new System.Drawing.Rectangle(0, 0, srcBmp.Width, srcBmp.Height),
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        int rows = bmp.Height, cols = bmp.Width;

        // GDI+ liefert BGR; DICOM RGB erwartet R,G,B interleaved (PlanarConfig 0)
        var bmpData = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, cols, rows),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        int stride = bmpData.Stride;
        byte[] raw = new byte[rows * stride];
        System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, raw, 0, raw.Length);
        bmp.UnlockBits(bmpData);

        // BGR (stride-padded) → RGB (tightly packed)
        byte[] pixels = new byte[rows * cols * 3];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int s = r * stride + c * 3;
                int d = (r * cols + c) * 3;
                pixels[d]     = raw[s + 2]; // R
                pixels[d + 1] = raw[s + 1]; // G
                pixels[d + 2] = raw[s];     // B
            }

        dataset.Add(DicomTag.SamplesPerPixel,           (ushort)3);
        dataset.Add(DicomTag.PhotometricInterpretation, "RGB");
        dataset.Add(DicomTag.PlanarConfiguration,       (ushort)0);  // interleaved
        dataset.Add(DicomTag.Rows,                      (ushort)rows);
        dataset.Add(DicomTag.Columns,                   (ushort)cols);
        dataset.Add(DicomTag.BitsAllocated,             (ushort)8);
        dataset.Add(DicomTag.BitsStored,                (ushort)8);
        dataset.Add(DicomTag.HighBit,                   (ushort)7);
        dataset.Add(DicomTag.PixelRepresentation,       (ushort)0);

        var pixelData = DicomPixelData.Create(dataset, true);
        pixelData.AddFrame(new MemoryByteBuffer(pixels));
    }

    /// <summary>Fallback: 64×64 Graustufen-Gradient wenn kein Bild angegeben.</summary>
    private static void EmbedGradientFallback(DicomDataset dataset)
    {
        dataset.Add(DicomTag.SamplesPerPixel,           (ushort)1);
        dataset.Add(DicomTag.PhotometricInterpretation, "MONOCHROME2");
        dataset.Add(DicomTag.Rows,                      (ushort)64);
        dataset.Add(DicomTag.Columns,                   (ushort)64);
        dataset.Add(DicomTag.BitsAllocated,             (ushort)8);
        dataset.Add(DicomTag.BitsStored,                (ushort)8);
        dataset.Add(DicomTag.HighBit,                   (ushort)7);
        dataset.Add(DicomTag.PixelRepresentation,       (ushort)0);

        var pixelData = DicomPixelData.Create(dataset, true);
        byte[] pixels = new byte[64 * 64];
        for (int r = 0; r < 64; r++)
            for (int c = 0; c < 64; c++)
                pixels[r * 64 + c] = (byte)((r + c) % 256);
        pixelData.AddFrame(new MemoryByteBuffer(pixels));
    }


    private static async Task<bool> SendCStoreAsync(TestConfig cfg, DicomFile file, CancellationToken ct)
    {
        bool success = false;
        var request = new DicomCStoreRequest(file);
        request.OnResponseReceived = (_, resp) => success = resp.Status == DicomStatus.Success;
        var client = DicomClientFactory.Create(cfg.Host, cfg.Port, false, cfg.CallingAE, cfg.ServerAE);
        await client.AddRequestAsync(request);
        await client.SendAsync(ct);
        return success;
    }

    private void ParseAndLogGdt6310(string path)
    {
        try
        {
            string[] lines = File.ReadAllLines(path, Encoding.GetEncoding(1252));
            foreach (string line in lines)
            {
                if (line.Length < 7) continue;
                string fk  = line.Substring(3, 4);
                string val = line.Length > 7 ? line[7..].Trim() : "";
                switch (fk)
                {
                    case "8000": Log($"  Satzart:       {val}", Color.FromArgb(170, 220, 255)); break;
                    case "3000": Log($"  Patient-ID:    {val}", Color.White);                   break;
                    case "3101": Log($"  Nachname:      {val}", Color.White);                   break;
                    case "3102": Log($"  Vorname:       {val}", Color.White);                   break;
                    case "6200": Log($"  Datum:         {val}", Color.White);                   break;
                    case "6210": Log($"  Uhrzeit:       {val}", Color.White);                   break;
                    case "8202": Log($"  Gerät (AE):    {val}", Color.White);                   break;
                    case "8132":
                        LogOk($"  Bilddatei:     {val}");
                        bool exists = File.Exists(val);
                        if (exists) Log($"  Datei auf Disk: ✓ vorhanden", Color.LightGreen);
                        else        Log($"  Datei auf Disk: ✗ nicht auffindbar", Color.Orange);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"  Konnte GDT-Antwort nicht lesen: {ex.Message}", Color.Orange);
        }
    }

    private static string SafeGet(DicomDataset ds, DicomTag tag)
    {
        try { return ds.GetString(tag) ?? ""; } catch { return ""; }
    }

    // =====================================================================
    //  Log helpers (thread-safe via Invoke)
    // =====================================================================
    private void Log(string msg, Color color)
    {
        if (InvokeRequired) { Invoke(() => Log(msg, color)); return; }
        _log.SelectionStart  = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor  = color;
        _log.AppendText(msg + "\n");
        _log.SelectionColor  = _log.ForeColor;
        _log.ScrollToCaret();
    }

    private void LogHeader(string msg) => Log($"\n── {msg}", Color.FromArgb(255, 200, 60));
    private void LogOk(string msg)     => Log($"✓ {msg}", Color.LightGreen);
    private void LogFail(string msg)   => Log($"✗ {msg}", Color.Tomato);
    private void LogSkip(string msg)   => Log($"○ {msg}", Color.DimGray);

    private void SetStatus(string msg)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(msg)); return; }
        _statusLabel.Text = msg;
    }

    // =====================================================================
    //  UI Builder helpers
    // =====================================================================
    private void AddRowFilePicker(Panel p, int row, string label, TextBox txt, string filter)
    {
        Style(txt);
        txt.Width = 260;
        var lbl = new Label { Text = label, AutoSize = true, ForeColor = Color.FromArgb(200, 200, 200),
                              Padding = new Padding(0, 6, 4, 0) };
        var inner = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                                          Margin = new Padding(0, 2, 0, 2) };
        var btn = new Button { Text = "…", Width = 28, Height = txt.Height + 4,
                               BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White,
                               FlatStyle = FlatStyle.Flat };
        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 90);
        btn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = filter, Title = label };
            if (!string.IsNullOrWhiteSpace(txt.Text) && File.Exists(txt.Text))
                dlg.InitialDirectory = Path.GetDirectoryName(txt.Text)!;
            if (dlg.ShowDialog() == DialogResult.OK) txt.Text = dlg.FileName;
        };
        inner.Controls.Add(lbl);
        inner.Controls.Add(txt);
        inner.Controls.Add(btn);
        p.Controls.Add(inner);
        p.Height = (row + 1) * 34;
    }

    private static Panel AutoPanel() => new() { AutoSize = true, Padding = new Padding(4) };

    private void AddRow(Panel p, int row, string label, Control ctrl)
    {
        Style(ctrl);
        var lbl = new Label { Text = label, AutoSize = true, ForeColor = Color.FromArgb(200, 200, 200),
                              Padding = new Padding(0, 6, 4, 0) };
        var inner = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                                          Margin = new Padding(0, 2, 0, 2) };
        inner.Controls.Add(lbl);
        inner.Controls.Add(ctrl);
        p.Controls.Add(inner);
        p.Height = (row + 1) * 34;
    }

    private void AddRowBrowse(Panel p, int row, string label, TextBox txt, bool browse)
    {
        Style(txt);
        txt.Width = 260;
        var lbl = new Label { Text = label, AutoSize = true, ForeColor = Color.FromArgb(200, 200, 200),
                              Padding = new Padding(0, 6, 4, 0) };
        var inner = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                                          Margin = new Padding(0, 2, 0, 2) };
        inner.Controls.Add(lbl);
        inner.Controls.Add(txt);
        if (browse)
        {
            var btn = new Button { Text = "…", Width = 28, Height = txt.Height + 4,
                                   BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White,
                                   FlatStyle = FlatStyle.Flat };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 90);
            btn.Click += (_, _) =>
            {
                using var dlg = new FolderBrowserDialog { SelectedPath = txt.Text };
                if (dlg.ShowDialog() == DialogResult.OK) txt.Text = dlg.SelectedPath;
            };
            inner.Controls.Add(btn);
        }
        p.Controls.Add(inner);
        p.Height = (row + 1) * 34;
    }

    private static GroupBox GroupBox(string title, Panel content)
    {
        var gb = new GroupBox
        {
            Text = title, AutoSize = true,
            ForeColor = Color.FromArgb(180, 180, 180),
            BackColor = Color.FromArgb(38, 38, 48),
            Margin = new Padding(6),
            Padding = new Padding(8),
        };
        content.BackColor = Color.Transparent;
        gb.Controls.Add(content);
        return gb;
    }

    private static void Style(Control c)
    {
        c.BackColor = Color.FromArgb(48, 48, 58);
        c.ForeColor = Color.White;
        if (c is TextBox tb) { tb.BorderStyle = BorderStyle.FixedSingle; }
        if (c is ComboBox cb) { cb.FlatStyle = FlatStyle.Flat; }
    }

    private static void StyleButton(Button btn, Color back, Color fore)
    {
        btn.BackColor = back;
        btn.ForeColor = fore;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 0;
        btn.Cursor = Cursors.Hand;
    }
}

record TestConfig(
    string GdtInputFolder,
    string GdtOutputFolder,
    string Host,
    int    Port,
    string ServerAE,
    string CallingAE,
    string PatientId,
    string Surname,
    string Firstname,
    string BirthDate,
    string Sex,
    string ImagePath);
