<#
  debloat.ps1 - TinyShell debloat engine

    powershell -File debloat.ps1 -List
    powershell -File debloat.ps1 -RestorePoint
    powershell -File debloat.ps1 -Apply -Ids 'appx.bing','telemetry.allow' -ProgressFile out.log
#>
[CmdletBinding()]
param(
    [switch]$List,
    [switch]$Apply,
    [switch]$RestorePoint,
    [string[]]$Ids = @(),
    [string]$ProgressFile
)

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'
$script:ProgressPath   = $ProgressFile

function Write-Log {
    param([string]$Text)
    Write-Host $Text
    if ($script:ProgressPath) {
        try { Add-Content -LiteralPath $script:ProgressPath -Value $Text -Encoding UTF8 } catch {}
    }
}

# ----------------------------------------------------------------------
# Helpers used by catalog entries
# ----------------------------------------------------------------------

function Remove-AppxPattern {
    param([string[]]$Patterns)
    foreach ($p in $Patterns) {
        Get-AppxPackage -AllUsers -Name $p -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop
                Write-Log "   removed  $($_.Name)"
            } catch {
                Write-Log "   skip     $($_.Name) ($($_.Exception.Message.Split([char]10)[0]))"
            }
        }
        Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like $p } |
            ForEach-Object {
                try {
                    Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop | Out-Null
                    Write-Log "   deprov   $($_.DisplayName)"
                } catch {
                    Write-Log "   skip     $($_.DisplayName)"
                }
            }
    }
}

function Set-Reg {
    param([string]$Path, [string]$Name, $Value, [string]$Type = 'DWord')
    try {
        if (-not (Test-Path -LiteralPath $Path)) { New-Item -Path $Path -Force | Out-Null }
        New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value `
                         -PropertyType $Type -Force | Out-Null
        Write-Log "   set      $Path\$Name = $Value"
    } catch {
        Write-Log "   fail     $Path\$Name ($($_.Exception.Message.Split([char]10)[0]))"
    }
}

function Remove-Reg {
    param([string]$Path, [string]$Name)
    try {
        if (Test-Path -LiteralPath $Path) {
            Remove-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction SilentlyContinue
            Write-Log "   cleared  $Path\$Name"
        }
    } catch { Write-Log "   skip     $Path\$Name" }
}

function Disable-Task {
    param([string[]]$Paths)
    foreach ($t in $Paths) {
        try {
            $task = Get-ScheduledTask -TaskPath (Split-Path $t -Parent) `
                                      -TaskName (Split-Path $t -Leaf) -ErrorAction Stop
            if ($task.State -ne 'Disabled') {
                Disable-ScheduledTask -InputObject $task -ErrorAction Stop | Out-Null
                Write-Log "   disabled $t"
            }
        } catch {
            Write-Log "   skip     $t"
        }
    }
}

function Disable-Svc {
    param([string[]]$Names)
    foreach ($n in $Names) {
        try {
            $svc = Get-Service -Name $n -ErrorAction Stop
            if ($svc.Status -eq 'Running') { Stop-Service -Name $n -Force -ErrorAction SilentlyContinue }
            Set-Service -Name $n -StartupType Disabled -ErrorAction Stop
            Write-Log "   disabled service $n"
        } catch {
            Write-Log "   skip     service $n"
        }
    }
}

# ----------------------------------------------------------------------
# Restore point
# ----------------------------------------------------------------------

