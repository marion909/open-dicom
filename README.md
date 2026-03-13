# OpenDicom

Kostenloser DICOM-Server für Windows mit **Modality Worklist (C-FIND)** und **Bildspeicher (C-STORE)**, vollständig integriert in den **GDT 2.1**-Workflow.

---

## Features

| Funktion | Details |
|---|---|
| **DICOM C-FIND** | Modality Worklist – Abfragen durch beliebige DICOM-SCUs (Geräte, Viewer) |
| **DICOM C-STORE** | Empfängt DICOM-Bilder, speichert `.dcm` + extrahiert lesbares `.png` |
| **GDT 2.1 Eingang** | Satzart 6301 – AIS legt `.gdt`-Datei ab, FileSystemWatcher legt Worklist-Eintrag an |
| **GDT 2.1 Ausgang** | Satzart 6310 – nach C-STORE an AIS zurückgeschrieben; FK 8132 zeigt auf das PNG-Bild |
| **Windows Service** | Läuft als Dienst (`install.bat`) oder manuell (`start.bat`) |
| **Erster Start** | Erstellt `service.ini` und alle Datenordner automatisch |
| **TestTool** | WinForms-GUI zum Testen des kompletten Ablaufs (GDT schreiben → Worklist → C-STORE → GDT-Antwort) |

---

## Voraussetzungen

- Windows 10/11 oder Windows Server 2019+
- Kein .NET-Runtime erforderlich – der Server ist **self-contained** (single `.exe`, ~35 MB)
- Zum Bauen aus dem Quellcode: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Schnellstart

### Als Windows-Dienst installieren

1. Ordner `C:\OpenDicom\` anlegen und `OpenDicom.exe` + `service.ini` hineinkopieren
2. `install.bat` **als Administrator** ausführen → Menü erscheint
3. Option **1 – Dienst installieren** wählen
4. Dienst startet automatisch beim Windows-Boot

### Manuell starten (ohne Dienst)

```bat
start.bat
```

---

## Konfiguration (`service.ini`)

```ini
[Dicom]
AeTitle=OPENDICOM        ; AE-Title des Servers (max. 16 Zeichen)
Port=11112               ; DICOM-Port (Standard: 11112)

[Paths]
GdtInputFolder=C:\OpenDicom\gdt_in      ; AIS legt hier .gdt-Dateien ab (Satzart 6301)
GdtOutputFolder=C:\OpenDicom\gdt_out    ; Server schreibt hier .gdt-Antworten (Satzart 6310)
DicomStorageFolder=C:\OpenDicom\storage ; DICOM-Bilder (.dcm + .png)

[Worklist]
EntryTtlHours=24         ; Worklist-Einträge nach 24 h automatisch löschen
DefaultModality=*        ; Modalität für Worklist-Abfragen (* = alle)

[Serilog]
MinimumLevel=Information ; Log-Level: Verbose / Debug / Information / Warning / Error
```

Die Datei wird beim ersten Start mit obigen Standardwerten **automatisch erstellt**, falls sie fehlt.

---

## GDT-Workflow

```
AIS                        OpenDicom-Server              DICOM-Gerät / Viewer
   │                              │                              │
   │── Satzart 6301 ──────────────▶│                              │
   │   (Patient + Auftrag)        │── Worklist-Eintrag anlegen   │
   │                              │                              │
   │                              │◀── C-FIND (Worklist-Abfrage)─│
   │                              │─── Worklist-Antwort ────────▶│
   │                              │                              │
   │                              │◀── C-STORE (DICOM-Bild) ─────│
   │                              │── .dcm speichern             │
   │                              │── .png extrahieren           │
   │◀── Satzart 6310 ─────────────│                              │
   │   (FK 8132 → PNG-Pfad)       │                              │
