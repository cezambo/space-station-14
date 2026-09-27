# RA-04: start local SS14 server, wait for port, then start client (Windows).
param(
    [switch]$Release,
    [switch]$NoClient,
    [string]$Config = "",
    [int]$Port = 1212,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

if ([string]::IsNullOrEmpty($Config)) {
    $Config = Join-Path $Root "Cognition/config/server_local.toml"
}
if ($Release) { $Configuration = "Release" }

if (-not (Test-Path $Config)) {
    throw "Config not found: $Config"
}

$dataDir = Join-Path $Root "Cognition/data/server"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

Write-Host "Starting server ($Configuration) with $Config ..."
$serverArgs = @(
    "run", "--project", "Content.Server", "-c", $Configuration, "--no-build", "--",
    "--config-file", $Config,
    "--data-dir", $dataDir
)
$server = Start-Process -FilePath "dotnet" -ArgumentList $serverArgs -PassThru -NoNewWindow

try {
    Write-Host "Waiting for port $Port ..."
    $opened = $false
    for ($i = 0; $i -lt 120; $i++) {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $tcp.Connect("127.0.0.1", $Port)
            $tcp.Close()
            $opened = $true
            break
        } catch {
            if ($server.HasExited) { throw "Server exited before port opened." }
            Start-Sleep -Seconds 1
        }
    }
    if (-not $opened) { throw "Timeout waiting for port $Port." }
    Write-Host "Server port $Port is open."

    if ($NoClient) {
        Write-Host "Headless mode (--NoClient). Press Ctrl+C to stop."
        Wait-Process -Id $server.Id
        return
    }

    Write-Host "Starting client (--connect) ..."
    & dotnet run --project Content.Client -c $Configuration --no-build -- `
        --connect --connect-address "localhost:$Port"
}
finally {
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
}
