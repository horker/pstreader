#Requires -Version 5.1
[CmdletBinding()]
param([string] $ModulePath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$ModulePath) { $ModulePath = Join-Path $PSScriptRoot '../../artifacts/PowerShell/PstReader/PstReader.psd1' }
$script:assertions = 0
function Assert([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw "Assertion failed: $Message" }
    $script:assertions++
}
function Assert-Throws([scriptblock] $Action, [string] $Message) {
    $caught = $false
    try { & $Action | Out-Null } catch { $caught = $true }
    Assert $caught $Message
}

$module = Import-Module $ModulePath -Force -PassThru
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('PstReader-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $first = Join-Path $temporary 'archive [1].pst'
    $second = Join-Path $temporary 'second.pst'
    [IO.File]::WriteAllBytes($first, (New-Object byte[] 32))
    [IO.File]::WriteAllBytes($second, (New-Object byte[] 32))
    $missing = Join-Path $temporary 'missing.pst'

    $commands = @(Get-Command -Module PstReader)
    Assert ($commands.Count -eq 5) 'Only the five public commands are exported.'
    foreach ($command in $commands) {
        Assert ($command.Parameters.ContainsKey('ErrorAction')) "$($command.Name) is an advanced function."
        Assert ($command.Parameters.ContainsKey('LiteralPath')) "$($command.Name) supports literal paths."
        Assert ((Get-Help $command.Name).Synopsis -notmatch '^Get-Pst') "$($command.Name) has help."
        Assert-Throws { & $command -LiteralPath $first -ErrorAction Stop } 'Real invalid PST headers fail.'
    }
    $records = @(Get-PstStore -LiteralPath $first -ErrorAction Continue 2>&1)
    Assert ($records.Count -eq 1 -and $records[0] -is [System.Management.Automation.ErrorRecord]) 'Failures use the error stream only.'
    Assert ($records[0].CategoryInfo.Category -eq 'InvalidData') 'Bad signatures are InvalidData.'
    $records = @(Get-PstStore -LiteralPath $missing -ErrorAction Continue 2>&1)
    Assert ($records[0].CategoryInfo.Category -eq 'ObjectNotFound') 'Missing files are ObjectNotFound.'
    Assert-Throws { Get-PstStore $temporary } 'Directories are rejected.'
    Assert-Throws { Get-PstStore Env:PATH } 'Non-filesystem providers are rejected.'
    Assert-Throws { Get-PstTask $first -DateField received } 'Task date field is validated by the binder.'
    Assert-Throws { Get-PstMail $first -DateField due } 'Mail date field is validated by the binder.'
    Assert-Throws { Get-PstMail $first -From '2026-02-01' -To '2026-01-01' } 'Reversed ranges are rejected.'
    Assert-Throws { Get-PstMail $first -From 'invalid' } 'Malformed dates are rejected.'
    Assert-Throws { Get-PstMail $first -TimeZone 'invalid-zone' } 'Unknown zones are rejected.'

    # Force the parser's optional crypto dependency to load, including on PowerShell 7.0
    # which ships a different Pkcs version. Malformed bytes must fail as data, not DLL loading.
    $cryptoError = $null
    $parser = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'XstReader.Base' } | Select-Object -First 1
    try { [Activator]::CreateInstance($parser.GetType('XstReader.Message')).ReadSignedOrEncryptedMessage([byte[]]@(1, 2, 3)) }
    catch {
        $cryptoError = $_.Exception
        while ($null -ne $cryptoError.InnerException -and $cryptoError -isnot [System.Security.Cryptography.CryptographicException]) {
            $cryptoError = $cryptoError.InnerException
        }
    }
    Assert ($cryptoError -is [System.Security.Cryptography.CryptographicException]) 'Parser dependencies load without host assembly conflicts.'

    $header = New-Object byte[] 32
    $header[0] = 0x21; $header[1] = 0x42; $header[2] = 0x44; $header[3] = 0x4E
    $header[8] = 0x53; $header[9] = 0x4D; $header[10] = 0x17
    $header[14] = 1; $header[15] = 1
    [IO.File]::WriteAllBytes($second, $header)
    $records = @(Get-PstFolder -LiteralPath $second -ErrorAction Continue 2>&1)
    Assert ($records.Count -eq 1 -and $records[0].Exception -is [IO.EndOfStreamException]) 'Truncated stores exercise the parser and its read-exactly compatibility code.'

    $modelAssembly = [PstReader.Models.MailItem].Assembly
    Remove-Module PstReader
    $module = Import-Module $ModulePath -PassThru
    Assert ([PstReader.Models.MailItem].Assembly -eq $modelAssembly) 'Reimport retains model type identity.'

    # Replace only the file reader inside this module session. The public command binding,
    # filesystem resolution, date conversion, object output, and PowerShell streams remain real.
    & $module {
        $script:TestState = [pscustomobject]@{
            Calls = (New-Object 'System.Collections.Generic.List[object]')
            Empty = $false
            Warn = $false
        }
        function script:New-PstStore {
            param([string] $Path)
            $store = [pscustomobject]@{ Path = $Path; State = $script:TestState }
            $store | Add-Member ScriptMethod ReadStore {
                $this.State.Calls.Add(@('Store', $this.Path))
                [PstReader.Models.StoreInfo]::new($this.Path, 'PST', 23, 19, 'Unicode64', 512, 32, 'Root', 2, 1)
            }
            $store | Add-Member ScriptMethod ReadFolders {
                [PstReader.Models.FolderInfo]::new('/Root', 'Root', 0, 0, 1)
                [PstReader.Models.FolderInfo]::new('/Root/Inbox', 'Inbox', 1, 1, 0)
            }
            $store | Add-Member ScriptMethod ReadCalendar {
                param($From, $To, $IncludeBody)
                $this.State.Calls.Add(@('Calendar', $this.Path, $From, $To, $IncludeBody))
                $date = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
                $body = if ($IncludeBody) { 'Calendar body' } else { $null }
                $item = [PstReader.Models.CalendarItem]::new('/Calendar', 'Meeting', $date, $date.AddHours(1), 'Room', $false, 'IPM.Appointment', $body, 'text')
                [pscustomobject]@{ Items = @($item); Warnings = @() }
            }
            $store | Add-Member ScriptMethod ReadTasks {
                param($From, $To, $DateField, $IncludeBody)
                $this.State.Calls.Add(@('Task', $this.Path, $From, $To, $DateField, $IncludeBody))
                $date = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
                $item = [PstReader.Models.TaskItem]::new('/Tasks', 'Task', $null, $date, $null, 0, 25.0, $false, 'IPM.Task', $null, $null)
                [pscustomobject]@{ Items = @($item); Warnings = @() }
            }
            $store | Add-Member ScriptMethod ReadMail {
                param($From, $To, $DateField, $IncludeBody, $FlaggedOnly)
                $this.State.Calls.Add(@('Mail', $this.Path, $From, $To, $DateField, $IncludeBody, $FlaggedOnly))
                $date = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
                $item = [PstReader.Models.MailItem]::new('/Inbox', 'Mail', 'Sender', 'sender@example.com', '', '', '', $date, $null, $false, $true, 'flagged', 'IPM.Note', $null, $null)
                $items = if ($this.State.Empty) { @() } else { @($item) }
                $warnings = if ($this.State.Warn) { @('Skipped a corrupt item.') } else { @() }
                [pscustomobject]@{ Items = $items; Warnings = $warnings }
            }
            return $store
        }
    }
    $state = & $module { $script:TestState }
    $store = Get-PstStore -LiteralPath $first
    Assert ($store -is [PstReader.Models.StoreInfo]) 'Store output is a CLR record.'
    Assert ($store.InputFile -eq $first) 'Literal paths preserve wildcard characters.'
    $folders = @($store | Get-PstFolder)
    Assert ($folders.Count -eq 2 -and $folders[0] -is [PstReader.Models.FolderInfo]) 'Store InputFile binds by property and folders are enumerated.'
    $stores = @(Get-ChildItem -LiteralPath $temporary | Get-PstStore)
    Assert ($stores.Count -eq 2) 'FileInfo pipelines support literal filenames.'
    Assert (@(Get-PstStore -Path (Join-Path $temporary '*.pst')).Count -eq 2) 'Wildcards expand to multiple files.'
    Assert (@($second, $second | Get-PstStore).Count -eq 2) 'String pipeline paths are accepted.'
    Assert (@(Get-PstStore -LiteralPath $first, $second).Count -eq 2) 'Path arrays are accepted.'

    Push-Location $temporary
    try { Assert ((Get-PstStore -LiteralPath 'second.pst').InputFile -eq $second) 'Relative paths use PowerShell location.' }
    finally { Pop-Location }

    $calendar = Get-PstCalendarItem -LiteralPath $first -From '2025-12-01T09:00:00+09:00' -To ([DateTimeOffset]::Parse('2026-02-01T00:00:00Z')) -TimeZone utc -IncludeBody
    Assert ($calendar -is [PstReader.Models.CalendarItem]) 'Calendar output retains its CLR type.'
    Assert ($calendar.Start -is [DateTimeOffset] -and $calendar.Start.Offset -eq [TimeSpan]::Zero) 'Calendar dates are typed and UTC.'
    Assert ($calendar.Body -eq 'Calendar body') 'Body options reach the reader.'
    Assert ($state.Calls[-1][2] -eq [DateTimeOffset]::Parse('2025-12-01T00:00:00Z')) 'Input offsets normalize to UTC.'
    $task = Get-PstTask -LiteralPath $first -DateField START -IncludeBody -OutputTimeZone 'Tokyo Standard Time'
    Assert ($task -is [PstReader.Models.TaskItem] -and $task.Due.Offset.TotalHours -eq 9) 'Task dates use the selected time zone.'
    Assert ($null -eq $task.Start -and $null -eq $task.Completed) 'Missing task dates remain null.'
    Assert ($state.Calls[-1][4] -ceq 'start' -and $state.Calls[-1][5]) 'Task options are forwarded and normalized.'
    $mail = Get-PstMail -LiteralPath $first -DateField SENT -FlaggedOnly -IncludeBody
    Assert ($mail -is [PstReader.Models.MailItem] -and $mail.HasAttachments -is [bool]) 'Mail properties retain their types.'
    Assert ($state.Calls[-1][4] -ceq 'sent' -and $state.Calls[-1][5] -and $state.Calls[-1][6]) 'Mail options reach the reader.'
    Assert ($mail.Received.Offset -eq [TimeZoneInfo]::Local.GetUtcOffset($mail.Received)) 'Default output uses local time.'
    Get-PstTask -LiteralPath $first | Out-Null
    Assert ($state.Calls[-1][4] -eq 'due') 'Default task date is due.'
    Get-PstMail -LiteralPath $first | Out-Null
    Assert ($state.Calls[-1][4] -eq 'received' -and !$state.Calls[-1][6]) 'Default mail date is received, without a flag restriction.'

    $state.Empty = $true
    Assert (@(Get-PstMail -LiteralPath $first).Count -eq 0) 'Empty results emit no objects.'
    $state.Empty = $false
    $state.Warn = $true
    $warnings = @()
    $mail = @(Get-PstMail -LiteralPath $first -WarningAction SilentlyContinue -WarningVariable warnings)
    Assert ($mail.Count -eq 1 -and $warnings.Count -eq 1) 'Warnings do not contaminate success output.'
    Assert-Throws { Get-PstMail -LiteralPath $first -WarningAction Stop } 'WarningAction Stop is respected.'
    $state.Warn = $false
    $records = @(Get-PstStore -LiteralPath $missing, $second -ErrorAction Continue 2>&1)
    Assert ($records.Count -eq 2 -and $records[1] -is [PstReader.Models.StoreInfo]) 'Processing continues after a file error.'
    $before = $state.Calls.Count
    Assert-Throws { Get-PstStore -LiteralPath $missing, $second -ErrorAction Stop } 'ErrorAction Stop terminates processing.'
    Assert ($state.Calls.Count -eq $before) 'Stop prevents reading subsequent files.'
    Assert (($mail | ConvertTo-Json -Depth 4 | ConvertFrom-Json).Subject -eq 'Mail') 'Objects compose with ConvertTo-Json.'
    Assert (($mail | ConvertTo-Csv -NoTypeInformation | ConvertFrom-Csv).Subject -eq 'Mail') 'Objects compose with CSV commands.'
    Write-Output "Passed $script:assertions assertions on PowerShell $($PSVersionTable.PSVersion)."
} finally {
    Remove-Module PstReader -Force
    # Only remove the uniquely named directory created by this test under the OS temp directory.
    $resolvedTemporary = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedTemporary.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected test directory: $resolvedTemporary"
    }
    Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
}
