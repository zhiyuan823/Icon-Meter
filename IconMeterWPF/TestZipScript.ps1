# Test script to verify zip creation functionality
try {
    $releasePath = "bin\Release"
    $zipFileName = "Test-Release.zip"
    $zipFilePath = "$env:USERPROFILE\$zipFileName"
    
    # Check if release folder exists
    if (Test-Path $releasePath) {
        Write-Host "Release path exists: $releasePath"
        
        # Remove existing zip file if it exists
        if (Test-Path $zipFilePath) {
            Remove-Item $zipFilePath -Force
            Write-Host "Removed existing zip file"
        }
        
        # Create the zip file
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($releasePath, $zipFilePath)
        Write-Host "Created zip file: $zipFilePath"
    } else {
        Write-Host "Release path does not exist: $releasePath"
    }
} catch {
    Write-Error "Error creating zip: $($_.Exception.Message)"
}