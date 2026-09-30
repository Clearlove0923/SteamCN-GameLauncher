[CmdletBinding()]
param(
    [string]$InnoCompiler,
    [string]$KachinaBuilder,
    [string[]]$KachinaPreviousPublishDirectory = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'SteamCN-GameLauncher.csproj'
$release = Get-Content -LiteralPath (Join-Path $repoRoot 'version.json') -Raw | ConvertFrom-Json
$version = $release.version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must be major.minor.patch.' }
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$assemblyName = @($project.Project.PropertyGroup.AssemblyName | Where-Object { $_ })[0]
$projectVersion = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$appInfo = Get-Content -LiteralPath (Join-Path $repoRoot 'AppInfo.cs') -Raw
$installerScript = Join-Path $repoRoot 'SteamCN-GameLauncher.iss'
$installerDefinition = Get-Content -LiteralPath $installerScript -Raw
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'Package.appxmanifest') -Raw
# AppInfo.Version 不带 v 前缀；Git tag 和 Release 路径仍使用 v 前缀。
if ($projectVersion -ne $version -or $appInfo -notmatch ('Version = "' + [regex]::Escape($version) + '"') -or $manifest.Package.Identity.Version -ne "$version.0") {
    throw 'Synchronize version.json, project Version, AppInfo.Version and package Version before publishing.'
}
foreach ($userDataDirectory in @('GameTime', 'HomeCache')) {
    if ($installerDefinition -notmatch ('(?im)^Name:\s*"\{app\}\\' + [regex]::Escape($userDataDirectory) + '";[^\r\n]*Flags:\s*uninsneveruninstall')) {
        throw "Installer must preserve $userDataDirectory during upgrades and uninstall."
    }
    if ($installerDefinition -notmatch ('(?im)^Source:[^\r\n]*Excludes:[^\r\n]*\\' + [regex]::Escape($userDataDirectory) + '\\\*')) {
        throw "Installer must exclude runtime data under $userDataDirectory."
    }
}
if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Install Inno Setup 6.5+ or specify -InnoCompiler.' }

