@echo off
REM SnapNote - stop starting automatically at sign-in.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Remove-Item -LiteralPath ([Environment]::GetFolderPath('Startup') + '\SnapNote.lnk') -ErrorAction SilentlyContinue"
echo Removed (if it was there).
pause
