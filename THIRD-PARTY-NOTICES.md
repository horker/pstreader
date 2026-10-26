# Third-party notices

## XstReader.Base

This repository includes the `XstReader.Base` parser from
[XstReaderNext](https://github.com/NeedsCoffee/XstReaderNext), commit
`71058c40035ba589d1617514854624802188de25`, which is a maintained fork of
the original [Dijji/XstReader](https://github.com/Dijji/XstReader).

The included source retains its copyright notices and is distributed under
the Microsoft Public License (MS-PL). The complete license is included at
`licenses/XstReader-MS-PL.md`.

Local modifications: header validation accepts both the PST client magic
(`SM`) and the OST client magic (`SO`). For 4 KiB OST data blocks, the fork's
strict trailer check is disabled to retain the original XstReader behavior;
the format is undocumented and the strict check rejects valid Outlook OSTs.
`Message.GetBodyAsHtmlString` also has an optional mode that preserves decoded
Unicode characters instead of converting them to numeric HTML entities.

The parser also builds for .NET Framework 4.7.2 and .NET Standard 2.0 for the
PowerShell module. Older frameworks use a compatibility implementation of
`Stream.ReadExactly` that loops over partial reads and throws on premature EOF.
The .NET Framework build references `System.Security`; other portable builds
reference `System.Security.Cryptography.Pkcs` 10.0.13.
