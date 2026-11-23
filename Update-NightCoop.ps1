<#
.SYNOPSIS
Keeps the NightCoop co-op mod for Cyberpunk 2077 up to date.

.DESCRIPTION
Reads the release manifest published on nightcoop.net, picks the latest build that is
not withdrawn, downloads the archive matching the installed game version, verifies its
SHA-256 against the manifest and unpacks it into the game folder.

.PARAMETER GamePath
Cyberpunk 2077 installation folder. Detected from Steam, GOG and Epic when omitted.

.PARAMETER GameVersion
Game version as the manifest names it (for example 2.5 or 2.31). Read from the game
executable when omitted.

.PARAMETER Force
Reinstall even if the latest build is already installed.

.PARAMETER Launch
Start the game after the update check.

.PARAMETER CheckOnly
Report whether an update is available without changing anything.

.PARAMETER RegisterTask
Create a scheduled task that runs the updater at logon and once a day.

.PARAMETER UnregisterTask
Remove the scheduled task again.

.PARAMETER TaskName
Name of the scheduled task.

.EXAMPLE
.\Update-NightCoop.ps1

.EXAMPLE
.\Update-NightCoop.ps1 -Launch

.EXAMPLE
.\Update-NightCoop.ps1 -RegisterTask
#>
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '',
    Justification = 'Script parameters are read from the functions below, which the rule does not follow.')]
[CmdletBinding(DefaultParameterSetName = 'Update')]
param(
    [string]$GamePath,

    [string]$GameVersion,

    [Parameter(ParameterSetName = 'Update')]
    [switch]$Force,

    [Parameter(ParameterSetName = 'Update')]
    [switch]$Launch,

    [Parameter(ParameterSetName = 'Check', Mandatory)]
    [switch]$CheckOnly,

    [Parameter(ParameterSetName = 'Register', Mandatory)]
    [switch]$RegisterTask,

    [Parameter(ParameterSetName = 'Unregister', Mandatory)]
    [switch]$UnregisterTask,

    [string]$TaskName = 'NightCoop Updater'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# The progress bar of Windows PowerShell 5.1 slows downloads down by an order of magnitude.
$ProgressPreference = 'SilentlyContinue'

$SiteBaseUrl = 'https://nightcoop.net/'
$ManifestUrl = 'https://nightcoop.net/releases.json'
$DownloadPageUrl = 'https://nightcoop.net/download.html'
$SupportedManifestSchema = 1
$UserAgent = 'NightCoopUpdater/1.0'

$GameExeRelativePath = 'bin\x64\Cyberpunk2077.exe'
$GameProcessName = 'Cyberpunk2077'
$ModPluginRelativePath = 'red4ext\plugins\NightCoop\NightCoop.dll'
$StateFileName = 'NightCoop.updater.json'
$SteamAppId = '1091500'
$GogGameId = '1423049311'
$EpicDisplayName = 'Cyberpunk 2077'

$ExitUpToDate = 0
$ExitFailed = 1
$ExitGameRunning = 2
$ExitUpdateAvailable = 3

$LogDirectory = Join-Path $env:LOCALAPPDATA 'NightCoopUpdater'
$LogFile = Join-Path $LogDirectory 'updater.log'

$script:GameFilesTouched = $false

function Write-Log {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '',
        Justification = 'Console tool: status lines must not end up in the pipeline.')]
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR')][string]$Level = 'INFO'
    )

    $color = switch ($Level) { 'WARN' { 'Yellow' } 'ERROR' { 'Red' } default { 'Gray' } }
    Write-Host $Message -ForegroundColor $color

    # The scheduled task runs hidden, so the file is the only trace of what it did.
    try {
        if (-not (Test-Path -LiteralPath $LogDirectory)) {
            New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
        }
        $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
        Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8
    }
    catch {
        Write-Verbose "Could not write to ${LogFile}: $_"
    }
}

