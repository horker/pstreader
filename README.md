# pstreader

`pstreader` は、Microsoft Outlook、Office、MAPI、および外部ランタイムを必要とせずに
PST/OST を読み取る Windows 用 CLI です。一般ユーザー権限で動作し、ストア情報、
フォルダー構造、カレンダー、タスク、およびメールを JSON または CSV で出力します。

解析処理は C# で実装され、XstReader 系の `XstReader.Base` をソースとして同梱しています。
配布用ビルドは .NET ランタイムを含む self-contained の単一 EXE です。

## 主な機能

- ファイル拡張子ではなく `!BDN` ヘッダ、content type、および version で形式を判定します。
- PST の ANSI 形式（version 14/15）と Unicode 形式（version 23）を扱います。
- Outlook 2013 以降の 4 KiB OST（version 36、`wVer=0x24`）を扱います。
- 内部ヘッダが OST である `.nst` 等も、拡張子に関係なく読み取ります。
- ストア情報および全フォルダーを列挙します。
- カレンダー項目の件名、開始、終了、場所、終日フラグ、Message Class を抽出します。
- 必要に応じて、プレーンテキスト、HTML、または展開済み RTF の本文を抽出します。
- 期間フィルターと JSON/CSV 出力に対応します。

## 使用方法

```text
pstreader info <file> [--format json|csv] [--output <file>]
pstreader folders <file> [--format json|csv] [--output <file>]
pstreader calendar <file> [--from <date>] [--to <date>]
                   [--format json|csv] [--output <file>]
                   [--output-time-zone <local|utc|time-zone-id>]
                   [--include-body]
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

期間は内部で UTC に正規化し、`--from` を含み `--to` を含まない半開区間として扱います。
期間に少しでも重なる予定を出力します。入力日時に UTC オフセットや `Z` がない場合は、
実行環境のローカルタイムゾーンとして解釈します。夏時間の切り替えにより存在しない日時、
または二通りに解釈できる日時には、明示的な UTC オフセットを指定してください。

開始・終了日時は既定で実行環境のローカルタイムゾーンに変換して出力します。UTC または
別のタイムゾーンで出力する場合は、次のように指定します。

```powershell
# UTC
.\pstreader.exe calendar "C:\Data\mailbox.ost" `
  --from "2026-01-01" --to "2026-02-01" `
  --output-time-zone utc

# Windows の TimeZoneInfo ID
.\pstreader.exe calendar "C:\Data\mailbox.ost" `
  --output-time-zone "Tokyo Standard Time"
```

`local` はローカルタイムゾーンを明示します。`--time-zone` は
`--output-time-zone` の短縮別名です。任意指定には、実行環境で
`TimeZoneInfo.FindSystemTimeZoneById` が解決できるタイムゾーン ID を使用します。

CSV の列は次のとおりです。

```text
folderPath,subject,start,end,location,isAllDay,messageClass
```

JSON では同名の camelCase プロパティを持つ配列を出力します。

### 予定の本文を出力する

既定では従来と同じ出力項目を維持し、本文を出力しません。本文が必要な場合は
`--include-body` を指定します。

```powershell
.\pstreader.exe calendar "C:\Data\mailbox.ost" `
  --include-body `
  --format json `
  --output calendar-with-body.json
