@echo off
setlocal enabledelayedexpansion

echo ========================================
echo SpaceBall Build Script
echo ========================================
echo.

set PROJ=SpaceBall
set RELEASE_DIR=release
set LOG_FILE=build_full.log
set DATETIME=%date% %time%

echo [BUILD] Starting build at %DATETIME%
echo.

:: Create release directory
if not exist "%RELEASE_DIR%" (
    echo [BUILD] Creating release directory...
    mkdir "%RELEASE_DIR%"
)

:: Clean and build
echo [BUILD] Building project...
cd %PROJ%
dotnet publish -c Release -o ..\%RELEASE_DIR% > ..\%LOG_FILE% 2>&1
set BUILD_RESULT=!errorlevel!
cd ..

if %BUILD_RESULT% neq 0 (
    echo [ERROR] Build failed with code %BUILD_RESULT%
    echo See %LOG_FILE% for details
    type %LOG_FILE%
    echo.
    pause
    exit /b %BUILD_RESULT%
)

:: Copy config file
echo [BUILD] Copying config file...
copy /Y "%PROJ%\planet_config.json" "%RELEASE_DIR%\" > nul

:: Show result
echo.
echo [BUILD] Build successful!
echo.

:: Show output
echo Release directory contents:
dir /b "%RELEASE_DIR%"
echo.

:: Show exe path
for /f "delims=" %%i in ('dir /s /b "%RELEASE_DIR%\*.exe" 2^>nul') do (
    echo [RUN] Executable: %%i
)

echo.
echo [INFO] Log file: %LOG_FILE%
echo.
echo To run: run.bat   or   %RELEASE_DIR%\SpaceDNA.exe
echo [DONE] Build complete!
echo.
pause

endlocal
