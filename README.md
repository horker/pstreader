# pstreader

`pstreader` は、Microsoft Outlook、Office、MAPI、および外部ランタイムを必要とせずに
PST/OST を読み取る Windows 用 CLI です。一般ユーザー権限で動作し、ストア情報、
フォルダー構造、およびカレンダー項目を JSON または CSV で出力します。

解析処理は C# で実装され、XstReader 系の `XstReader.Base` をソースとして同梱しています。
配布用ビルドは .NET ランタイムを含む self-contained の単一 EXE です。

## 主な機能

- ファイル拡張子ではなく `!BDN` ヘッダ、content type、および version で形式を判定します。
- PST の ANSI 形式（version 14/15）と Unicode 形式（version 23）を扱います。
- Outlook 2013 以降の 4 KiB OST（version 36、`wVer=0x24`）を扱います。
- 内部ヘッダが OST である `.nst` 等も、拡張子に関係なく読み取ります。
- ストア情報および全フォルダーを列挙します。
- カレンダー項目の件名、開始、終了、場所、終日フラグ、Message Class を抽出します。
- 期間フィルターと JSON/CSV 出力に対応します。

## 使用方法

```text
pstreader info <file> [--format json|csv] [--output <file>]
pstreader folders <file> [--format json|csv] [--output <file>]
pstreader calendar <file> [--from <date>] [--to <date>]
                   [--format json|csv] [--output <file>]
```

コマンドを省略した場合は `calendar` として扱います。`--output` を省略すると標準出力へ
書き込みます。データ以外の警告とエラーは標準エラーへ書き込みます。

### ストア情報を表示する

```powershell
.\pstreader.exe info "C:\Data\archive.pst" --format json
```

出力には、ヘッダから判定した PST/OST、version、レイアウト、ページサイズ、ファイルサイズ、
ルートフォルダー、フォルダー数、および各フォルダーが宣言する項目数の合計が含まれます。

### フォルダーを CSV で列挙する

```powershell
.\pstreader.exe folders "C:\Data\mailbox.ost" --format csv --output folders.csv
```

### 期間を指定して予定を JSON へ出力する

```powershell
.\pstreader.exe calendar "C:\Data\mailbox.ost" `
  --from "2026-01-01T00:00:00+09:00" `
  --to "2026-02-01T00:00:00+09:00" `
  --format json `
  --output calendar.json
```

期間は UTC に正規化し、`--from` を含み `--to` を含まない半開区間として扱います。
期間に少しでも重なる予定を出力します。タイムゾーンを省略した日時は UTC として解釈し、
開始・終了日時は UTC で出力します。

CSV の列は次のとおりです。

```text
folderPath,subject,start,end,location,isAllDay,messageClass
```

JSON では同名の camelCase プロパティを持つ配列を出力します。

## カレンダー項目の判定

`PidTagMessageClass` が `IPM.Appointment` で始まる項目を対象とします。カレンダー固有値は
PST/OST の Name-to-ID Map を解決してから、主に次の named properties から取得します。

| 出力 | MAPI プロパティ |
|---|---|
| 開始 | `PidLidAppointmentStartWhole` (`0x820D`) |
| 終了 | `PidLidAppointmentEndWhole` (`0x820E`) |
| 場所 | `PidLidLocation` (`0x8208`) |
| 終日 | `PidLidAppointmentSubType` (`0x8215`) |

開始・終了には `PidLidCommonStart` / `PidLidCommonEnd` と
`PidTagStartDate` / `PidTagEndDate` のフォールバックも使用します。繰り返し予定の期間判定には
利用可能な場合に `PidLidClipStart` / `PidLidClipEnd` を使用します。

現時点では繰り返し予定を個々の発生に展開せず、PST/OST に保存されたマスター項目を
1 件として出力します。

## ファイルが使用中の場合

プログラム自体に管理者権限は不要です。ただし、Outlook 等がファイルの一部に排他的な
バイト範囲ロックを設定している場合、共有読み取りを指定しても解析できません。その場合は
Outlook と Teams を終了するか、読み取り可能なコピーを作成してから実行してください。

## 終了コード

| コード | 意味 |
|---:|---|
| `0` | 成功 |
| `2` | コマンドライン引数が不正 |
| `3` | 入力ファイルまたは PST/OST の読み取りに失敗 |

破損した個別項目またはフォルダーを読み飛ばせる場合、処理は成功として継続し、警告を
標準エラーへ出力します。

## 開発

.NET 10 SDK を使用します。

```powershell
dotnet build .\PstReader.slnx -c Release
dotnet test .\tests\PstReader.Tests\PstReader.Tests.csproj -c Release
```

### 単一 EXE を発行する

```powershell
.\scripts\publish.ps1
```

または、直接次のコマンドを実行します。

```powershell
dotnet publish .\src\PstReader\PstReader.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\publish\win-x64
```

出力は `artifacts\publish\win-x64\pstreader.exe` です。配布先には .NET、Outlook、
Office、XstReader の別ファイルをインストールする必要はありません。

## 第三者コード

同梱する `XstReader.Base` の出典、固定した上流コミット、およびローカル互換修正は
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) に記載しています。ライセンス全文は
[`licenses/XstReader-MS-PL.md`](licenses/XstReader-MS-PL.md) に含まれます。

