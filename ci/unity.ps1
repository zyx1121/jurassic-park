# Run Unity in batchmode and wait for it. Unity.exe is a GUI-subsystem binary, so `&` returns
# immediately in PowerShell; Start-Process -Wait is required to get a real exit code.
param(
    [Parameter(Mandatory = $true)][string]$UnityExe,
    [Parameter(Mandatory = $true)][string]$LogFile,
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$UnityArgs
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null
$args = @('-batchmode', '-nographics', '-logFile', $LogFile) + $UnityArgs
Write-Host "unity $($args -join ' ')"
$p = Start-Process -FilePath $UnityExe -ArgumentList $args -Wait -PassThru -NoNewWindow
Write-Host "unity exit code $($p.ExitCode)"
if ($p.ExitCode -ne 0) {
    if (Test-Path $LogFile) { Get-Content $LogFile -Tail 120 }
    exit $p.ExitCode
}
