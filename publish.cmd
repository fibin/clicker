@echo off
rem Builds a single Clicker.exe into the publish folder (needs the .NET 8+ runtime on the PC).
dotnet publish Clicker\Clicker.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
if %errorlevel%==0 echo. & echo Done: publish\Clicker.exe
pause
