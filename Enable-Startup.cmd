@echo off
"%~dp0AudioSwitchCtl.exe" --install-startup
if errorlevel 1 pause
