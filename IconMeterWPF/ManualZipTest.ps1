# Manual Zip Creation Test Script
# This script tests the zip creation logic without requiring the full project build

Write-Host "Testing zip creation functionality..." -ForegroundColor Green

try {
    # Define paths
    $releasePath = "bin\Release"
    $zipFileName = "Test-Release-Manual.zip"
    $zipFilePath = "$env:USERPROFILE\$zipFileName"
    
    Write-Host "Checking release path: $releasePath" -ForegroundColor Yellow
    
    # Check if release folder exists
    if (Test-Path $releasePath) {
        Write-Host "✓ Release path exists" -ForegroundColor Green
        
        # Remove existing zip file if it exists
        if (Test-Path $zipFilePath) {
            Remove-Item $zipFilePath -Force
            Write-Host "✓ Removed existing zip file" -ForegroundColor Green
        }
        
        # Create the zip file
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($releasePath, $zipFilePath)
        Write-Host "✓ Created zip file successfully: $zipFilePath" -ForegroundColor Green
        
        # Verify zip file was created
        if (Test-Path $zipFilePath) {
            $fileInfo = Get-Item $zipFilePath
            Write-Host "✓ Zip file size: $($fileInfo.Length) bytes" -ForegroundColor Green
        }
    } else {
        Write-Host "✗ Release path does not exist: $releasePath" -ForegroundColor Red
        Write-Host "This is expected if the project hasn't been built yet." -ForegroundColor Yellow
    }
    
} catch {
    Write-Error "Error in zip creation test: $($_.Exception.Message)"
    Write-Host "This might be due to missing dependencies or build artifacts." -ForegroundColor Yellow
}

Write-Host "Test completed." -ForegroundColor Green