# Each build uses a new directory; stale files can never enter a later installer.
$runRoot = Join-Path $repoRoot ('Output\v' + $version + '-' + [Guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $runRoot 'publish'
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
& dotnet publish $projectPath --configuration Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -p:SatelliteResourceLanguages='zh-CN%3Ben-US' --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
# SatelliteResourceLanguages only filters managed satellites. Windows App SDK also
# copies native MUI locale directories; restrict those to the two shipped languages.
$resolvedPublish = [IO.Path]::GetFullPath($publishDir)
foreach ($localeDir in Get-ChildItem -LiteralPath $resolvedPublish -Directory) {
    if ($localeDir.Name -match '^[a-z]{2,3}-(?:[a-z]{2,8})(?:-[a-z]{2,8})?$' -and
        $localeDir.Name -notin @('zh-CN', 'en-US')) {
        $resolvedLocale = [IO.Path]::GetFullPath($localeDir.FullName)
        if ([IO.Path]::GetDirectoryName($resolvedLocale) -ne $resolvedPublish) {
            throw "Unexpected language directory: $resolvedLocale"
        }
        Remove-Item -LiteralPath $resolvedLocale -Recurse -Force
    }
}
foreach ($required in @("$assemblyName.exe", "$assemblyName.dll", "$assemblyName.deps.json", "$assemblyName.runtimeconfig.json", "$assemblyName.pri", 'coreclr.dll', 'Microsoft.UI.Xaml.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) { throw "Publish output missing: $required" }
}
foreach ($userDataDirectory in @('GameTime', 'HomeCache')) {
    $publishedUserData = Join-Path $publishDir $userDataDirectory
    if (Test-Path -LiteralPath $publishedUserData) {
        $unexpectedFiles = @(Get-ChildItem -LiteralPath $publishedUserData -Recurse -File -ErrorAction Stop)
        if ($unexpectedFiles.Count -gt 0) {
            throw "Publish output contains runtime user data under $userDataDirectory."
        }
    }
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $publishDir
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\languages\LICENSE.txt') -Destination (Join-Path $publishDir 'Inno-Chinese-Translation-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\FFmpeg-GPL-3.0.txt') -Destination $publishDir
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\FFmpeg-SOURCE.txt') -Destination $publishDir

# === Kachina 更新器与在线更新包 =========================================
# 固定 builder 版本与摘要，避免 release 构建随 latest 漂移。
$kachinaBuilderVersion = '0.5.1'
$kachinaBuilderSha256 = 'addaa8e2de08926636f4eabbc9bed4c28e46e92ae55f7cc77d60a34b53bfe8cb'
if (-not $KachinaBuilder) {
    $kachinaToolDir = Join-Path $repoRoot ".build\tools\kachina-builder\$kachinaBuilderVersion"
    $KachinaBuilder = Join-Path $kachinaToolDir 'kachina-builder.exe'
    if (-not (Test-Path -LiteralPath $KachinaBuilder)) {
        New-Item -ItemType Directory -Path $kachinaToolDir -Force | Out-Null
        $kachinaBuilderUrl = "https://github.com/YuehaiTeam/kachina-installer/releases/download/$kachinaBuilderVersion/kachina-builder.exe"
        Invoke-WebRequest -Uri $kachinaBuilderUrl -OutFile $KachinaBuilder
    }
    $actualBuilderHash = (Get-FileHash -LiteralPath $KachinaBuilder -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualBuilderHash -ne $kachinaBuilderSha256) {
        throw "Kachina builder checksum mismatch: $actualBuilderHash"
    }
}
if (-not (Test-Path -LiteralPath $KachinaBuilder)) {
    throw "Kachina builder not found: $KachinaBuilder"
}

$kachinaConfig = Join-Path $repoRoot 'packaging\kachina.config.json'
$kachinaTheme = Join-Path $runRoot 'kachina-theme.css'
$themeTemplate = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging\kachina-theme.css') -Raw
$appImage = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $repoRoot 'Assets\Icons\character-cutout.png')))
[IO.File]::WriteAllText($kachinaTheme, $themeTemplate.Replace('__APP_IMAGE_BASE64__', $appImage), [Text.UTF8Encoding]::new($false))
$kachinaUpdater = Join-Path $publishDir 'SteamCN-GameLauncher.update.exe'
& $KachinaBuilder pack -c $kachinaConfig -t $kachinaTheme -o $kachinaUpdater `
    --icon (Join-Path $repoRoot 'Assets\Icons\SteamCN-GameLauncher.ico')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $kachinaUpdater)) {
    throw "Kachina updater build failed: $LASTEXITCODE"
}

$kachinaMetadata = Join-Path $runRoot 'kachina.metadata.json'
$kachinaHashed = Join-Path $runRoot 'kachina-hashed'
$kachinaGenArgs = @(
    'gen', '-j', '6',
    '-i', $publishDir,
    '-m', $kachinaMetadata,
    '-o', $kachinaHashed,
    '-r', 'Clearlove0923/SteamCN-GameLauncher',
    '-t', $version,
    '-u', $kachinaUpdater
)
foreach ($previousDirectory in $KachinaPreviousPublishDirectory) {
    if (-not (Test-Path -LiteralPath $previousDirectory -PathType Container)) {
        throw "Kachina previous publish directory not found: $previousDirectory"
    }
    $kachinaGenArgs += '--diff-vers'
    $kachinaGenArgs += [IO.Path]::GetFullPath($previousDirectory)
}
& $KachinaBuilder @kachinaGenArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $kachinaMetadata)) {
    throw "Kachina metadata build failed: $LASTEXITCODE"
}

$kachinaInstaller = Join-Path $runRoot "SteamCN-GameLauncher.Install.$version.exe"
& $KachinaBuilder pack -c $kachinaConfig -t $kachinaTheme -m $kachinaMetadata -d $kachinaHashed -o $kachinaInstaller `
    --icon (Join-Path $repoRoot 'Assets\Icons\SteamCN-GameLauncher.ico')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $kachinaInstaller)) {
    throw "Kachina online package build failed: $LASTEXITCODE"
}

# === Inno Setup 通道（现有安装入口保持不变）===============================
& $InnoCompiler "/DMyAppVersion=$version" "/DSourceDir=$publishDir" "/O$runRoot" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }
$installer = Join-Path $runRoot "$assemblyName-v$version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer was not produced.' }
$checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$kachinaChecksum = (Get-FileHash -LiteralPath $kachinaInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLines = @(
    "$checksum  $([IO.Path]::GetFileName($installer))"
    "$kachinaChecksum  $([IO.Path]::GetFileName($kachinaInstaller))"
) -join "`n"
[IO.File]::WriteAllText((Join-Path $runRoot 'SHA256SUMS.txt'), "$checksumLines`n", [Text.UTF8Encoding]::new($false))
Write-Output "Installer: $installer"
Write-Output "SHA256: $checksum"
Write-Output "Kachina package: $kachinaInstaller"
Write-Output "Kachina SHA256: $kachinaChecksum"
Write-Output 'Upload the identical Kachina package to the v<version> release on both CNB and GitHub.'
