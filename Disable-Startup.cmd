@echo off
"%~dp0AudioSwitchCtl.exe" --uninstall-startup
if errorlevel 1 pause
