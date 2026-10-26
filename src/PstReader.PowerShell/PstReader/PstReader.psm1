Set-StrictMode -Version Latest

if ($PSEdition -eq 'Core' -and $PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PstReader requires Windows PowerShell 5.1 or PowerShell 7.0 and later.'
}

$framework = if ($PSEdition -eq 'Desktop') { 'net472' } else { 'netstandard2.0' }
$libraryPath = Join-Path $PSScriptRoot "lib/$framework"
if ($PSEdition -eq 'Desktop') {
    Add-Type -Path (Join-Path $libraryPath 'XstReader.Base.dll')
} else {
    Add-Type -Path (Join-Path $PSScriptRoot 'lib/PstReader.PowerShell.Loader.dll')
    [PstReader.PowerShell.ModuleLoader]::Load($libraryPath)
}
Add-Type -Path (Join-Path $libraryPath 'PstReader.Core.dll')

function ConvertTo-PstDate {
    param([object] $Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTime]) { return [PstReader.ReaderDateTime]::Parse($Value.ToString('O')) }
    if ($Value -isnot [string]) { throw [ArgumentException]::new('Dates must be strings, DateTime, or DateTimeOffset values.') }
    return [PstReader.ReaderDateTime]::Parse($Value)
}

function New-PstStore {
    param([string] $Path)
    return [PstReader.PstStore]::new($Path)
}

function New-PstReadError {
    param([Exception] $Exception, [object] $Target)
    while ($null -ne $Exception.InnerException) { $Exception = $Exception.InnerException }
    $category = [System.Management.Automation.ErrorCategory]::ReadError
    if ($Exception -is [IO.FileNotFoundException] -or $Exception -is [IO.DirectoryNotFoundException] -or
        $Exception -is [System.Management.Automation.ItemNotFoundException]) {
        $category = [System.Management.Automation.ErrorCategory]::ObjectNotFound
    } elseif ($Exception -is [UnauthorizedAccessException]) {
        $category = [System.Management.Automation.ErrorCategory]::PermissionDenied
    } elseif ($Exception -is [ArgumentException] -or $Exception -is [FormatException]) {
        $category = [System.Management.Automation.ErrorCategory]::InvalidArgument
    } elseif ($Exception -is [IO.InvalidDataException] -or $Exception -is [PstReader.Format.PffFormatException]) {
        $category = [System.Management.Automation.ErrorCategory]::InvalidData
    }
    return [System.Management.Automation.ErrorRecord]::new($Exception, 'PstReadFailed', $category, $Target)
}

