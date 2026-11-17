@echo off
setlocal
echo Fully exit Athan Time first. Run this as your normal user, not as administrator.
echo Remove your settings, backups, window position, app logs and login startup?
echo Other users and custom recordings are kept. The application itself is kept.
choice /C NY /N /M "Remove my settings? [N/Y]: "
if errorlevel 3 exit /b 0
if not errorlevel 2 exit /b 0
"%~dp0AthanTime.Net.exe" --remove-user-settings
if errorlevel 1 (
  echo Settings cleanup failed with exit code %errorlevel%. Close all app copies and check access, then retry.
  pause
  exit /b 1
)
echo Settings removed. You can now remove the application files.
pause
