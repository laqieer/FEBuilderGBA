@echo off
REM Phase 0: Git Submodule Migration Script (Windows)
REM Extracts patch2/ directory into a separate repository and adds as submodule
REM
REM IMPORTANT: Run this from Git Bash instead:
REM    bash scripts/phase0-submodule-migration.sh
REM
REM This batch file is a wrapper to launch the bash script

setlocal

echo ============================================
echo Phase 0: Git Submodule Migration
echo ============================================
echo.
echo This script will extract patch2/ to a separate repository.
echo.
echo IMPORTANT: This operation requires Git Bash for proper execution.
echo.
echo Please run the following command instead:
echo.
echo    bash scripts/phase0-submodule-migration.sh
echo.
echo Or open Git Bash and navigate to this repository, then run:
echo.
echo    ./scripts/phase0-submodule-migration.sh
echo.
pause

REM Attempt to find Git Bash
set GIT_BASH=
if exist "C:\Program Files\Git\bin\bash.exe" set GIT_BASH=C:\Program Files\Git\bin\bash.exe
if exist "C:\Program Files (x86)\Git\bin\bash.exe" set GIT_BASH=C:\Program Files (x86)\Git\bin\bash.exe
if exist "%PROGRAMFILES%\Git\bin\bash.exe" set GIT_BASH=%PROGRAMFILES%\Git\bin\bash.exe

if "%GIT_BASH%"=="" (
    echo.
    echo ERROR: Git Bash not found. Please run the script manually:
    echo    bash scripts/phase0-submodule-migration.sh
    echo.
    pause
    exit /b 1
)

echo.
echo Found Git Bash at: %GIT_BASH%
echo Launching migration script...
echo.

"%GIT_BASH%" "%~dp0phase0-submodule-migration.sh"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Migration script failed with error code %ERRORLEVEL%
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Migration complete!
pause
