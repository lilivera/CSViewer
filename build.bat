@echo off
setlocal

where msbuild >nul 2>&1
if errorlevel 1 (
    echo MSBuild ‚ªŒ©‚Â‚©‚è‚Ü‚¹‚ñB
    echo Visual Studio Developer Command Prompt ‚©‚çÀs‚µ‚Ä‚­‚¾‚³‚¢B
    exit /b 1
)

msbuild CsvPreviewer.sln /t:Rebuild /p:Configuration=Release /m
if errorlevel 1 exit /b 1

echo.
echo Build succeeded:
echo CsvPreviewer\bin\Release\CSViewer.exe
exit /b 0