function Invoke-PstRead {
    param([string] $Command, [System.Collections.IDictionary] $Parameters,
        [System.Management.Automation.PSCmdlet] $Caller)

    $from = $null
    $to = $null
    $zone = [TimeZoneInfo]::Local
    try {
        if ($Parameters.ContainsKey('From')) { $from = ConvertTo-PstDate $Parameters['From'] }
        if ($Parameters.ContainsKey('To')) { $to = ConvertTo-PstDate $Parameters['To'] }
        [PstReader.ReaderDateTime]::ValidateRange($from, $to)
        if ($Parameters.ContainsKey('TimeZone')) {
            $zone = [PstReader.ReaderDateTime]::ResolveTimeZone($Parameters['TimeZone'])
        }
    } catch {
        $Caller.ThrowTerminatingError((New-PstReadError $_.Exception $Parameters))
    }

    $literal = $Parameters.ContainsKey('LiteralPath')
    $paths = if ($literal) { $Parameters['LiteralPath'] } else { $Parameters['Path'] }
    foreach ($inputPath in $paths) {
        $failure = $null
        try {
            $resolve = @{ ErrorAction = 'Stop' }
            if ($literal) { $resolve.LiteralPath = $inputPath } else { $resolve.Path = $inputPath }
            $resolvedPaths = @(Resolve-Path @resolve)
            if ($resolvedPaths.Count -eq 0) {
                throw [IO.FileNotFoundException]::new("No files match: $inputPath", $inputPath)
            }
        } catch { $failure = New-PstReadError $_.Exception $inputPath }
        if ($null -ne $failure) {
            $Caller.WriteError($failure)
            continue
        }

        foreach ($resolved in $resolvedPaths) {
            $failure = $null
            $warnings = @()
            $items = @()
            try {
                if ($resolved.Provider.Name -ne 'FileSystem' -or
                    [IO.Directory]::Exists($resolved.ProviderPath)) {
                    throw [ArgumentException]::new('Specify a file in the FileSystem provider.')
                }
                $store = New-PstStore $resolved.ProviderPath
                $includeBody = [bool] $Parameters['IncludeBody']
                switch ($Command) {
                    'Store' { $items = @($store.ReadStore()) }
                    'Folder' { $items = @($store.ReadFolders()) }
                    'Calendar' { $result = $store.ReadCalendar($from, $to, $includeBody) }
                    'Task' {
                        $field = if ($Parameters.ContainsKey('DateField')) { $Parameters['DateField'] } else { 'due' }
                        $result = $store.ReadTasks($from, $to, $field.ToLowerInvariant(), $includeBody)
                    }
                    'Mail' {
                        $field = if ($Parameters.ContainsKey('DateField')) { $Parameters['DateField'] } else { 'received' }
                        $result = $store.ReadMail($from, $to, $field.ToLowerInvariant(), $includeBody,
                            [bool] $Parameters['FlaggedOnly'])
                    }
                }
                if ($Command -in 'Calendar', 'Task', 'Mail') {
                    $warnings = $result.Warnings
                    $items = @($result.Items | ForEach-Object { [PstReader.ReaderDateTime]::InTimeZone($_, $zone) })
                }
            } catch { $failure = New-PstReadError $_.Exception $resolved.ProviderPath }
            if ($null -ne $failure) {
                $Caller.WriteError($failure)
                continue
            }
            foreach ($warning in $warnings) { $Caller.WriteWarning("$($resolved.ProviderPath): $warning") }
            foreach ($item in $items) { $Caller.WriteObject($item) }
        }
    }
}

function Get-PstStore {
    <#
    .SYNOPSIS
    Reads PST/OST store information.
    .DESCRIPTION
    Reads PST/OST files using PstReader.Core.dll without Outlook. Returns typed objects,
    one per pipeline record. File failures are non-terminating errors unless ErrorAction
    is Stop. Skipped corrupt items are reported on the warning stream.
    .PARAMETER Path
    One or more filesystem paths. Wildcards and pipeline input are supported.
    .PARAMETER LiteralPath
    Exact paths without wildcard expansion. Accepts FullName, PSPath, and InputFile properties.
    .EXAMPLE
    Get-ChildItem C:\Data\*.pst | Get-PstStore
    .OUTPUTS
    PstReader.Models.StoreInfo
    #>
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    [OutputType([PstReader.Models.StoreInfo])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipeline, ValueFromPipelineByPropertyName, ParameterSetName = 'Path')]
        [ValidateNotNullOrEmpty()]
        [string[]] $Path,

        [Parameter(Mandatory, ValueFromPipelineByPropertyName, ParameterSetName = 'LiteralPath')]
        [Alias('FullName', 'PSPath', 'InputFile')]
        [ValidateNotNullOrEmpty()]
        [string[]] $LiteralPath
    )
    process { Invoke-PstRead -Command Store -Parameters $PSBoundParameters -Caller $PSCmdlet }
}

function Get-PstFolder {
    <#
    .SYNOPSIS
    Lists all folders in a PST/OST file.
    .DESCRIPTION
    Reads PST/OST files using PstReader.Core.dll without Outlook. Returns typed objects,
    one per pipeline record. File failures are non-terminating errors unless ErrorAction
    is Stop. Skipped corrupt items are reported on the warning stream.
    .PARAMETER Path
    One or more filesystem paths. Wildcards and pipeline input are supported.
    .PARAMETER LiteralPath
    Exact paths without wildcard expansion. Accepts FullName, PSPath, and InputFile properties.
    .EXAMPLE
    Get-ChildItem C:\Data\*.pst | Get-PstFolder
    .OUTPUTS
    PstReader.Models.FolderInfo
    #>
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    [OutputType([PstReader.Models.FolderInfo])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipeline, ValueFromPipelineByPropertyName, ParameterSetName = 'Path')]
        [ValidateNotNullOrEmpty()]
        [string[]] $Path,

        [Parameter(Mandatory, ValueFromPipelineByPropertyName, ParameterSetName = 'LiteralPath')]
        [Alias('FullName', 'PSPath', 'InputFile')]
        [ValidateNotNullOrEmpty()]
        [string[]] $LiteralPath
    )
    process { Invoke-PstRead -Command Folder -Parameters $PSBoundParameters -Caller $PSCmdlet }
}

