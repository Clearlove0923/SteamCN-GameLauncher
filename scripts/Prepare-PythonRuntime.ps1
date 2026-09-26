[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $repoRoot '.build'
$downloadRoot = Join-Path $buildRoot 'downloads'
$runtimeRoot = Join-Path $buildRoot 'python-runtime'
$archiveName = 'cpython-3.10.21+20260924-x86_64-pc-windows-msvc-install_only.tar.gz'
$archivePath = Join-Path $downloadRoot $archiveName
$archiveUrl = 'https://github.com/astral-sh/python-build-standalone/releases/download/20260924/cpython-3.10.21%2B20260924-x86_64-pc-windows-msvc-install_only.tar.gz'
$archiveSha256 = 'fac843934a3ec32914fda4902f7b4d291e52dceed27ce5a944f4492098c96c05'
$pythonProject = Join-Path $repoRoot 'python'
$pythonExe = Join-Path $runtimeRoot 'python.exe'

function Assert-ChildPath([string]$Path, [string]$Parent) {
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside build directory: $resolvedPath"
    }
}

function Test-Runtime([string]$Executable) {
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { return $false }
    & $Executable -c 'import fastapi, httpx, pydantic, uvicorn' 2>$null
    return $LASTEXITCODE -eq 0
}

function Remove-PythonBuildArtifacts {
    foreach ($generatedPath in @(
        (Join-Path $pythonProject 'build'),
        (Join-Path $pythonProject 'steamcn_home_content.egg-info')
    )) {
        Assert-ChildPath $generatedPath $pythonProject
        if (Test-Path -LiteralPath $generatedPath) {
            Remove-Item -LiteralPath $generatedPath -Recurse -Force
        }
    }
}

# A previous interrupted/local pip build may have left packaging metadata in source.
Remove-PythonBuildArtifacts

if (Test-Runtime $pythonExe) {
    Write-Output "Embedded Python is ready: $pythonExe"
    exit 0
}

New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
if (Test-Path -LiteralPath $archivePath) {
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $archiveSha256) {
        Remove-Item -LiteralPath $archivePath -Force
    }
}

if (-not (Test-Path -LiteralPath $archivePath)) {
    Write-Output "Downloading embedded Python $archiveName ..."
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath -UseBasicParsing
}

$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $archiveSha256) {
    throw "Embedded Python SHA256 mismatch. Expected $archiveSha256, got $actualHash."
}

$stagingRoot = Join-Path $buildRoot ('python-runtime-staging-' + [Guid]::NewGuid().ToString('N'))
Assert-ChildPath $stagingRoot $buildRoot
Assert-ChildPath $runtimeRoot $buildRoot
try {
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    & tar.exe -xzf $archivePath -C $stagingRoot
    if ($LASTEXITCODE -ne 0) { throw "tar.exe failed with exit code $LASTEXITCODE" }

    $extractedPython = Get-ChildItem -LiteralPath $stagingRoot -Recurse -Filter python.exe -File |
        Select-Object -First 1
    if (-not $extractedPython) { throw 'The embedded Python archive does not contain python.exe.' }

    if (Test-Path -LiteralPath $runtimeRoot) {
        Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
    }
    Move-Item -LiteralPath $extractedPython.Directory.FullName -Destination $runtimeRoot
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Write-Output 'Installing Python Worker and its declared dependencies ...'
& $pythonExe -m pip install --disable-pip-version-check --no-warn-script-location --upgrade $pythonProject
if ($LASTEXITCODE -ne 0) { throw "pip failed with exit code $LASTEXITCODE" }
Remove-PythonBuildArtifacts

if (-not (Test-Runtime $pythonExe)) {
    throw 'Embedded Python dependency verification failed.'
}
Write-Output "Embedded Python is ready: $pythonExe"
