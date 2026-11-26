@echo off
REM ---------------------------------------------------------------
REM  SnapNote - start automatically when you sign in to Windows.
REM  Adds one shortcut to your personal Startup folder:
REM    %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
REM  Undo with "Remove from Startup.bat".
REM ---------------------------------------------------------------
setlocal
cd /d "%~dp0"
if not exist "SnapNote.exe" call setup.bat || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Startup') + '\SnapNote.lnk');" ^
  "$s.TargetPath='%~dp0SnapNote.exe'; $s.Arguments='--quiet'; $s.WorkingDirectory='%~dp0'; $s.Save()"
if errorlevel 1 ( echo [!] Could not add the shortcut. & pause & exit /b 1 )
echo SnapNote will now start when you sign in.
pause
