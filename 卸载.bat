@echo off
cd /d "%~dp0"
set "EXE=%USERPROFILE%\Desktop\触摸板开关.exe"
if not exist "%EXE%" set "EXE=%~dp0dist\触摸板开关.exe"
if not exist "%EXE%" (
    echo 找不到触摸板开关.exe
    pause
    exit /b 1
)
"%EXE%" --uninstall
del /f /q "%USERPROFILE%\Desktop\触摸板开关.exe" 2>nul
echo 已卸载触摸板开关。
echo.
pause