function Get-OptionalProperty {
    param(
        [Parameter(Mandatory)][AllowNull()]$InputObject,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $InputObject) { return $null }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-RegistryValue {
    param(
        [Parameter(Mandatory)][string[]]$KeyPath,
        [Parameter(Mandatory)][string]$Name
    )

    foreach ($key in $KeyPath) {
        $item = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        $value = Get-OptionalProperty -InputObject $item -Name $Name
        if ($value) { return [string]$value }
    }
    return $null
}

function Test-GameFolder {
    param([string]$Path)

    return [bool]$Path -and (Test-Path -LiteralPath (Join-Path $Path $GameExeRelativePath) -PathType Leaf)
}

function Get-SteamInstall {
    $steamRoot = Get-RegistryValue -Name 'InstallPath' -KeyPath @(
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )
    if (-not $steamRoot) { return }

    $libraries = @($steamRoot)
    $libraryIndex = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $libraryIndex) {
        $libraries += Select-String -LiteralPath $libraryIndex -Pattern '^\s*"path"\s+"(.+)"\s*$' |
            ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }

    foreach ($library in ($libraries | Select-Object -Unique)) {
        $appManifest = Join-Path $library "steamapps\appmanifest_$SteamAppId.acf"
        if (-not (Test-Path -LiteralPath $appManifest)) { continue }

        $match = Select-String -LiteralPath $appManifest -Pattern '^\s*"installdir"\s+"(.+)"\s*$' |
            Select-Object -First 1
        if (-not $match) { continue }

        $path = Join-Path $library ('steamapps\common\' + $match.Matches[0].Groups[1].Value)
        if (Test-GameFolder -Path $path) {
            [pscustomobject]@{ Store = 'Steam'; Path = $path; LaunchTarget = "steam://rungameid/$SteamAppId" }
        }
    }
}

function Get-GogInstall {
    $path = Get-RegistryValue -Name 'path' -KeyPath @(
        "HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games\$GogGameId",
        "HKLM:\SOFTWARE\GOG.com\Games\$GogGameId"
    )
    if (Test-GameFolder -Path $path) {
        [pscustomobject]@{ Store = 'GOG'; Path = $path; LaunchTarget = (Join-Path $path $GameExeRelativePath) }
    }
}

function Get-EpicInstall {
    $manifestDirectory = Join-Path $env:ProgramData 'Epic\EpicGamesLauncher\Data\Manifests'
    if (-not (Test-Path -LiteralPath $manifestDirectory)) { return }

    foreach ($file in Get-ChildItem -LiteralPath $manifestDirectory -Filter '*.item' -File) {
        try {
            $item = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        }
        catch {
            Write-Verbose "Skipping unreadable Epic manifest $($file.Name): $_"
            continue
        }

        if ((Get-OptionalProperty -InputObject $item -Name 'DisplayName') -ne $EpicDisplayName) { continue }

        $path = Get-OptionalProperty -InputObject $item -Name 'InstallLocation'
        $appName = Get-OptionalProperty -InputObject $item -Name 'AppName'
        if (Test-GameFolder -Path $path) {
            [pscustomobject]@{
                Store        = 'Epic'
                Path         = $path
                LaunchTarget = "com.epicgames.launcher://apps/${appName}?action=launch&silent=true"
            }
        }
    }
}

function Resolve-GameInstall {
    param([string]$ExplicitPath)

    $detected = @(Get-SteamInstall) + @(Get-GogInstall) + @(Get-EpicInstall)

    if ($ExplicitPath) {
        if (-not (Test-GameFolder -Path $ExplicitPath)) {
            throw "No Cyberpunk 2077 found in '$ExplicitPath' ($GameExeRelativePath is missing)."
        }
        $fullPath = [IO.Path]::GetFullPath($ExplicitPath).TrimEnd('\')
        $known = $detected | Where-Object { [IO.Path]::GetFullPath($_.Path).TrimEnd('\') -ieq $fullPath } |
            Select-Object -First 1
        if ($known) { return $known }
        return [pscustomobject]@{
            Store        = 'Custom'
            Path         = $fullPath
            LaunchTarget = (Join-Path $fullPath $GameExeRelativePath)
        }
    }

    if ($detected.Count -eq 0) {
        throw 'Cyberpunk 2077 was not found in Steam, GOG or Epic. Pass the folder with -GamePath.'
    }
    if ($detected.Count -gt 1) {
        $list = ($detected | ForEach-Object { "$($_.Store): $($_.Path)" }) -join '; '
        throw "Several Cyberpunk 2077 installations found ($list). Pick one with -GamePath."
    }
    return $detected[0]
}

function Get-InstalledGameVersion {
    param(
        [Parameter(Mandatory)][string]$GameRoot,
        [string]$Override
    )

    if ($Override) { return $Override.Trim() }

    $version = (Get-Item -LiteralPath (Join-Path $GameRoot $GameExeRelativePath)).VersionInfo.ProductVersion
    if (-not $version) {
        throw 'Could not read the game version from the executable. Pass it with -GameVersion (for example 2.5).'
    }
    return $version.Trim()
}

function Get-ReleaseManifest {
    param([Parameter(Mandatory)][string]$WorkDirectory)

    # Saved to disk and read back as UTF-8: Windows PowerShell 5.1 decodes JSON bodies
    # without a charset header as Latin-1 and mangles the release titles.
    $target = Join-Path $WorkDirectory 'releases.json'
    Invoke-WebRequest -Uri $ManifestUrl -OutFile $target -UseBasicParsing -UserAgent $UserAgent
    $manifest = Get-Content -LiteralPath $target -Raw -Encoding UTF8 | ConvertFrom-Json

    $schema = Get-OptionalProperty -InputObject $manifest -Name 'schema'
    if ($schema -ne $SupportedManifestSchema) {
        throw "Release manifest has schema '$schema', this updater understands schema $SupportedManifestSchema. Update manually from $DownloadPageUrl."
    }
    if (Get-OptionalProperty -InputObject $manifest -Name 'demo') {
        throw 'The site currently publishes demo data only: there are no files behind the links.'
    }
    return $manifest
}

function Select-LatestRelease {
    param([Parameter(Mandatory)]$Manifest)

    # Same rule as the download page: the build named "latest" unless it was withdrawn,
    # otherwise the first build in the list that is still offered.
    $available = @($Manifest.releases | Where-Object { -not (Get-OptionalProperty -InputObject $_ -Name 'yanked') })
    if ($available.Count -eq 0) { throw 'The release manifest lists no available build.' }

    $latestName = Get-OptionalProperty -InputObject $Manifest -Name 'latest'
    $latest = $available | Where-Object { $_.version -eq $latestName } | Select-Object -First 1
    if ($latest) { return $latest }
    return $available[0]
}

function Select-ModFile {
    param(
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)][string]$GameVersion
    )

    $supported = @(Get-OptionalProperty -InputObject $Release -Name 'game')
    if ($supported -notcontains $GameVersion) {
        throw "NightCoop $($Release.version) supports game versions $($supported -join ', '); installed is $GameVersion."
    }

    $modFiles = @($Release.files | Where-Object { (Get-OptionalProperty -InputObject $_ -Name 'kind') -eq 'mod' })
    $matching = @($modFiles | Where-Object { @(Get-OptionalProperty -InputObject $_ -Name 'game') -contains $GameVersion })
    if ($matching.Count -eq 1) { return $matching[0] }

    # Early builds shipped one archive for every supported game version.
    $universal = @($modFiles | Where-Object { -not (Get-OptionalProperty -InputObject $_ -Name 'game') })
    if ($matching.Count -eq 0 -and $universal.Count -eq 1) { return $universal[0] }

    throw "NightCoop $($Release.version) has no unambiguous archive for game version $GameVersion."
}

function Resolve-DownloadUri {
    param([Parameter(Mandatory)][string]$RelativeUrl)

    $base = [Uri]$SiteBaseUrl
    $uri = New-Object System.Uri($base, $RelativeUrl)
    # The manifest is the trust anchor for the checksums, so it must not be able to
    # send the download to another host or downgrade it to plain HTTP.
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne $base.Host) {
        throw "Refusing to download from '$uri': not an HTTPS address on $($base.Host)."
    }
    return $uri
}

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory)]$ModFile,
        [Parameter(Mandatory)][string]$WorkDirectory
    )

    $expectedHash = Get-OptionalProperty -InputObject $ModFile -Name 'sha256'
    if (-not $expectedHash) { throw "The manifest gives no SHA-256 for $($ModFile.name)." }

    $uri = Resolve-DownloadUri -RelativeUrl $ModFile.url
    $target = Join-Path $WorkDirectory 'mod.zip'
    Write-Log "Downloading $($ModFile.name) ($([math]::Round($ModFile.size / 1MB, 1)) MB)"
    Invoke-WebRequest -Uri $uri -OutFile $target -UseBasicParsing -UserAgent $UserAgent

    $actualHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($actualHash -ine $expectedHash) {
        throw "SHA-256 mismatch for $($ModFile.name): expected $expectedHash, got $($actualHash.ToLower()). Nothing was installed."
    }
    Write-Log 'SHA-256 verified'
    return $target
}