function Get-PstCalendarItem {
    <#
    .SYNOPSIS
    Reads calendar items overlapping a date range.
    .DESCRIPTION
    Reads PST/OST files using PstReader.Core.dll without Outlook. Returns typed objects,
    one per pipeline record. File failures are non-terminating errors unless ErrorAction
    is Stop. Skipped corrupt items are reported on the warning stream.
    .PARAMETER Path
    One or more filesystem paths. Wildcards and pipeline input are supported.
    .PARAMETER LiteralPath
    Exact paths without wildcard expansion. Accepts FullName, PSPath, and InputFile properties.
    .PARAMETER From
    Lower date bound. Accepts ISO 8601 strings, DateTime, or DateTimeOffset. Dates without
    offsets use local time; ambiguous or invalid local times require an explicit offset.
    .PARAMETER To
    Exclusive upper date bound. Must be later than From. Calendar items match by overlap;
    task and mail dates must be at or after From and before To.
    .PARAMETER TimeZone
    Output time zone: local (default), utc, or a system time zone ID. Also accepts OutputTimeZone.
    .PARAMETER IncludeBody
    Reads plain text, HTML, or RTF bodies and their formats. Otherwise Body and BodyFormat are null.
    .EXAMPLE
    Get-ChildItem C:\Data\*.pst | Get-PstCalendarItem
    .OUTPUTS
    PstReader.Models.CalendarItem
    #>
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    [OutputType([PstReader.Models.CalendarItem])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipeline, ValueFromPipelineByPropertyName, ParameterSetName = 'Path')]
        [ValidateNotNullOrEmpty()]
        [string[]] $Path,

        [Parameter(Mandatory, ValueFromPipelineByPropertyName, ParameterSetName = 'LiteralPath')]
        [Alias('FullName', 'PSPath', 'InputFile')]
        [ValidateNotNullOrEmpty()]
        [string[]] $LiteralPath,

        [ValidateNotNullOrEmpty()]
        [object] $From,
        [ValidateNotNullOrEmpty()]
        [object] $To,
        [Alias('OutputTimeZone')]
        [ValidateNotNullOrEmpty()]
        [string] $TimeZone = 'local',
        [switch] $IncludeBody
    )
    process { Invoke-PstRead -Command Calendar -Parameters $PSBoundParameters -Caller $PSCmdlet }
}

