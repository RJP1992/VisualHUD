@echo off
REM Launcher for VisualHUD — the SWBF2 HUD editor.
REM Double-click this to open the editor without needing a terminal.
REM
REM Assumes the editor has already been built in Debug mode via `dotnet build`.
REM If you see "VisualHUD.exe not found", run:
REM     dotnet build SwbfHudEditor.sln -c Debug
REM from the repo root, then double-click this file again.

setlocal
set EXE=%~dp0src\SwbfHudEditor.App\bin\Debug\net8.0-windows\VisualHUD.exe

if not exist "%EXE%" (
    echo.
    echo VisualHUD.exe not found at:
    echo    %EXE%
    echo.
    echo Build it first:
    echo    dotnet build "%~dp0SwbfHudEditor.sln" -c Debug
    echo.
    pause
    exit /b 1
)

start "" "%EXE%"
endlocal
