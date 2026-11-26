@echo off
REM ---------------------------------------------------------------
REM  SnapNote - build the app (one time, about 5 seconds)
REM  Uses the C# compiler that ships inside Windows (.NET Framework 4).
REM  Nothing is downloaded and nothing is installed system-wide.
REM  NOTE: tested on one PC (Windows 11). If it fails, see README.md.
REM ---------------------------------------------------------------
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [!] The .NET Framework 4 C# compiler was not found.
  echo     It ships with Windows 10 and 11. See README.md.
  pause
  exit /b 1
)

echo Building SnapNote.exe ...
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:SnapNote.exe /win32icon:src\SnapNote.ico ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo [!] Build failed. See the messages above.
  pause
  exit /b 1
)
echo Done. Start it with "Run SnapNote.bat".
exit /b 0
