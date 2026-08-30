@echo off
setlocal

set "PDIR=%~dp0"
cd /d "%PDIR%"

start "" dotnet run --project Content.Server -c Release --no-build
dotnet run --project Content.Client -c Release --no-build

endlocal