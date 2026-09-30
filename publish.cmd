@echo off
rem Builds a single Clicker.exe into the publish folder (needs the .NET 8+ runtime on the PC).

:check_running
tasklist /FI "IMAGENAME eq Clicker.exe" 2>nul | find /I "Clicker.exe" >nul
if %errorlevel%==0 (
    echo Clicker is running, so publish\Clicker.exe can't be replaced.
    echo Exit it from the tray icon: right-click - Exit. The X button only hides it to the tray.
    echo.
    pause
    goto check_running
)

dotnet publish Clicker\Clicker.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
if %errorlevel%==0 echo. & echo Done: publish\Clicker.exe
pause
