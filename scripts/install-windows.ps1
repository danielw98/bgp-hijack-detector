# Installs every dependency of the project on Windows 10/11 through winget:
# Docker Desktop (runs the lab and the app), the .NET 10 SDK (builds and tests the app),
# MSYS2 with MinGW gcc (builds the C parser for the tests), Python 3.12 with Playwright
# and Chromium (the author scripts and the page gate). Skips what is already installed.
# Entry point: install.bat at the repo root.

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Msys = 'C:\msys64\usr\bin\bash.exe'
$Packages = @(
    @{ Id = 'Docker.DockerDesktop'; Name = 'Docker Desktop'
       Present = { Test-Path "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe" } }
    @{ Id = 'Microsoft.DotNet.SDK.10'; Name = '.NET 10 SDK'
       Present = { (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks) -match '^10\.') } }
    @{ Id = 'MSYS2.MSYS2'; Name = 'MSYS2'
       Present = { Test-Path $Msys } }
    @{ Id = 'Python.Python.3.12'; Name = 'Python 3.12'
       Present = { (Get-Command python -ErrorAction SilentlyContinue) -and ((python --version 2>&1) -match 'Python 3\.1[1-9]') } }
)

function Install-WingetPackage([hashtable]$Package)
{
    if (& $Package.Present)
    {
        Write-Host "[ok] $($Package.Name)"
        return
    }
    Write-Host "[..] $($Package.Name)"
    winget install --id $Package.Id --exact --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw "winget could not install $($Package.Name) ($($Package.Id))" }
}

function Update-SessionPath
{
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                [Environment]::GetEnvironmentVariable('Path', 'User')
}

function Invoke-Checked([string]$Label, [scriptblock]$Command)
{
    Write-Host "[..] $Label"
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Label failed (exit $LASTEXITCODE)" }
}

if (-not (Get-Command winget -ErrorAction SilentlyContinue))
{
    throw 'winget is missing: install "App Installer" from the Microsoft Store, then run install.bat again.'
}

foreach ($package in $Packages) { Install-WingetPackage $package }
Update-SessionPath

Invoke-Checked 'MinGW gcc in MSYS2' { & $Msys -lc 'pacman -S --needed --noconfirm mingw-w64-x86_64-gcc' }
Invoke-Checked 'Playwright for Python' { python -m pip install --upgrade --quiet playwright }
Invoke-Checked 'Chromium for Playwright' { python -m playwright install chromium }
Invoke-Checked 'NuGet packages' { dotnet restore (Join-Path $Root 'BGPWatch.slnx') }

Write-Host ''
Write-Host 'Gata. Daca Docker Desktop tocmai a fost instalat, reporneste calculatorul o data'
Write-Host '(cere WSL 2), apoi dublu-clic pe start.bat.'
