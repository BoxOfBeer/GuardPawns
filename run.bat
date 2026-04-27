@echo off
setlocal

set PROJ=SpaceBall
set RELEASE_DIR=release
set EXE=SpaceDNA.exe

:: Запуск из release (после build.bat)
if exist "%RELEASE_DIR%\%EXE%" (
    echo [RUN] Starting from %RELEASE_DIR%\%EXE%
    cd "%RELEASE_DIR%"
    "%EXE%"
    cd ..
    exit /b 0
)

:: Запуск через dotnet run (из корня репозитория)
if exist "%PROJ%\SpaceDNA.csproj" (
    echo [RUN] Starting via dotnet run from %PROJ%
    cd %PROJ%
    dotnet run -c Release
    cd ..
    exit /b 0
)

if exist "%PROJ%\*.csproj" (
    echo [RUN] Starting via dotnet run from %PROJ%
    cd %PROJ%
    dotnet run -c Release
    cd ..
    exit /b 0
)

echo [ERROR] No build found. Run build.bat first, or ensure %PROJ% folder exists.
exit /b 1
