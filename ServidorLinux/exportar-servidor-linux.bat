@echo off
title Dragon Ball Jandirus -- exportar o servidor pra Linux
REM Gera ServidorLinux\jandirus-servidor-linux.tar.gz, o servidor dedicado pra VPS Debian. Ver LEIA-ME.md.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0exportar-servidor-linux.ps1" %*
echo.
pause