```

JSON には `body` と `bodyFormat` が追加されます。`bodyFormat` は `text`、`html`、
`rtf` のいずれかです。対応する本文プロパティがない場合は、両方とも `null` になります。
CSV では末尾に `body,bodyFormat` 列を追加します。改行、カンマ、二重引用符を含む本文は
通常の CSV 規則に従って引用します。

HTML はHTML文字列として、RTFは圧縮解除後のRTF文字列として出力します。書式を除いた
プレーンテキストへの変換は行いません。OSTに本文が同期されていない項目や、暗号化・保護
された項目では本文を取得できない場合があります。

## カレンダー項目の判定

`PidTagMessageClass` が `IPM.Appointment` で始まる項目を対象とします。カレンダー固有値は
PST/OST の Name-to-ID Map を解決してから、主に次の named properties から取得します。

| 出力 | MAPI プロパティ |
|---|---|
| 開始 | `PidLidAppointmentStartWhole` (`0x820D`) |
| 終了 | `PidLidAppointmentEndWhole` (`0x820E`) |
| 場所 | `PidLidLocation` (`0x8208`) |
| 終日 | `PidLidAppointmentSubType` (`0x8215`) |
| 本文 | `PidTagBody` / `PidTagHtml` / `PidTagRtfCompressed` |

開始・終了には `PidLidCommonStart` / `PidLidCommonEnd` と
`PidTagStartDate` / `PidTagEndDate` のフォールバックも使用します。繰り返し予定の期間判定には
利用可能な場合に `PidLidClipStart` / `PidLidClipEnd` を使用します。

現時点では繰り返し予定を個々の発生に展開せず、PST/OST に保存されたマスター項目を
1 件として出力します。

## タスクとメールを取得する

```powershell
# 期限が指定期間に含まれるタスク
.\pstreader.exe tasks "C:\Data\mailbox.ost" --date-field due `
  --from "2026-01-01" --to "2026-02-01" --output tasks.json

# 送信日時でメールを絞り込み、本文付きCSVを出力
.\pstreader.exe mail "C:\Data\mailbox.ost" --date-field sent `
  --from "2026-01-01" --to "2026-02-01" --include-body `
  --format csv --output mail.csv

# 未完了のフラグ付きメールのみ取得
.\pstreader.exe mail "C:\Data\mailbox.ost" --flagged-only --include-body
```

両コマンドで `--format`、`--output`、`--from`、`--to`、
`--output-time-zone`（別名 `--time-zone`）、`--include-body` を使用できます。

| コマンド | --date-field | 既定値 |
|---|---|---|
| tasks | start / due / completed | due |
| mail | received / sent | received |

期間は選択した単一日時について `from <= 日時 < to` と判定します。
選択した日時がない場合、別の日時への代替は行わず、期間指定時だけ除外します。
期間指定がなければ日時のない項目も取得します。選択日時の昇順、次に件名の順で
出力し、日時のない項目は末尾に配置します。日時の入力解釈と出力タイムゾーンは
カレンダーと共通です。

全フォルダーから、タスクは `IPM.Task`、メールは `IPM.Note` と、それぞれの
ドット区切りの派生クラスを取得します。フラグ付きメールをタスクに含めません。
添付ファイルの保存と繰り返しタスクの個別展開は行いません。

### 出力項目

タスクのCSV列（JSONでも同名のプロパティを持つ配列）：

```text
folderPath,subject,start,due,completed,status,percentComplete,isComplete,messageClass
```

タスクの日時・状態・進捗・完了フラグは `PSETID_Task` の名前付きプロパティから取得します。
`status` は保存された整数（0: 未開始、1: 進行中、2: 完了、3: 待機、4: 延期）、
`percentComplete` は保存された比率を100倍した百分率です。
`isComplete` は保存された完了フラグであり、状態や進捗から推測しません。

メールのCSV列：

```text
folderPath,subject,senderName,senderAddress,to,cc,bcc,received,sent,isRead,hasAttachments,flagStatus,messageClass
```

差出人・宛先は保存されたSMTPアドレスを優先し、なければ保存されたアドレスを使用します。
宛先は `表示名 <アドレス>` を `; ` で連結した文字列です。片方しかない場合はその値を
使用し、宛先テーブルに値がない場合は保存された表示宛先を使用します。
`received` は受信日時、`sent` は送信日時です。

`flagStatus` は `PidTagFlagStatus`（0x1090）から取得し、未設定・0は `none`、
1は `complete`、2は `flagged`、その他は `unknown` とします。
`--flagged-only` は `mail` 専用で、`flagged` のメールだけを取得し、完了済みは除外します。
期間指定との併用時は両方の条件を満たす必要があります。
このオプションを省略した場合も `flagStatus` を出力します。

欠損した日時・状態・進捗・真偽値はJSONで `null`、CSVで空欄です。
本文は `--include-body` 指定時のみ末尾に `body,bodyFormat` を追加します。
本文形式と抽出方法はカレンダーと共通です。

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
