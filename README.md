# pstreader

`pstreader` is a Windows CLI that reads PST/OST files without Microsoft Outlook,
Office, MAPI, or an external runtime. It runs with standard user privileges and
exports store information, folder structures, calendar items, tasks, and mail as
JSON or CSV.

The parser is implemented in C# and includes the source for the XstReader-derived
`XstReader.Base` library. The distribution build is a self-contained, single EXE
that includes the .NET runtime.

## Features

- Detects the format using the `!BDN` header, content type, and version rather than the file extension.
- Supports ANSI PST (versions 14/15) and Unicode PST (version 23).
- Supports Outlook 2013 and later 4 KiB OST (version 36, `wVer=0x24`).
- Reads files such as `.nst` with an OST internal header, regardless of their extension.
- Lists store information and all folders.
- Extracts calendar subjects, start and end times, locations, all-day flags, and message classes.
- Optionally extracts plain text, HTML, or decompressed RTF bodies.
- Supports date range filtering and JSON/CSV output.

## Usage

```text
pstreader info <file> [--format json|csv] [--output <file>]
pstreader folders <file> [--format json|csv] [--output <file>]
pstreader calendar <file> [--from <date>] [--to <date>]
                   [--format json|csv] [--output <file>]
                   [--output-time-zone <local|utc|time-zone-id>]
                   [--include-body]
```

The command defaults to `calendar` when omitted. If `--output` is omitted, data
is written to standard output. Warnings and errors are written to standard error.

### Display store information

```powershell
.\pstreader.exe info "C:\Data\archive.pst" --format json
```

The output includes the PST/OST type detected from the header, version, layout,
page size, file size, root folder, folder count, and the sum of the item counts
declared by each folder.

### List folders as CSV

```powershell
.\pstreader.exe folders "C:\Data\mailbox.ost" --format csv --output folders.csv
```

### Export calendar items within a date range as JSON

```powershell
.\pstreader.exe calendar "C:\Data\mailbox.ost" `
  --from "2026-01-01T00:00:00+09:00" `
  --to "2026-02-01T00:00:00+09:00" `
  --format json `
  --output calendar.json
```

The range is normalized to UTC internally and treated as a half-open interval
that includes `--from` and excludes `--to`. Any appointment overlapping the range
is exported. Input dates without a UTC offset or `Z` are interpreted in the local
time zone. Specify an explicit UTC offset for times that do not exist or are
ambiguous because of daylight saving time transitions.

Start and end times are converted to the local time zone by default. To output
UTC or another time zone, use:

```powershell
# UTC
.\pstreader.exe calendar "C:\Data\mailbox.ost" `
  --from "2026-01-01" --to "2026-02-01" `
  --output-time-zone utc

# Windows TimeZoneInfo ID
.\pstreader.exe calendar "C:\Data\mailbox.ost" `
  --output-time-zone "Tokyo Standard Time"
```

`local` explicitly selects the local time zone. `--time-zone` is a shorter alias
for `--output-time-zone`. For other zones, use an ID that
`TimeZoneInfo.FindSystemTimeZoneById` can resolve in the current environment.

The CSV columns are:

```text
folderPath,subject,start,end,location,isAllDay,messageClass
```

JSON output is an array with camelCase properties of the same names.

### Export calendar bodies

By default, the existing output fields are preserved and bodies are omitted.
Specify `--include-body` to include them.

```powershell
.\pstreader.exe calendar "C:\Data\mailbox.ost" `
  --include-body `
  --format json `
  --output calendar-with-body.json
```

JSON adds `body` and `bodyFormat`. The format is `text`, `html`, or `rtf`. Both
properties are `null` if no supported body property is available. CSV appends
`body,bodyFormat` columns. Bodies containing newlines, commas, or double quotes
are quoted according to standard CSV rules.

HTML is output as an HTML string, and RTF as a decompressed RTF string. Formatting
is not stripped to produce plain text. Bodies may be unavailable for items whose
bodies have not been synchronized to the OST, or for encrypted or protected items.

## Calendar item detection

Items whose `PidTagMessageClass` starts with `IPM.Appointment` are selected.
Calendar-specific values are obtained primarily from the following named
properties after resolving the PST/OST Name-to-ID Map.

| Output | MAPI property |
|---|---|
| Start | `PidLidAppointmentStartWhole` (`0x820D`) |
| End | `PidLidAppointmentEndWhole` (`0x820E`) |
| Location | `PidLidLocation` (`0x8208`) |
| All-day flag | `PidLidAppointmentSubType` (`0x8215`) |
| Body | `PidTagBody` / `PidTagHtml` / `PidTagRtfCompressed` |

