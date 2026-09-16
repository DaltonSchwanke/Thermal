@echo off
setlocal
title Thermal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start.ps1"
if errorlevel 1 (
    echo.
    echo Thermal could not start. See the message above for the next step.
    pause
    exit /b 1
)
