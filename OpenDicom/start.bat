@echo off
:: ============================================================
::  OpenDicom – Manueller Start (Konsolenmodus, kein Service)
:: ============================================================

set "EXE_PATH=%~dp0OpenDicom.exe"

if not exist "%EXE_PATH%" (
    echo [FEHLER] OpenDicom.exe nicht gefunden: %EXE_PATH%
    pause
    exit /b 1
)

echo  OpenDicom DICOM Server
echo  =======================
echo  Konfiguration: %~dp0service.ini
echo  Zum Beenden: Strg+C
echo.

"%EXE_PATH%"
