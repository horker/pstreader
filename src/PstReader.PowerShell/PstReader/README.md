# PstReader PowerShell module

Read PST/OST files directly, without Outlook, Office, MAPI, or a separate process.
The module consists of PowerShell advanced functions and the same reader DLL used
by the `pstreader` CLI.

## Requirements and installation

- Windows PowerShell 5.1 with .NET Framework 4.7.2 or later, or PowerShell 7.0+.
- The .NET 10 SDK is required only to build from source.
- The distribution includes `net472` and `netstandard2.0` DLLs. The module selects
  the appropriate build automatically. PowerShell 6 is not supported.
- Validated on Windows with PowerShell 5.1, 7.0.13, and 7.6.8.

From the repository root, run `./scripts/publish-module.ps1`. Copy the entire
resulting `artifacts/PowerShell/PstReader` folder to any directory listed by the
target shell's `$env:PSModulePath`, then run:

```powershell
Import-Module PstReader
Get-Command -Module PstReader
Get-Help Get-PstMail -Full
```

An explicit path works without installation:

```powershell
Import-Module ./artifacts/PowerShell/PstReader/PstReader.psd1
```

DLLs remain loaded until PowerShell exits. After rebuilding or upgrading the
module, start a new PowerShell process before importing the new version.
Follow your environment's script execution policy when importing scripts.

## Commands

| Command | Output type | Purpose |
| --- | --- | --- |
| `Get-PstStore` | `PstReader.Models.StoreInfo` | Store format, size, and counts |
| `Get-PstFolder` | `PstReader.Models.FolderInfo` | All folders and declared counts |
| `Get-PstCalendarItem` | `PstReader.Models.CalendarItem` | Calendar items overlapping a range |
| `Get-PstTask` | `PstReader.Models.TaskItem` | Tasks, optionally filtered by a date |
| `Get-PstMail` | `PstReader.Models.MailItem` | Mail, optionally filtered by date or flag |

All commands accept `-Path <string[]>` (position 0, wildcards, pipeline input) or
`-LiteralPath <string[]>` (no wildcard interpretation). `FullName`, `PSPath`, and
`InputFile` are aliases for property-based literal path binding. Relative paths
are resolved from the current PowerShell filesystem location. Files are opened
read-only; no connection or disposal command is needed.

Calendar, task, and mail commands additionally accept:

| Parameter | Behavior |
| --- | --- |
| `-From` | Lower date bound; ISO 8601 string, `DateTime`, or `DateTimeOffset` |
| `-To` | Exclusive upper bound, later than `From` |
| `-TimeZone` | Output time zone: `local` (default), `utc`, or system time-zone ID |
| `-OutputTimeZone` | Alias for `TimeZone` |
| `-IncludeBody` | Populate `Body` and `BodyFormat` (`text`, `html`, or `rtf`) |

`Get-PstTask -DateField` accepts `start`, `due` (default), or `completed`.
`Get-PstMail -DateField` accepts `received` (default) or `sent`.
`Get-PstMail -FlaggedOnly` selects active follow-up flags and excludes completed
flags. Calendar filtering uses overlap; task and mail filtering uses the selected
date in `[From, To)`. Missing dates have no fallback and are excluded when a bound
is specified. Without date bounds, records with missing dates are included.

Dates without an offset use local time. Invalid or ambiguous local times must
include an explicit offset. The output time zone does not change the meaning of
the input range. Use time-zone IDs supported by the host operating system.

## Pipelines and exports

```powershell
# Brackets are literal characters here.
Get-PstStore -LiteralPath 'C:\Data\archive [2026].pst' | Get-PstFolder

# Multiple files, active mail flags, and native PowerShell filtering/export.
Get-ChildItem C:\Data\*.pst | Get-PstMail -FlaggedOnly -IncludeBody |
    Where-Object HasAttachments |
    Select-Object Subject, SenderAddress, Received, Body |
    Export-Csv ./mail.csv -NoTypeInformation -Encoding UTF8

# Wrap the results in an array to retain JSON array shape even for zero/one item.
$items = @(Get-PstCalendarItem C:\Data\archive.pst -From '2026-01-01' `
    -To '2026-02-01' -TimeZone utc)
ConvertTo-Json -InputObject $items -Depth 4 | Set-Content ./calendar.json -Encoding UTF8
```

Output properties use PascalCase and preserve .NET types. `Body` and `BodyFormat`
are present but null unless requested. PowerShell's JSON/CSV serialization is
native to the host version; it is not the CLI's camelCase export schema. Use the
CLI when byte-level compatibility with existing CLI exports is required.

File failures are non-terminating errors, allowing subsequent files to be read.
Use `-ErrorAction Stop` with `try`/`catch` to stop on a failure. Invalid parameter
values and invalid date ranges terminate the command. Corrupt items that can be
skipped are reported with `WriteWarning`; `-WarningAction` and `-WarningVariable`
are supported. Neither diagnostics nor formatted text enter the success stream.

The parser's existing limitations still apply, including stored recurrence data
without recurrence expansion and potential conflicts with files locked by Outlook.
Each operation scans the file separately and materializes its result before
emitting records to the pipeline.

## Shared library API

`PstReader.Core` has no dependency on the CLI or PowerShell. Its public entry point
is `PstReader.PstStore`:

```csharp
var store = new PstReader.PstStore(@"C:\Data\archive.pst");
var info = store.ReadStore();
var folders = store.ReadFolders();
var calendar = store.ReadCalendar(from, to, includeBody: true);
var tasks = store.ReadTasks(from, to, dateField: "due");
var mail = store.ReadMail(from, to, flaggedOnly: true);
```

The library returns UTC dates and includes recoverable warnings alongside item
lists. `ReaderDateTime.Parse`, `ResolveTimeZone`, and `InTimeZone` provide shared
date interpretation and conversion. PowerShell converts these dates for output;
the CLI retains its existing JSON/CSV serializers and exit codes.

On PowerShell 7, a small loader isolates the underlying parser and its dependencies
from PowerShell's bundled assemblies. On Windows PowerShell, the .NET Framework
build uses the framework's cryptography assembly directly.
