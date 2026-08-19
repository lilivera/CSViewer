@echo off
setlocal

call build.bat
if errorlevel 1 exit /b 1

CsvPreviewer.SmokeTests\bin\Release\CsvPreviewer.SmokeTests.exe
exit /b %errorlevel%
