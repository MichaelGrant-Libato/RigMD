@echo off
setlocal
echo ======================================
echo  RigMD Uninstaller Launcher
echo ======================================
echo.

if exist "%ProgramFiles%\RigMD\unins000.exe" (
    echo Launching RigMD uninstaller from %ProgramFiles%\RigMD\unins000.exe...
    start "" "%ProgramFiles%\RigMD\unins000.exe"
    goto :eof
)

if exist "%LocalAppData%\Programs\RigMD\unins000.exe" (
    echo Launching RigMD uninstaller from %LocalAppData%\Programs\RigMD\unins000.exe...
    start "" "%LocalAppData%\Programs\RigMD\unins000.exe"
    goto :eof
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall-rigmd.ps1"
