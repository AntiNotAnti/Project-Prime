@echo off
setlocal
set "launcher=%~dp0Start-ProjectPrime.ps1"
set "powershell=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%powershell%" set "powershell=pwsh.exe"
"%powershell%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%launcher%" %*
set "exitCode=%ERRORLEVEL%"
if not "%exitCode%"=="0" pause
exit /b %exitCode%
