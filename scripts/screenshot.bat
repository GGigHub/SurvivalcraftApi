@echo off
if "%~1"=="" (
    echo Usage: %~0 ^<output_filename^>
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0screenshot.ps1" "%~1"
