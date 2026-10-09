@echo off
title TheraQuest tracker
cd /d "%~dp0tracker"
if not exist .venv (
  echo First run: creating Python environment and installing MediaPipe...
  python -m venv .venv
  call .venv\Scripts\activate
  pip install -r requirements.txt
) else (
  call .venv\Scripts\activate
)
python tracker.py %*
pause