function Expand-ArchiveSafely {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$Destination
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $relativePaths = New-Object System.Collections.Generic.List[string]

    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name) { continue }

            # Entry names are attacker-shaped input: "..\" segments must not escape the folder.
            $target = [IO.Path]::GetFullPath((Join-Path $root $entry.FullName))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Archive entry '$($entry.FullName)' points outside the target folder."
            }

            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            $relativePaths.Add($target.Substring($root.Length))
        }
    }
    finally {
        $archive.Dispose()
    }

    if ($relativePaths.Count -eq 0) { throw 'The archive is empty.' }
    return $relativePaths.ToArray()
}

function Copy-ModFile {
    param(
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$GameRoot,
        [Parameter(Mandatory)][string[]]$RelativePath
    )

    foreach ($relative in $RelativePath) {
        $target = Join-Path $GameRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        $script:GameFilesTouched = $true
        Copy-Item -LiteralPath (Join-Path $StagingRoot $relative) -Destination $target -Force
    }
}

function Clear-StaleModFile {
    param(
        [Parameter(Mandatory)][string]$GameRoot,
        [string[]]$PreviousPath,
        [Parameter(Mandatory)][string[]]$CurrentPath
    )

    $root = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\') + '\'
    foreach ($relative in $PreviousPath) {
        if ($CurrentPath -contains $relative) { continue }
        # The archive also carries RED4ext, which other mods load too: only files inside
        # NightCoop's own folders are ever removed.
        if (-not ($relative -split '\\' | Where-Object { $_ -like 'NightCoop*' })) { continue }

        $target = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            Remove-Item -LiteralPath $target -Force
            Write-Log "Removed file no longer shipped: $relative"
        }
    }
}