Start and end times also fall back to `PidLidCommonStart` / `PidLidCommonEnd` and
`PidTagStartDate` / `PidTagEndDate`. For recurring appointments, range filtering
uses `PidLidClipStart` / `PidLidClipEnd` when available.

Recurring appointments are not currently expanded into individual occurrences.
Each master item stored in the PST/OST is exported as one item.

## Retrieve tasks and mail

```powershell
# Tasks whose due dates fall within the specified range
.\pstreader.exe tasks "C:\Data\mailbox.ost" --date-field due `
  --from "2026-01-01" --to "2026-02-01" --output tasks.json

# Filter mail by sent date and export CSV with bodies
.\pstreader.exe mail "C:\Data\mailbox.ost" --date-field sent `
  --from "2026-01-01" --to "2026-02-01" --include-body `
  --format csv --output mail.csv

# Retrieve only flagged mail that has not been completed
.\pstreader.exe mail "C:\Data\mailbox.ost" --flagged-only --include-body
```

Both commands support `--format`, `--output`, `--from`, `--to`,
`--output-time-zone` (alias `--time-zone`), and `--include-body`.

| Command | --date-field | Default |
|---|---|---|
| tasks | start / due / completed | due |
| mail | received / sent | received |

The range is evaluated against the selected single date as `from <= date < to`.
If the selected date is missing, no other date is substituted; the item is
excluded only when a range is specified. Without a range, items with missing
dates are included. Items are sorted by the selected date in ascending order,
then by subject, with missing dates placed last. Input date interpretation and
output time zones follow the calendar rules.

All folders are scanned for `IPM.Task` tasks, `IPM.Note` mail, and their respective
dot-separated derived classes. Flagged mail is not included as tasks. Attachments
are not saved, and recurring tasks are not expanded into individual occurrences.

### Output fields

Task CSV columns (JSON is an array with properties of the same names):

```text
folderPath,subject,start,due,completed,status,percentComplete,isComplete,messageClass
```

Task dates, status, progress, and completion flags are read from named properties
in `PSETID_Task`. `status` is the stored integer (0: not started, 1: in progress,
2: complete, 3: waiting, 4: deferred). `percentComplete` is the stored fraction
multiplied by 100. `isComplete` is the stored completion flag and is not inferred
from status or progress.

Mail CSV columns:

```text
folderPath,subject,senderName,senderAddress,to,cc,bcc,received,sent,isRead,hasAttachments,flagStatus,messageClass
```

Sender and recipient addresses prefer stored SMTP addresses, falling back to
stored addresses when SMTP addresses are unavailable. Recipients are formatted as
`Display name <address>` and joined with `; `. If only a name or address is
available, that value is used. If the recipient table has no values, stored
display recipients are used. `received` is the received date, and `sent` is the
sent date.

`flagStatus` is read from `PidTagFlagStatus` (0x1090): missing or 0 becomes `none`,
1 becomes `complete`, 2 becomes `flagged`, and other values become `unknown`.
`--flagged-only` is available only for `mail` and selects only `flagged` messages,
excluding completed ones. When combined with a date range, both conditions must
be satisfied. `flagStatus` is included even when this option is omitted.

Missing dates, status, progress, and Boolean values are `null` in JSON and empty
in CSV. `body,bodyFormat` are appended only when `--include-body` is specified.
Body formats and extraction follow the calendar rules.

## Files in use

The program itself does not require administrator privileges. However, if Outlook
or another application holds exclusive byte-range locks on parts of the file,
parsing may fail even with shared reading enabled. Close Outlook and Teams, or
create a readable copy before running the command.

## Exit codes

| Code | Meaning |
|---:|---|
| `0` | Success |
| `2` | Invalid command-line arguments |
| `3` | Failed to read the input file or PST/OST |

When individual corrupt items or folders can be skipped, processing continues
successfully and warnings are written to standard error.

## Development

Use the .NET 10 SDK.

```powershell
dotnet build .\PstReader.slnx -c Release
dotnet test .\tests\PstReader.Tests\PstReader.Tests.csproj -c Release
```

### Publish a single EXE

```powershell
.\scripts\publish.ps1
```

Alternatively, run:

```powershell
dotnet publish .\src\PstReader\PstReader.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\publish\win-x64
```

The output is `artifacts\publish\win-x64\pstreader.exe`. Target machines do not
need separate installations of .NET, Outlook, Office, or XstReader.

## Third-party code

The source, pinned upstream commit, and local compatibility changes for the
included `XstReader.Base` are documented in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md). The full license is included in
[`licenses/XstReader-MS-PL.md`](licenses/XstReader-MS-PL.md).
