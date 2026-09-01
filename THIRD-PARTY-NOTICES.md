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