function Read-InstallState {
    param([Parameter(Mandatory)][string]$GameRoot)

    $path = Join-Path $GameRoot $StateFileName
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try {
        return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Write-Log "Ignoring unreadable state file ${path}: $_" -Level WARN
        return $null
    }
}

function Save-InstallState {
    param(
        [Parameter(Mandatory)][string]$GameRoot,
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)]$ModFile,
        [Parameter(Mandatory)][string]$GameVersion,
        [Parameter(Mandatory)][string[]]$RelativePath
    )

    $state = [ordered]@{
        version       = $Release.version
        protocol      = Get-OptionalProperty -InputObject $Release -Name 'protocol'
        gameVersion   = $GameVersion
        archiveSha256 = $ModFile.sha256
        installedAt   = (Get-Date).ToUniversalTime().ToString('s') + 'Z'
        files         = $RelativePath
    }
    $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $GameRoot $StateFileName) -Encoding UTF8
}

function Test-UpToDate {
    param(
        $State,
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)]$ModFile,
        [Parameter(Mandatory)][string]$GameRoot
    )

    # The archive hash rather than the version alone: after a game patch the same mod
    # version needs the archive built for the new game version.
    return ($null -ne $State) -and
        ((Get-OptionalProperty -InputObject $State -Name 'version') -eq $Release.version) -and
        ((Get-OptionalProperty -InputObject $State -Name 'archiveSha256') -eq $ModFile.sha256) -and
        (Test-Path -LiteralPath (Join-Path $GameRoot $ModPluginRelativePath))
}

function Test-GameRunning {
    return [bool](Get-Process -Name $GameProcessName -ErrorAction SilentlyContinue)
}

function Get-LocalizedText {
    param($Text)

    if ($Text -is [string]) { return $Text }
    return Get-OptionalProperty -InputObject $Text -Name 'en'
}

