@echo off
echo.
echo Creating Icon Meter Release Zip File
echo.

REM Check if Release directory exists
if not exist "bin\Release" (
    echo Error: bin\Release directory does not exist
    echo Please build the project in Release mode first
    pause
    exit /b 1
)

REM Remove existing zip file
if exist "IconMeter-Release.zip" (
    echo Removing existing zip file...
    del "IconMeter-Release.zip"
)

REM Create zip using PowerShell
echo Creating zip file from bin\Release...
powershell -ExecutionPolicy Bypass -Command ^
"try { 
    Add-Type -AssemblyName System.IO.Compression.FileSystem; 
    [System.IO.Compression.ZipFile]::CreateFromDirectory('bin\Release', 'IconMeter-Release.zip');
    Write-Host 'Successfully created IconMeter-Release.zip' -ForegroundColor Green;
} catch {
    Write-Error 'Failed to create zip: $($_.Exception.Message)';
}"

echo.
echo Zip creation process completed.
echo.

pause