function Get-PstTask {
    <#
    .SYNOPSIS
    Reads tasks filtered by their selected date.
    .DESCRIPTION
    Reads PST/OST files using PstReader.Core.dll without Outlook. Returns typed objects,
    one per pipeline record. File failures are non-terminating errors unless ErrorAction
    is Stop. Skipped corrupt items are reported on the warning stream.
    .PARAMETER Path
    One or more filesystem paths. Wildcards and pipeline input are supported.
    .PARAMETER LiteralPath
    Exact paths without wildcard expansion. Accepts FullName, PSPath, and InputFile properties.
    .PARAMETER From
    Lower date bound. Accepts ISO 8601 strings, DateTime, or DateTimeOffset. Dates without
    offsets use local time; ambiguous or invalid local times require an explicit offset.
    .PARAMETER To
    Exclusive upper date bound. Must be later than From. Calendar items match by overlap;
    task and mail dates must be at or after From and before To.
    .PARAMETER TimeZone
    Output time zone: local (default), utc, or a system time zone ID. Also accepts OutputTimeZone.
    .PARAMETER IncludeBody
    Reads plain text, HTML, or RTF bodies and their formats. Otherwise Body and BodyFormat are null.
    .PARAMETER DateField
    Date used for filtering and sorting. Defaults to due. Missing dates have no fallback.
    .EXAMPLE
    Get-ChildItem C:\Data\*.pst | Get-PstTask
    .OUTPUTS
    PstReader.Models.TaskItem
    #>
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    [OutputType([PstReader.Models.TaskItem])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipeline, ValueFromPipelineByPropertyName, ParameterSetName = 'Path')]
        [ValidateNotNullOrEmpty()]
        [string[]] $Path,

        [Parameter(Mandatory, ValueFromPipelineByPropertyName, ParameterSetName = 'LiteralPath')]
        [Alias('FullName', 'PSPath', 'InputFile')]
        [ValidateNotNullOrEmpty()]
        [string[]] $LiteralPath,

        [ValidateNotNullOrEmpty()]
        [object] $From,
        [ValidateNotNullOrEmpty()]
        [object] $To,
        [Alias('OutputTimeZone')]
        [ValidateNotNullOrEmpty()]
        [string] $TimeZone = 'local',
        [switch] $IncludeBody,
        [ValidateSet('start', 'due', 'completed')]
        [string] $DateField = 'due'
    )
    process { Invoke-PstRead -Command Task -Parameters $PSBoundParameters -Caller $PSCmdlet }
}

function Get-PstMail {
    <#
    .SYNOPSIS
    Reads mail, optionally restricted to active flags.
    .DESCRIPTION
    Reads PST/OST files using PstReader.Core.dll without Outlook. Returns typed objects,
    one per pipeline record. File failures are non-terminating errors unless ErrorAction
    is Stop. Skipped corrupt items are reported on the warning stream.
    .PARAMETER Path
    One or more filesystem paths. Wildcards and pipeline input are supported.
    .PARAMETER LiteralPath
    Exact paths without wildcard expansion. Accepts FullName, PSPath, and InputFile properties.
    .PARAMETER From
    Lower date bound. Accepts ISO 8601 strings, DateTime, or DateTimeOffset. Dates without
    offsets use local time; ambiguous or invalid local times require an explicit offset.
    .PARAMETER To
    Exclusive upper date bound. Must be later than From. Calendar items match by overlap;
    task and mail dates must be at or after From and before To.
    .PARAMETER TimeZone
    Output time zone: local (default), utc, or a system time zone ID. Also accepts OutputTimeZone.
    .PARAMETER IncludeBody
    Reads plain text, HTML, or RTF bodies and their formats. Otherwise Body and BodyFormat are null.
    .PARAMETER DateField
    Date used for filtering and sorting. Defaults to received. Missing dates have no fallback.
    .PARAMETER FlaggedOnly
    Returns only mail with an active, uncompleted follow-up flag.
    .EXAMPLE
    Get-ChildItem C:\Data\*.pst | Get-PstMail
    .OUTPUTS
    PstReader.Models.MailItem
    #>
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    [OutputType([PstReader.Models.MailItem])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipeline, ValueFromPipelineByPropertyName, ParameterSetName = 'Path')]
        [ValidateNotNullOrEmpty()]
        [string[]] $Path,

        [Parameter(Mandatory, ValueFromPipelineByPropertyName, ParameterSetName = 'LiteralPath')]
        [Alias('FullName', 'PSPath', 'InputFile')]
        [ValidateNotNullOrEmpty()]
        [string[]] $LiteralPath,

        [ValidateNotNullOrEmpty()]
        [object] $From,
        [ValidateNotNullOrEmpty()]
        [object] $To,
        [Alias('OutputTimeZone')]
        [ValidateNotNullOrEmpty()]
        [string] $TimeZone = 'local',
        [switch] $IncludeBody,
        [ValidateSet('received', 'sent')]
        [string] $DateField = 'received',
        [switch] $FlaggedOnly
    )
    process { Invoke-PstRead -Command Mail -Parameters $PSBoundParameters -Caller $PSCmdlet }
}

Export-ModuleMember -Function Get-PstStore, Get-PstFolder, Get-PstCalendarItem, Get-PstTask, Get-PstMail
