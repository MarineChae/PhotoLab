@echo off
setlocal
cd /d "%~dp0"
dotnet restore PhotoLab.slnx
if errorlevel 1 goto :fail
dotnet run --project src\PhotoLab.Desktop\PhotoLab.Desktop.csproj -c Release
if errorlevel 1 goto :fail
exit /b 0
:fail
echo Build or run failed. Check that .NET 10 SDK is installed.
exit /b 1
