<#
.SYNOPSIS
    Creates or removes the IconMeter startup scheduled task.
    This script is intended to run with administrator privileges
    (it is launched via "runas" from the main application) so it
    can register a task that runs with the highest privileges.

.PARAMETER Action
    "create" to register the task, "delete" to remove it.

.PARAMETER TaskName
    The scheduled task name.

.PARAMETER Executable
    Full path of the IconMeter executable (required for "create").

.EXIT CODES
    0  success
    1  invalid arguments
    2  failed to create/delete the task
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("create", "delete")]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$TaskName,

    [string]$Executable = ""
)

$ErrorActionPreference = "Stop"

function New-TaskXml {
    param([string]$Command)

    $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name

    # The task runs at logon with the highest available privileges so the
    # application starts with administrator rights at startup without a UAC prompt.
    $xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Starts IconMeter at logon with administrator privileges.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$user</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>"$Command"</Command>
    </Exec>
  </Actions>
</Task>
"@
    return $xml
}

try {
    if ($Action -eq "delete") {
        # delete the scheduled task (and its parent folder if it becomes empty)
        $folder = Split-Path -Parent "$TaskName"
        schtasks.exe /delete /tn "$TaskName" /f | Out-Null
        if ($folder -and $folder -ne "\") {
            # remove the (now empty) IconMeter folder; ignore errors if not empty
            schtasks.exe /delete /tn "$folder" /f | Out-Null
        }
        # either way, treat deletion as success
        exit 0
    }

    if ($Action -eq "create") {
        if (-not (Test-Path $Executable)) {
            Write-Error "Executable not found: $Executable"
            exit 1
        }

        # ensure the parent folder exists (e.g. "IconMeter")
        $folder = Split-Path -Parent "$TaskName"
        if ($folder -and $folder -ne "\") {
            # schtasks creates the folder implicitly when the task is created, but we
            # create it explicitly first so it shows up even if the task creation fails
            & schtasks.exe /create /tn "$folder" /f | Out-Null
        }

        $xml = New-TaskXml -Command $Executable
        $tempXml = [System.IO.Path]::GetTempFileName() + ".xml"
        [System.IO.File]::WriteAllText($tempXml, $xml, [System.Text.Encoding]::Unicode)

        # create the task from the XML; because this script is already elevated,
        # Register-ScheduledTask / schtasks can persist the HighestAvailable run level
        schtasks.exe /create /tn "$TaskName" /xml "$tempXml" /f | Out-Null
        Remove-Item $tempXml -Force -ErrorAction SilentlyContinue

        if ($LASTEXITCODE -ne 0) {
            Write-Error "schtasks /create failed with exit code $LASTEXITCODE"
            exit 2
        }
        exit 0
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 2
}