function Invoke-UpdateCheck {
    param(
        [Parameter(Mandatory)]$Install,
        [Parameter(Mandatory)][string]$WorkDirectory,
        [switch]$ReportOnly,
        [switch]$Reinstall
    )

    $gameRoot = $Install.Path
    $detectedGameVersion = Get-InstalledGameVersion -GameRoot $gameRoot -Override $GameVersion
    Write-Log "Game: $($Install.Store), version $detectedGameVersion, $gameRoot"

    $manifest = Get-ReleaseManifest -WorkDirectory $WorkDirectory
    $release = Select-LatestRelease -Manifest $manifest
    $modFile = Select-ModFile -Release $release -GameVersion $detectedGameVersion
    $state = Read-InstallState -GameRoot $gameRoot

    $installedVersion = Get-OptionalProperty -InputObject $state -Name 'version'
    if (-not $installedVersion) { $installedVersion = 'unknown (not installed by this updater)' }

    if (-not $Reinstall -and (Test-UpToDate -State $state -Release $release -ModFile $modFile -GameRoot $gameRoot)) {
        Write-Log "NightCoop $($release.version) is up to date"
        return $ExitUpToDate
    }

    Write-Log "Installed: $installedVersion. Available: $($release.version) from $($release.date), protocol $(Get-OptionalProperty -InputObject $release -Name 'protocol')"
    $title = Get-LocalizedText -Text (Get-OptionalProperty -InputObject $release -Name 'title')
    if ($title) { Write-Log "$title - $DownloadPageUrl" }

    if ($ReportOnly) { return $ExitUpdateAvailable }

    if (Test-GameRunning) {
        Write-Log 'Cyberpunk 2077 is running: its files are locked. Close the game and run the updater again.' -Level WARN
        return $ExitGameRunning
    }

    $archive = Get-VerifiedArchive -ModFile $modFile -WorkDirectory $WorkDirectory
    $staging = Join-Path $WorkDirectory 'staging'
    $files = Expand-ArchiveSafely -ArchivePath $archive -Destination $staging

    Copy-ModFile -StagingRoot $staging -GameRoot $gameRoot -RelativePath $files
    $previousFiles = @(Get-OptionalProperty -InputObject $state -Name 'files') | Where-Object { $_ }
    Clear-StaleModFile -GameRoot $gameRoot -PreviousPath $previousFiles -CurrentPath $files
    Save-InstallState -GameRoot $gameRoot -Release $release -ModFile $modFile -GameVersion $detectedGameVersion -RelativePath $files

    Write-Log "NightCoop $($release.version) installed ($($files.Count) files)"
    return $ExitUpToDate
}

function Invoke-GameLaunch {
    param([Parameter(Mandatory)]$Install)

    if (Test-GameRunning) {
        Write-Log 'Cyberpunk 2077 is already running'
        return
    }
    Write-Log "Starting Cyberpunk 2077 ($($Install.Store))"
    Start-Process -FilePath $Install.LaunchTarget
}

function Register-UpdaterTask {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Only reachable through the explicit -RegisterTask switch.')]
    param()

    $arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`""
    # A trailing backslash would escape the closing quote of the argument.
    if ($GamePath) { $arguments += " -GamePath `"$($GamePath.TrimEnd('\'))`"" }
    if ($GameVersion) { $arguments += " -GameVersion `"$GameVersion`"" }

    $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments
    $triggers = @(
        (New-ScheduledTaskTrigger -AtLogOn -User $user),
        (New-ScheduledTaskTrigger -Daily -At '18:00')
    )
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings -Principal $principal -Description 'Keeps the NightCoop mod for Cyberpunk 2077 up to date.' -Force | Out-Null
    Write-Log "Scheduled task '$TaskName' registered: at logon and daily at 18:00. Log: $LogFile"
}

function Unregister-UpdaterTask {
    if (-not (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
        Write-Log "Scheduled task '$TaskName' does not exist"
        return
    }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Log "Scheduled task '$TaskName' removed"
}

function Invoke-Main {
    if ($UnregisterTask) {
        Unregister-UpdaterTask
        return $ExitUpToDate
    }

    # Windows PowerShell 5.1 still negotiates TLS 1.0 by default on older systems.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $install = Resolve-GameInstall -ExplicitPath $GamePath

    if ($RegisterTask) {
        Register-UpdaterTask
        return $ExitUpToDate
    }

    $workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('NightCoopUpdater-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $workDirectory -Force | Out-Null
    try {
        $exitCode = Invoke-UpdateCheck -Install $install -WorkDirectory $workDirectory -ReportOnly:$CheckOnly -Reinstall:$Force
    }
    catch {
        # Being offline should not keep anyone from playing with the build they have;
        # a half-copied build should.
        if (-not $Launch -or $script:GameFilesTouched) { throw }
        Write-Log "Update check failed, starting with the installed build: $($_.Exception.Message)" -Level WARN
        $exitCode = $ExitFailed
    }
    finally {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($Launch) { Invoke-GameLaunch -Install $install }
    return $exitCode
}

try {
    $exitCode = Invoke-Main
}
catch {
    Write-Log $_.Exception.Message -Level ERROR
    if ($script:GameFilesTouched) {
        Write-Log 'The game folder was partly updated. Run the updater again before playing.' -Level ERROR
    }
    $exitCode = $ExitFailed
}
exit $exitCode
