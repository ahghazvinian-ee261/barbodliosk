@echo off
REM Builds a single self-contained BarbodKiosk.exe (no .NET install needed on target PC)
REM Requires: .NET 8 SDK installed on THIS build machine only.
REM Download SDK: https://dotnet.microsoft.com/download/dotnet/8.0

cd /d "%~dp0"

dotnet publish -c Release -r win-x64 --self-contained true

echo.
echo ============================================================
echo Build complete. Find BarbodKiosk.exe in:
echo   bin\Release\net8.0-windows\win-x64\publish\
echo ============================================================
pause
