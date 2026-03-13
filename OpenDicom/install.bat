@echo off
:: ============================================================
::  OpenDicom – Windows-Service Installation / Deinstallation
::  Als Administrator ausfuehren!
:: ============================================================

:: Pfad zur .exe ermitteln (relativ zum Ort dieser .bat-Datei)
set "EXE_PATH=%~dp0OpenDicom.exe"

:: Pruefe ob die .exe vorhanden ist
if not exist "%EXE_PATH%" (
    echo [FEHLER] OpenDicom.exe nicht gefunden: %EXE_PATH%
    echo Bitte das Skript aus dem Verzeichnis ausfuehren, in dem OpenDicom.exe liegt.
    pause
    exit /b 1
)

:: Administratorrechte pruefen
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [FEHLER] Bitte als Administrator ausfuehren.
    pause
    exit /b 1
)

echo.
echo  OpenDicom Service Manager
echo  ===========================
echo  EXE: %EXE_PATH%
echo.
echo  [1] Service installieren und starten
echo  [2] Service stoppen und deinstallieren
echo  [3] Service-Status anzeigen
echo  [4] Abbrechen
echo.
set /p CHOICE=Auswahl (1-4): 

if "%CHOICE%"=="1" goto INSTALL
if "%CHOICE%"=="2" goto UNINSTALL
if "%CHOICE%"=="3" goto STATUS
goto END

:: ---- INSTALL ------------------------------------------------
:INSTALL
echo.
echo [INFO] Pruefe ob Service bereits existiert...
sc query OpenDicom >nul 2>&1
if %errorLevel% equ 0 (
    echo [WARN] Service "OpenDicom" ist bereits installiert.
    echo        Starte Service neu...
    sc stop OpenDicom >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc start OpenDicom
    if %errorLevel% equ 0 (
        echo [OK] Service gestartet.
    ) else (
        echo [FEHLER] Service konnte nicht gestartet werden. Siehe Event-Log.
    )
    goto END
)

echo [INFO] Installiere Service "OpenDicom"...
sc create OpenDicom ^
    binpath= "%EXE_PATH%" ^
    start= auto ^
    DisplayName= "OpenDicom DICOM Server" ^
    obj= LocalSystem

if %errorLevel% neq 0 (
    echo [FEHLER] Service konnte nicht erstellt werden.
    pause
    exit /b 1
)

echo [INFO] Setze Beschreibung...
sc description OpenDicom "Kostenloser DICOM Worklist + Store Server mit GDT 2.1 Anbindung"

echo [INFO] Konfiguriere automatischen Neustart bei Fehler...
sc failure OpenDicom reset= 86400 actions= restart/5000/restart/10000/restart/30000

echo [INFO] Starte Service...
sc start OpenDicom
if %errorLevel% equ 0 (
    echo.
    echo [OK] OpenDicom wurde erfolgreich installiert und gestartet.
    echo      Beim ersten Start werden die Konfigurationsdatei (service.ini)
    echo      und die Datenordner automatisch angelegt.
    echo      Konfigurationsdatei: %~dp0service.ini
) else (
    echo [FEHLER] Service konnte nicht gestartet werden. Bitte Event-Log pruefen.
)
goto END

:: ---- UNINSTALL ----------------------------------------------
:UNINSTALL
echo.
echo [INFO] Stoppe Service "OpenDicom"...
sc stop OpenDicom >nul 2>&1
timeout /t 3 /nobreak >nul

echo [INFO] Loesche Service "OpenDicom"...
sc delete OpenDicom
if %errorLevel% equ 0 (
    echo [OK] Service wurde erfolgreich entfernt.
) else (
    echo [FEHLER] Service konnte nicht entfernt werden (moeglicherweise nicht installiert).
)
goto END

:: ---- STATUS -------------------------------------------------
:STATUS
echo.
sc query OpenDicom
goto END

:: ---- END ----------------------------------------------------
:END
echo.
pause
