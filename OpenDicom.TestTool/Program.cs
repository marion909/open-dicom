using System.Text;
using FellowOakDicom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDicom.TestTool;

// Windows-1252 fÃ¼r GDT-Encoding registrieren
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// fo-dicom DI-Setup
new DicomSetupBuilder()
    .RegisterServices(s =>
    {
        s.AddFellowOakDicom();
        s.AddLogging(lb => lb.SetMinimumLevel(LogLevel.Error));
    })
    .Build();

// WinForms starten
ApplicationConfiguration.Initialize();
Application.Run(new MainForm());
