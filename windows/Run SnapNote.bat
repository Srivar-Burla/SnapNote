@echo off
REM ---------------------------------------------------------------
REM  SnapNote - start the app (it lives in the system tray).
REM  If it is already running, this starts a capture instead.
REM ---------------------------------------------------------------
setlocal
cd /d "%~dp0"
if not exist "SnapNote.exe" (
  call setup.bat || exit /b 1
)
start "" "%~dp0SnapNote.exe" %*
