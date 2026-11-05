@echo off
title AIR Browser
cd /d "%~dp0"

:: Try to open in Edge app mode (looks like a real app)
where msedge >nul 2>&1
if %errorlevel%==0 (
  start "" msedge --app="%cd%\index.html" --new-window
  exit
)

:: Fallback to Chrome app mode
where chrome >nul 2>&1
if %errorlevel%==0 (
  start "" chrome --app="%cd%\index.html" --new-window
  exit
)

:: Last fallback - just open in default browser
start "" "%cd%\index.html"
