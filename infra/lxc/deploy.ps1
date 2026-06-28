#Requires -Version 5.1
# Blackwall — deploy published artifacts to a remote LXC
# Usage: .\deploy.ps1 -LxcHost <ip> [-SshUser <user>]
# Example: .\deploy.ps1 -LxcHost 192.168.1.50
# Example: .\deploy.ps1 -LxcHost 192.168.1.50 -SshUser root
param(
    [Parameter(Mandatory)][string]$LxcHost,
    [string]$SshUser = "root"
)

$ErrorActionPreference = "Stop"

$Remote   = "${SshUser}@${LxcHost}"
$AppDir   = "/opt/blackwall"
$RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$OutDir   = Join-Path $RepoRoot ".publish"

function Info($msg)    { Write-Host "[INFO]  $msg" -ForegroundColor Cyan }
function Success($msg) { Write-Host "[OK]    $msg" -ForegroundColor Green }
function Fail($msg)    { Write-Host "[ERROR] $msg" -ForegroundColor Red; exit 1 }

# ─── Publish ──────────────────────────────────────────────────────────────────
Info "Publishing Blackwall.Api..."
dotnet publish "$RepoRoot\src\Blackwall.Api\Blackwall.Api.csproj" `
    -c Release -o "$OutDir\api" --nologo -v q
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed for Blackwall.Api" }

Info "Publishing Blackwall.Web..."
dotnet publish "$RepoRoot\src\Blackwall.Web\Blackwall.Web.csproj" `
    -c Release -o "$OutDir\web" --nologo -v q
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed for Blackwall.Web" }

Success "Publish complete."

# ─── Archive (Windows tar, available since Windows 10 1803) ──────────────────
Info "Archiving artifacts..."
tar -czf "$OutDir\api.tar.gz" -C "$OutDir\api" .
tar -czf "$OutDir\web.tar.gz" -C "$OutDir\web" .

# ─── Upload ───────────────────────────────────────────────────────────────────
Info "Uploading API archive to ${Remote}:${AppDir}/ ..."
scp "$OutDir\api.tar.gz" "${Remote}:${AppDir}/api.tar.gz"
if ($LASTEXITCODE -ne 0) { Fail "scp failed for API" }

Info "Uploading Web archive to ${Remote}:${AppDir}/ ..."
scp "$OutDir\web.tar.gz" "${Remote}:${AppDir}/web.tar.gz"
if ($LASTEXITCODE -ne 0) { Fail "scp failed for Web" }

Success "Upload complete."

# ─── Extract, fix ownership, restart ─────────────────────────────────────────
Info "Extracting, fixing ownership and restarting services..."
$remoteCmd = @"
set -e
rm -rf $AppDir/api $AppDir/web
mkdir -p $AppDir/api $AppDir/web
tar -xzf $AppDir/api.tar.gz -C $AppDir/api
tar -xzf $AppDir/web.tar.gz -C $AppDir/web
rm -f $AppDir/api.tar.gz $AppDir/web.tar.gz
chown -R blackwall:blackwall $AppDir/api $AppDir/web
systemctl restart blackwall-api blackwall-web
systemctl is-active blackwall-api
systemctl is-active blackwall-web
"@

$tempFile = [System.IO.Path]::GetTempFileName() + ".sh"
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($tempFile, ($remoteCmd -replace "`r`n", "`n"), $utf8NoBom)

$remoteScript = "/tmp/blackwall_deploy.sh"
scp -q $tempFile "${Remote}:${remoteScript}"
Remove-Item $tempFile
if ($LASTEXITCODE -ne 0) { Fail "scp of deploy script failed" }

ssh $Remote "bash $remoteScript; rm -f $remoteScript"
if ($LASTEXITCODE -ne 0) { Fail "Remote setup or service restart failed" }

Success "Deployment done. Both services are running."

# ─── Cleanup local artifacts ─────────────────────────────────────────────────
Remove-Item -Recurse -Force $OutDir
