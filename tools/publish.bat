@echo off
REM Builds the release: one VisualHUD.exe in dist\ that runs on any 64-bit Windows 10 or 11,
REM without .NET installed. Needs the .NET 8 SDK (or newer) to build.

setlocal
set ROOT=%~dp0..
set OUT=%ROOT%\dist

dotnet publish "%ROOT%\src\SwbfHudEditor.App\SwbfHudEditor.App.csproj" -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=none -o "%OUT%"
if errorlevel 1 (
    echo.
    echo The build failed.
    pause
    exit /b 1
)

echo.
echo Built %OUT%\VisualHUD.exe
endlocal
