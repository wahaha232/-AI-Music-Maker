@echo off
setlocal
set "PY=%~dp0..\..\AI_Music.venv\Scripts\python.exe"
if not exist "%PY%" set "PY=python"
if "%~1"=="" (
  "%PY%" "%~dp0music_to_video.py" --from-settings
  pause
) else (
  "%PY%" "%~dp0music_to_video.py" %*
)
endlocal
