@echo off
chcp 65001 >nul
echo Сборка релиза MyPlayer (портатив + MSI). Нужен интернет при первом запуске.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" %*
if errorlevel 1 (
  echo.
  echo *** СБОРКА НЕ УДАЛАСЬ. Пришлите текст выше и файлы из папки build\*.log ***
) else (
  echo.
  echo Открываю папку с готовыми файлами...
  explorer "%~dp0..\artifacts"
)
echo.
pause
