@echo off
title TheraQuest server
cd /d "%~dp0server"
start "" http://localhost:8000
python server.py
pause