function New-TinyShellRestorePoint {
    try {
        $sr = Get-Service -Name 'VSS' -ErrorAction Stop
        if ($sr.Status -ne 'Running') { Start-Service VSS -ErrorAction SilentlyContinue }
        try { Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction Stop } catch {}

        Checkpoint-Computer -Description "TinyShell debloat $(Get-Date -Format 'yyyy-MM-dd HH:mm')" `
                             -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
        Write-Log "   restore point created"
        return $true
    } catch {
        Write-Log "   restore point FAILED: $($_.Exception.Message.Split([char]10)[0])"
        Write-Log "   (Windows only allows one System Protection restore point per 24h by default"
        Write-Log "    on some SKUs - this is not necessarily an error on your part.)"
        return $false
    }
}

# ----------------------------------------------------------------------
# Catalog
#
# Descriptions are written for someone who isn't a sysadmin: what it does
# in plain terms and, where it matters, what you give up. Risk levels:
#   Safe       - cosmetic/telemetry only, nothing you'd notice breaking
#   Moderate   - turns off a feature some people actually use
#   Aggressive - can break things (Game Pass, sync, enterprise policy)
# ----------------------------------------------------------------------

$Catalog = @(

    # ---------------- Preinstalled apps ----------------
    @{ Id='appx.news';      Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='News & Interests'
       Desc="The headlines widget that shows up whether you asked for it or not. Removing it doesn't affect anything else."
       Script={ Remove-AppxPattern 'Microsoft.BingNews' } }

    @{ Id='appx.weather';   Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Weather app'
       Desc="Microsoft's built-in weather app. Safe to remove if you check weather on your phone or in a browser instead."
       Script={ Remove-AppxPattern 'Microsoft.BingWeather' } }

    @{ Id='appx.gethelp';   Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Get Help'
       Desc="Opens a Microsoft support chat window. You'll never miss it - normal troubleshooting doesn't need it."
       Script={ Remove-AppxPattern 'Microsoft.GetHelp' } }

    @{ Id='appx.tips';      Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Tips / Get Started'
       Desc="The 'welcome to Windows' tutorial app that pesters new PCs. Useless once you know how to use a computer."
       Script={ Remove-AppxPattern 'Microsoft.Getstarted','Microsoft.WindowsTips' } }

    @{ Id='appx.officehub'; Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Office promo app'
       Desc="A shortcut that nags you to buy a Microsoft 365 subscription. Doesn't remove actual Office if you have it installed separately."
       Script={ Remove-AppxPattern 'Microsoft.MicrosoftOfficeHub' } }

    @{ Id='appx.solitaire'; Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Solitaire Collection'
       Desc="Microsoft's card games app - the free version shows ads between hands. Remove if you don't play it."
       Script={ Remove-AppxPattern 'Microsoft.MicrosoftSolitaireCollection' } }

    @{ Id='appx.people';    Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='People app'
       Desc="A contacts app most people never open, since phone contacts live on your phone. Safe to remove."
       Script={ Remove-AppxPattern 'Microsoft.People' } }

    @{ Id='appx.maps';      Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Maps app'
       Desc="Windows' built-in Maps app. Remove it if you use Google Maps or your phone for directions instead."
       Script={ Remove-AppxPattern 'Microsoft.WindowsMaps' } }

    @{ Id='appx.feedback';  Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Feedback Hub'
       Desc="Where you'd send Microsoft feedback about Windows bugs. If you've never used it, you don't need it."
       Script={ Remove-AppxPattern 'Microsoft.WindowsFeedbackHub' } }

    @{ Id='appx.media';     Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Groove Music / Movies & TV'
       Desc="Old Microsoft media apps that most people replaced with Spotify, Netflix, or VLC years ago."
       Script={ Remove-AppxPattern 'Microsoft.ZuneMusic','Microsoft.ZuneVideo' } }

    @{ Id='appx.mixed';     Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='3D Viewer / Print 3D / Mixed Reality'
       Desc="Leftover apps from Microsoft's old push into 3D and VR. Almost nobody uses these."
       Script={ Remove-AppxPattern 'Microsoft.Microsoft3DViewer','Microsoft.Print3D','Microsoft.MixedReality.Portal' } }

    @{ Id='appx.skype';     Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Skype (Store version)'
       Desc="The Store-installed Skype app. If you use Teams, Zoom, Discord, or a separately-installed Skype, this is just clutter."
       Script={ Remove-AppxPattern 'Microsoft.SkypeApp' } }

    @{ Id='appx.teams';     Category='Apps you probably never open'; Risk='Moderate'; Default=$true
       Name='Teams (personal/consumer)'
       Desc="The free, personal version of Teams that Windows installs automatically - not the work/school one your employer gives you. Removing it turns off the taskbar chat icon."
       Script={ Remove-AppxPattern 'MicrosoftTeams','MSTeams' } }

    @{ Id='appx.todos';     Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='To Do, Power Automate, Alarms'
       Desc="A grab-bag of productivity apps most people use a different app for instead (Todoist, phone alarms, etc.)."
       Script={ Remove-AppxPattern 'Microsoft.Todos','Microsoft.PowerAutomateDesktop','Microsoft.WindowsAlarms' } }

    @{ Id='appx.promo';     Category='Apps you probably never open'; Risk='Safe'; Default=$true
       Name='Third-party promo apps (Disney+, Spotify, TikTok, etc.)'
       Desc="Ads for other companies' apps that come pre-installed on new PCs, sometimes without even being opened once. Doesn't touch the app if you already use and like it - only removes the unused stub."
       Script={ Remove-AppxPattern 'Clipchamp.Clipchamp','Disney*','Spotify*','*Duolingo*','*EclipseManager*','*AdobeExpress*','*PrimeVideo*','*CandyCrush*','*Facebook*','*Twitter*','*TikTok*' } }

    @{ Id='appx.copilot';   Category='Apps you probably never open'; Risk='Moderate'; Default=$false
       Name='Copilot app'
       Desc="Microsoft's AI chat assistant app. Turn this off if you don't use it - you can always reinstall it from the Store later."
       Script={ Remove-AppxPattern 'Microsoft.Copilot','Microsoft.Windows.Copilot' } }

    @{ Id='appx.cortana';   Category='Apps you probably never open'; Risk='Moderate'; Default=$false
       Name='Cortana'
       Desc="Microsoft's old voice assistant, mostly retired since 2023. Fine to remove if it's still on your PC."
       Script={ Remove-AppxPattern 'Microsoft.549981C3F5F10' } }

    @{ Id='appx.xbox';      Category='Apps you probably never open'; Risk='Aggressive'; Default=$false
       Name='Xbox app & Game Bar'
       Desc="WARNING: if you play any PC Game Pass titles or use Xbox cloud saves, don't remove this - it will break them. Only pick this if you never play games through Xbox/Game Pass on this PC."
       Script={ Remove-AppxPattern 'Microsoft.Xbox*','Microsoft.GamingApp','Microsoft.GamingServices' } }

    @{ Id='appx.onedrive';  Category='Apps you probably never open'; Risk='Aggressive'; Default=$false
       Name='OneDrive'
       Desc="WARNING: fully uninstalls OneDrive. If you have files syncing to OneDrive, back them up or move them out of the OneDrive folder first, or you could lose access to files that only exist in the cloud copy."
       Script={
           $exe = "$env:SystemRoot\System32\OneDriveSetup.exe"
           if (Test-Path $exe) { Start-Process $exe -ArgumentList '/uninstall' -Wait -NoNewWindow }
           Remove-Item "$env:USERPROFILE\OneDrive" -Recurse -Force -ErrorAction SilentlyContinue
           Write-Log '   OneDrive uninstalled'
       } }

    # ---------------- Look & feel ----------------
    @{ Id='shell.widgets';  Category='Look & feel'; Risk='Safe'; Default=$true
       Name='Widgets board'
       Desc="Turns off the taskbar Widgets button and the news feed that pops out of it."
       Script={ Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests' 0 } }

    @{ Id='shell.chat';     Category='Look & feel'; Risk='Safe'; Default=$true
       Name='Chat (Teams) taskbar icon'
       Desc="Removes the little chat bubble icon from the taskbar that opens consumer Teams."
       Script={ Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Chat' 'ChatIcon' 3 } }

    @{ Id='shell.websearch';Category='Look & feel'; Risk='Safe'; Default=$true
       Name='Web results in Start menu search'
       Desc="Makes Start menu search only look through your own files and apps instead of also searching Bing."
       Script={
           Set-Reg 'HKCU:\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'DisableSearchBoxSuggestions' 1
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search' 'BingSearchEnabled' 0
       } }

    @{ Id='shell.recommend';Category='Look & feel'; Risk='Safe'; Default=$true
       Name='"Recommended" section in Start menu'
       Desc="Removes the recently-used-files tiles under the Start menu's app list, if it feels cluttered to you."
       Script={ Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'Start_IrisRecommendations' 0 } }

    @{ Id='shell.gamebar';  Category='Look & feel'; Risk='Safe'; Default=$true
       Name='Game Bar background recording'
       Desc="Stops Windows from silently recording gameplay clips in the background. Doesn't affect actually playing games."
       Script={
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled' 0
           Set-Reg 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled' 0
       } }

    @{ Id='shell.contextmenu'; Category='Look & feel'; Risk='Safe'; Default=$false
       Name='Classic right-click menu (Windows 10 style)'
       Desc="Windows 11's right-click menu hides options like 'Send to' or a straight 'Delete' behind a 'Show more options' click. This restores the old full menu instantly, no 'Show more options' needed."
       Script={ Set-Reg 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32' '(Default)' '' 'String' } }

    @{ Id='shell.darkmode'; Category='Look & feel'; Risk='Safe'; Default=$false
       Name='Dark mode everywhere'
       Desc="Switches both Windows and apps to dark mode in one go, instead of setting them separately in Settings."
       Script={
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'AppsUseLightTheme' 0
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'SystemUsesLightTheme' 0
       } }

    @{ Id='shell.endtask';  Category='Look & feel'; Risk='Safe'; Default=$true
       Name='"End task" on the taskbar right-click'
       Desc="Adds an 'End task' option when you right-click a frozen app's taskbar button, so you don't have to open Task Manager just to force-close something."
       Script={ Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings' 'TaskbarEndTask' 1 } }

    @{ Id='shell.extensions'; Category='Look & feel'; Risk='Safe'; Default=$false
       Name='Always show file extensions'
       Desc="Shows the .docx, .exe, .jpg part of every filename in Explorer. Makes it much easier to tell a real file from a disguised one."
       Script={ Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'HideFileExt' 0 } }

    @{ Id='shell.hiddenfiles'; Category='Look & feel'; Risk='Safe'; Default=$false
       Name='Show hidden files and folders'
       Desc="Makes Explorer show files Windows normally hides (like config folders). Useful if you're troubleshooting something; otherwise leave it off."
       Script={ Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'Hidden' 1 } }

    @{ Id='shell.stickykeys'; Category='Look & feel'; Risk='Safe'; Default=$true
       Name='Sticky Keys popup'
       Desc="Stops the 'Do you want to turn on Sticky Keys?' dialog that appears if you accidentally press Shift five times in a row - very common if you play games with Shift as a hotkey."
       Script={ Set-Reg 'HKCU:\Control Panel\Accessibility\StickyKeys' 'Flags' '506' 'String' } }

    @{ Id='shell.meetnow';  Category='Look & feel'; Risk='Safe'; Default=$true
       Name='"Meet Now" tray icon'
       Desc="Removes the Meet Now video-call shortcut icon from the system tray, next to the clock."
       Script={ Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'HideSCAMeetNow' 1 } }

    # ---------------- Ads & suggestions ----------------
    @{ Id='ads.cdm';        Category='Ads & suggestions'; Risk='Safe'; Default=$true
       Name='"Suggested" apps and promos everywhere'
       Desc="The master switch for the stuff that makes Windows feel like it's advertising to you: apps that silently reinstall themselves, Start menu promos, and 'spotlight' popups."
       Script={
           $p = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
           foreach ($n in 'ContentDeliveryAllowed','OemPreInstalledAppsEnabled',
                          'PreInstalledAppsEnabled','SilentInstalledAppsEnabled',
                          'SoftLandingEnabled','SystemPaneSuggestionsEnabled',
                          'SubscribedContent-338388Enabled','SubscribedContent-338389Enabled',
                          'SubscribedContent-310093Enabled','SubscribedContent-353694Enabled',
                          'SubscribedContent-353696Enabled','SubscribedContent-88000326Enabled') {
               Set-Reg $p $n 0
           }
       } }

    @{ Id='ads.consumer';   Category='Ads & suggestions'; Risk='Safe'; Default=$true
       Name='Auto-reinstalling "suggested" apps'
       Desc="Stops Windows from re-downloading apps like Candy Crush or Spotify after you've already removed them."
       Script={
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures' 1
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableConsumerAccountStateContent' 1
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableSoftLanding' 1
       } }

    @{ Id='ads.settings';   Category='Ads & suggestions'; Risk='Safe'; Default=$true
       Name='Promos inside Settings and Explorer'
       Desc="Removes the little ads and upsells that show up inside the Settings app and File Explorer's sidebar."
       Script={
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338393Enabled' 0
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-353698Enabled' 0
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'ShowSyncProviderNotifications' 0
       } }

    @{ Id='ads.lockscreen'; Category='Ads & suggestions'; Risk='Safe'; Default=$true
       Name='Lock screen tips and ads'
       Desc="Turns off the little tips, fun facts, and app promos that Windows sometimes overlays on your lock screen picture."
       Script={
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsSpotlightFeatures' 1
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'RotatingLockScreenOverlayEnabled' 0
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338387Enabled' 0
       } }

    # ---------------- Privacy ----------------
    @{ Id='telem.allow';    Category='Privacy'; Risk='Safe'; Default=$true
       Name='Usage data sent to Microsoft'
       Desc="Turns Windows' data collection down to the minimum required level, and stops the 'how are we doing?' feedback popups."
       Script={
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry' 0
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'DoNotShowFeedbackNotifications' 1
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Siuf\Rules' 'NumberOfSIUFInPeriod' 0
           Set-Reg 'HKCU:\SOFTWARE\Microsoft\Siuf\Rules' 'PeriodInNanoSeconds' 0
       } }

    @{ Id='telem.adid';     Category='Privacy'; Risk='Safe'; Default=$true
       Name='Advertising ID'
       Desc="Windows quietly assigns your account an ad ID that lets apps show you 'personalized' ads. This turns it off."
       Script={ Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo' 'Enabled' 0 } }

    @{ Id='telem.activity'; Category='Privacy'; Risk='Safe'; Default=$true
       Name='Activity history / Timeline'
       Desc="Stops Windows keeping (and uploading) a history of what apps and documents you've opened."
       Script={
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'EnableActivityFeed' 0
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'PublishUserActivities' 0
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'UploadUserActivities' 0
       } }

    @{ Id='telem.speech';   Category='Privacy'; Risk='Safe'; Default=$true
       Name='Online speech & typing personalization'
       Desc="Stops Windows sending your typing patterns and voice samples to Microsoft's cloud to 'improve' autocomplete and dictation."
       Script={
           Set-Reg 'HKCU:\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy' 'HasAccepted' 0
           Set-Reg 'HKCU:\Software\Microsoft\InputPersonalization' 'RestrictImplicitTextCollection' 1
           Set-Reg 'HKCU:\Software\Microsoft\InputPersonalization' 'RestrictImplicitInkCollection' 1
           Set-Reg 'HKCU:\Software\Microsoft\Input\TIPC' 'Enabled' 0
       } }

    @{ Id='telem.track';    Category='Privacy'; Risk='Safe'; Default=$true
       Name='"Recently opened programs" tracking'
       Desc="Stops Start menu from keeping a record of which programs you've launched recently."
       Script={ Set-Reg 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'Start_TrackProgs' 0 } }

    @{ Id='telem.location'; Category='Privacy'; Risk='Moderate'; Default=$false
       Name='Location services'
       Desc="Turns off Windows' location tracking system-wide. Heads up: this can also break 'Find My Device' and automatic timezone switching when you travel."
       Script={
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors' 'DisableLocation' 1
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors' 'DisableLocationScripting' 1
       } }

    @{ Id='telem.remote';   Category='Privacy'; Risk='Moderate'; Default=$false
       Name='Remote Assistance invitations'
       Desc="Blocks other people from remotely connecting to your PC via a Remote Assistance invite (a feature most people never use anyway)."
       Script={ Set-Reg 'HKLM:\SYSTEM\CurrentControlSet\Control\Remote Assistance' 'fAllowToGetHelp' 0 } }

    # ---------------- AI features ----------------
    @{ Id='ai.copilot';     Category='AI features'; Risk='Moderate'; Default=$true
       Name='Windows Copilot button'
       Desc="Removes the Copilot icon from the taskbar and turns off the built-in AI assistant integration."
       Script={
           Set-Reg 'HKCU:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot' 'TurnOffWindowsCopilot' 1
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot' 'TurnOffWindowsCopilot' 1
       } }

    @{ Id='ai.recall';      Category='AI features'; Risk='Moderate'; Default=$true
       Name='Recall (screenshot history)'
       Desc="Recall takes periodic screenshots of everything you do so you can 'search your past.' A lot of people find that unsettling for privacy reasons - this turns it off entirely."
       Script={
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'DisableAIDataAnalysis' 1
           Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'AllowRecallEnablement' 0
       } }

    @{ Id='ai.clicktodo';   Category='AI features'; Risk='Safe'; Default=$true
       Name='Click to Do'
       Desc="Turns off the AI popup that suggests actions when you select text or images on screen."
       Script={ Set-Reg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'DisableClickToDo' 1 } }

    # ---------------- Startup & performance ----------------
    @{ Id='task.telemetry'; Category='Startup & performance'; Risk='Safe'; Default=$true
       Name='Background telemetry tasks'
       Desc="Disables a batch of scheduled tasks that run quietly in the background collecting diagnostic data and error reports."
       Script={ Disable-Task @(
           '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser',
           '\Microsoft\Windows\Application Experience\ProgramDataUpdater',
           '\Microsoft\Windows\Application Experience\StartupAppTask',
           '\Microsoft\Windows\Application Experience\AitAgent',
           '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator',
           '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip',
           '\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask',
           '\Microsoft\Windows\Feedback\Siuf\DmClient',
           '\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload',
           '\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector',
           '\Microsoft\Windows\Windows Error Reporting\QueueReporting',
           '\Microsoft\Windows\Autochk\Proxy',
           '\Microsoft\Windows\RetailDemo\CleanupOfflineContent',
           '\Microsoft\Windows\CloudExperienceHost\CreateObjectTask'
       ) } }

    @{ Id='perf.faststartup'; Category='Startup & performance'; Risk='Moderate'; Default=$false
       Name='Fast Startup'
       Desc="Fast Startup speeds up boot by hibernating the kernel instead of a full shutdown - but it's also a common cause of dual-boot Linux issues and 'my files look out of date' bugs after a Windows Update. Turn this off if you dual-boot or use external drives a lot."
       Script={ Set-Reg 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' 'HiberbootEnabled' 0 } }

    @{ Id='cleanup.temp';   Category='Startup & performance'; Risk='Safe'; Default=$false
       Name='Clear temporary files'
       Desc="Deletes the contents of your Windows Temp folder right now - safe, temporary files are regenerated automatically as needed. Can free up a surprising amount of space."
       Script={
           Get-ChildItem "$env:TEMP" -Force -ErrorAction SilentlyContinue | ForEach-Object {
               try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop } catch {}
           }
           Write-Log "   temp folder cleared"
       } }

    @{ Id='cleanup.updatecache'; Category='Startup & performance'; Risk='Moderate'; Default=$false
       Name='Clear Windows Update download cache'
       Desc="Clears out old downloaded update files that Windows Update keeps around 'just in case.' Briefly stops the Update service to do it safely - your PC will still check for updates normally afterward."
       Script={
           Stop-Service wuauserv -Force -ErrorAction SilentlyContinue
           Remove-Item "$env:WINDIR\SoftwareDistribution\Download\*" -Recurse -Force -ErrorAction SilentlyContinue
           Start-Service wuauserv -ErrorAction SilentlyContinue
           Write-Log "   update cache cleared"
       } }

    # ---------------- Services ----------------
    @{ Id='svc.diagtrack';  Category='Services (advanced)'; Risk='Aggressive'; Default=$false
       Name='DiagTrack telemetry service'
       Desc="Fully disables the background service responsible for sending diagnostic data to Microsoft. More thorough than the 'Usage data' tweak above, but some enterprise/managed PCs expect this service to exist - skip this one on a work laptop."
       Script={ Disable-Svc 'DiagTrack','dmwappushservice' } }
)

# ----------------------------------------------------------------------
# Entry points
# ----------------------------------------------------------------------

if ($List) {
    $out = $Catalog | ForEach-Object {
        [pscustomobject]@{
            Id       = $_.Id
            Name     = $_.Name
            Desc     = $_.Desc
            Category = $_.Category
            Risk     = $_.Risk
            Default  = [bool]$_.Default
        }
    }
    $out | ConvertTo-Json -Depth 4
    exit 0
}

if ($RestorePoint) {
    Write-Log "TinyShell — creating System Restore checkpoint"
    $ok = New-TinyShellRestorePoint
    exit ($ok ? 0 : 1)
}

if ($Apply) {
    Write-Log "TinyShell debloat — $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Log "Selected: $($Ids.Count) item(s)"
    Write-Log ""

    $applied = 0; $failed = 0

    foreach ($id in $Ids) {
        $item = $Catalog | Where-Object { $_.Id -eq $id } | Select-Object -First 1
        if (-not $item) { Write-Log "?? unknown id: $id"; $failed++; continue }

        Write-Log "==> [$($item.Risk)] $($item.Name)"
        try {
            & $item.Script
            $applied++
        } catch {
            Write-Log "   ERROR: $($_.Exception.Message.Split([char]10)[0])"
            $failed++
        }
        Write-Log ""
    }

    Write-Log "----------------------------------------"
    Write-Log "Done. applied=$applied failed=$failed"
    exit 0
}

Write-Host "Usage: debloat.ps1 -List | -RestorePoint | -Apply -Ids <ids> [-ProgressFile <path>]"
exit 1
