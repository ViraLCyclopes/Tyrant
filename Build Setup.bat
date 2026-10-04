@echo off
rem Builds Tyrant's Setup program (Tyrant_<version>_x64-setup.exe) into the release folder next to this file.
rem Needs the .NET 10 SDK, Node and Rust (see README.md, "The Tyrant app").
setlocal
title Building Tyrant Setup
cd /d "%~dp0studio"

where npm >nul 2>nul || (echo Node.js is not installed or not on PATH: get it from https://nodejs.org & goto :failed)
where dotnet >nul 2>nul || (echo The .NET SDK is not installed or not on PATH: get .NET 10 from https://dotnet.microsoft.com & goto :failed)
where cargo >nul 2>nul || (echo Rust is not installed or not on PATH: get it from https://rustup.rs & goto :failed)

if not exist node_modules (
  echo Installing the app's packages, first time only...
  call npm install || goto :failed
)

echo Building Tyrant's Setup program; this takes a few minutes...
call npm run build:app || goto :failed

echo.
echo Done. The Setup program is in: "%~dp0release"
start "" "%~dp0release"
pause
exit /b 0

:failed
echo.
echo The build failed; the messages above say why.
pause
exit /b 1
