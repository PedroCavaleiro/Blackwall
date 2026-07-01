<#
.SYNOPSIS
Blackwall — deploy published artifacts to a remote LXC

.DESCRIPTION
Usage: .\deploy.ps1 -LxcIp <lxc-ip> [-SshUser <ssh-user>]
#>

param (
    [Parameter(Mandatory=$true, Position=0, HelpMessage="IP address of the remote LXC container")]
    [string]$LxcIp,

    [Parameter(Position=1)]
    [string]$SshUser = "root"
)

$ErrorActionPreference = "Stop"

# ─── Variables ────────────────────────────────────────────────────────────────
$Remote = "$SshUser@$LxcIp"
$AppDir = "/opt/blackwall"

# Resolve repo root (2 levels up from script directory)
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$OutDir = Join-Path $RepoRoot ".publish"
$TarFile = Join-Path $RepoRoot "publish.tar.gz"

# ─── Helper Functions ─────────────────────────────────────────────────────────
function Write-Info { param([string]$Message) Write-Host "[INFO]  $Message" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "[OK]    $Message" -ForegroundColor Green }

# ─── Publish ──────────────────────────────────────────────────────────────────
try {
    Write-Info "Publishing Blackwall.Api..."
    $ApiProj = Join-Path $RepoRoot "src\Blackwall.Api\Blackwall.Api.csproj"
    $ApiOut = Join-Path $OutDir "api"
    dotnet publish $ApiProj -c Release -o $ApiOut --nologo -v q

    Write-Info "Publishing Blackwall.Web..."
    $WebProj = Join-Path $RepoRoot "src\Blackwall.Web\Blackwall.Web.csproj"
    $WebOut = Join-Path $OutDir "web"
    dotnet publish $WebProj -c Release -o $WebOut --nologo -v q

    Write-Success "Publish complete."

    # ─── Compress ─────────────────────────────────────────────────────────────
    Write-Info "Compressing published files for transfer..."
    Push-Location $OutDir
    # Using Windows' native tar to compress the contents of the publish folder
    tar -czf $TarFile .
    Pop-Location
    Write-Success "Archive created."

    # ─── Upload ───────────────────────────────────────────────────────────────
    Write-Info "Uploading application payload to $Remote..."
    scp $TarFile "${Remote}:/tmp/publish.tar.gz"
    Write-Success "Upload complete."

    # ─── Remote Execution (Clean, Extract, Restart) ───────────────────────────
    Write-Info "Deploying and restarting services on remote host..."

    # We pass a multi-line script to SSH to handle the remote processing
    $RemoteScript = @"
        # Clear out the target directory but preserve the .env file
        find $AppDir -mindepth 1 -maxdepth 1 ! -name '.env' -exec rm -rf {} + 2>/dev/null || true

        # Extract the new payload
        tar -xzf /tmp/publish.tar.gz -C $AppDir/
        
        # Clean up the temporary archive
        rm /tmp/publish.tar.gz

        # Fix ownership
        chown -R blackwall:blackwall $AppDir/api $AppDir/web

        # Restart and verify services
    systemctl restart blackwall-api blackwall-web
    systemctl is-active blackwall-api
    systemctl is-active blackwall-web
"@

    # Convert Windows CRLF line endings to Linux LF line endings
    $RemoteScript = $RemoteScript.Replace("`r`n", "`n")

    ssh $Remote $RemoteScript
    Write-Success "Deployment done. Both services are running."

}
finally {
    # ─── Cleanup ──────────────────────────────────────────────────────────────
    Write-Info "Cleaning up local temporary files..."
    if (Test-Path $OutDir) { Remove-Item -Path $OutDir -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path $TarFile) { Remove-Item -Path $TarFile -Force -ErrorAction SilentlyContinue }
}