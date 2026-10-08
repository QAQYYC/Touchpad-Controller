@echo off
cd /d "%~dp0"
if not exist "%~dp0dist\触摸板开关.exe" (
    echo 找不到 dist\触摸板开关.exe
    pause
    exit /b 1
)
"%~dp0dist\触摸板开关.exe" --install
