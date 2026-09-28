[CmdletBinding()]
param([string]$InnoCompiler)

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
# v3.0.0 起 AppInfo.Version 不带 v 前缀（Velopack 解析需要 SemVer）。
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

# === Inno Setup 通道（官网下载区主链接，老用户主入口）====================
# v3.0.0 起 Inno Setup 产物用于：① 官网下载页首次安装 ② 老用户跨代升级
# 已通过此渠道安装的用户后续小版本仍走 Velopack 自动更新（v3.1.0+）。
& $InnoCompiler "/DMyAppVersion=$version" "/DSourceDir=$publishDir" "/O$runRoot" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }
$installer = Join-Path $runRoot "$assemblyName-v$version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer was not produced.' }
$checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $runRoot 'SHA256SUMS.txt'), "$checksum  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Installer: $installer"
Write-Output "SHA256: $checksum"

# === Velopack 通道（自动更新清单 + Update.exe 子进程）====================
# 产物：io.steamcn.launcher-Setup.exe（NSIS，首次安装给自动更新用户用）
#      io.steamcn.launcher-{version}-full.nupkg（全量更新包）
#      io.steamcn.launcher-{version}-delta.nupkg（差分包，需要 --previousPackDir）
#      releases.stable.json（更新清单）
#      RELEASES（legacy 兼容）
$veloDir = Join-Path $runRoot 'velopack'
New-Item -ItemType Directory -Path $veloDir -Force | Out-Null
$veloArgs = @(
    'pack',
    '--packId', 'io.steamcn.launcher',
    '--packVersion', $version,
    '--packDir', $publishDir,
    '--mainExe', "$assemblyName.exe",
    '--packTitle', 'Steam国服游戏启动器',
    '--icon', (Join-Path $repoRoot 'Assets\Icons\SteamCN-GameLauncher.ico'),
    '--outputDir', $veloDir
)
# 如果上一版本的 RELEASES 存在，自动生成 delta 包（调用方需事先把上一版本产物放到 $runRoot\velopack\RELEASES）。
$prevReleases = Join-Path $veloDir 'RELEASES'
if (Test-Path -LiteralPath $prevReleases) {
    $veloArgs += '--deltaReleases'
    $veloArgs += $prevReleases
}
& vpk @veloArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed: $LASTEXITCODE" }
foreach ($required in @('io.steamcn.launcher-Setup.exe', 'io.steamcn.launcher-' + $version + '-full.nupkg', 'releases.stable.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $veloDir $required))) {
        # vpk 在某些版本把 RELEASES.json 命名为 releases.stable.json；个别旧版本叫 releases.json
        $alt = Join-Path $veloDir 'releases.json'
        if ($required -eq 'releases.stable.json' -and (Test-Path -LiteralPath $alt)) { continue }
        throw "Velopack output missing: $required"
    }
}
Write-Output "Velopack artifacts: $veloDir"
Get-ChildItem -LiteralPath $veloDir | ForEach-Object { Write-Output ("  {0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name) }