```

### GDT-Dateiformat (Satzart 6301 – Eingang)

Pflichtfelder:

| FK | Bedeutung | Beispiel |
|---|---|---|
| 8000 | Satzart | `6301` |
| 8100 | Zeilenanzahl | `8` |
| 3000 | Patientennummer | `12345` |
| 3101 | Nachname | `Mustermann` |
| 3102 | Vorname | `Max` |
| 3103 | Geburtsdatum (TTMMJJJJ) | `15031980` |
| 3110 | Geschlecht (M/W/D) | `M` |
| 6200 | Untersuchungsdatum (TTMMJJJJ) | `13032026` |

### GDT-Dateiformat (Satzart 6310 – Ausgang)

Relevante Felder der Antwortdatei:

| FK | Bedeutung |
|---|---|
| 8132 | Pfad zur PNG-Bilddatei (direkt öffenbar) |
| 8202 | AE-Title des Servers |
| 6200 / 6210 | Untersuchungsdatum / -uhrzeit |

---

## Bildverarbeitung

Nach C-STORE extrahiert der Server automatisch ein **PNG** aus dem DICOM-Datensatz:

| DICOM-Format | Ausgabe |
|---|---|
| 8-Bit Graustufen | PNG (8bpp, Graupalette) |
| 16-Bit Graustufen | PNG (auf 8 Bit normalisiert) |
| 24-Bit RGB interleaved | PNG (24bpp) |
| 24-Bit RGB planar | PNG (24bpp) |

Das `.dcm`-Original bleibt erhalten. Der Pfad in der GDT-Antwort (FK 8132) zeigt auf die **PNG-Datei**.

---

## TestTool

Das mitgelieferte WinForms-Testtool (`OpenDicom.TestTool.exe`) führt automatisch alle 4 Schritte des Workflows aus:

1. **GDT Satzart 6301 schreiben** – legt Testpatient im Eingabeordner ab
2. **Worklist via C-FIND abfragen** – wartet bis zu 15 s auf Eintrag
3. **Test-DICOM via C-STORE senden** – wahlweise mit eigenem Bild (JPG/PNG/BMP/TIF) oder automatisch generiertem 64×64-Testmuster
4. **GDT Satzart 6310 prüfen** – zeigt FK 8132 (Bilddatei) und prüft ob die Datei existiert

---

## Aus dem Quellcode bauen

```powershell
# Server
dotnet publish OpenDicom\OpenDicom.csproj -c Release -o C:\OpenDicom

# TestTool
dotnet publish OpenDicom.TestTool\OpenDicom.TestTool.csproj -c Release -o C:\OpenDicom\TestTool
```

---

## Projektstruktur

```
open-dicom/
├── OpenDicom/                  # Server
│   ├── DicomCore/
│   │   ├── DicomDataset.cs         # Explicit/Implicit VR LE read+write
│   │   ├── DicomTag.cs             # Tag-Konstanten
│   │   ├── DicomVR.cs / DicomUids.cs / DicomUidGenerator.cs
│   │   └── DicomFileWriter.cs      # DICOM Part-10 Datei schreiben
│   ├── Network/
│   │   ├── Pdu.cs                  # PDU lesen/schreiben, P-DATA-Fragmentierung
│   │   ├── AssociatePdu.cs         # A-ASSOCIATE-RQ parsen, AC/RJ bauen
│   │   ├── DicomConnection.cs      # Zustandsautomat pro Client (C-ECHO/C-FIND/C-STORE)
│   │   └── DicomServer.cs          # TcpListener-Wrapper
│   ├── Dicom/
│   │   ├── DicomHandler.cs         # C-FIND, C-STORE, PNG-Extraktion
│   │   └── DicomServerService.cs   # BackgroundService-Wrapper
│   ├── Logging/
│   │   └── FileLoggerProvider.cs   # Rolling-File-Logger (BlockingCollection-Queue)
│   ├── Gdt/
│   │   ├── GdtParser.cs            # Satzart 6301 einlesen (Win-1252)
│   │   ├── GdtRecord.cs            # Datenmodell mit DICOM-Konvertierung
│   │   └── GdtWriter.cs            # Satzart 6310 schreiben (GDT-konform)
│   ├── Storage/
│   │   ├── WorklistEntry.cs        # DICOM-Dataset-Generierung
│   │   └── WorklistStore.cs        # Thread-safe In-Memory-Speicher mit TTL
│   ├── Watcher/
│   │   └── GdtWatcherService.cs    # FileSystemWatcher + Debounce + Archiv
│   ├── Program.cs                  # Host-Bootstrap, INI-Konfiguration
│   ├── AppSettings.cs              # Typisierte Konfigurationsklassen
│   ├── service.ini                 # Konfigurationsdatei
│   ├── install.bat                 # Dienst installieren/deinstallieren
│   └── start.bat                   # Manuell starten
├── OpenDicom.TestTool/             # WinForms-Testtool
│   ├── MainForm.cs
│   └── Program.cs
├── test/                           # Beispiel-GDT-Dateien
│   ├── test_6301_Mustermann_Max.gdt
│   ├── test_6301_Schmidt_Maria.gdt
│   └── example_6310_Mustermann_Max.gdt
└── open-dicom.sln
```

---

## Technologie

- **.NET 8** – Worker Service, self-contained, win-x64, PublishSingleFile
- **Eigene DICOM-Implementierung** – BCL-only, kein fo-dicom (C-FIND, C-STORE, P-DATA-Fragmentierung)
- **System.Drawing.Common** – PNG-Extraktion aus DICOM-Pixeldaten
- **Microsoft.Extensions.Logging** – Rolling-File-Logger + Windows Event Log
- **GDT 2.1** – Windows-1252, hard CR LF, byte-genaue Längenfeldberechnung
- **AIS-Integration** – Österreichisches Arztinformationssystem via GDT 2.1 Satzarten 6301/6310

---

## Lizenz

MIT License – kostenlos für private und kommerzielle Nutzung.
