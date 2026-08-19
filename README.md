# CSViewer

CSVをExcelで開かず、すべての項目を文字列のまま安全に確認するWindows向けGUIアプリです。

- 対象フレームワーク: .NET Framework 4.8
- UI: Windows Forms
- 言語: VB.NET
- 外部DLL・NuGetパッケージ: 不要

## 主な機能

- ファイル選択、ドラッグ＆ドロップ、コマンドライン引数からの読み込み
- UTF-8（BOMあり・なし）、Shift_JIS、UTF-16 LE/BEの読み込み
- 文字コードの自動判定または明示指定
- カンマ、タブ、セミコロン、パイプの自動判定または明示指定
- ヘッダー有無の切り替え
- すべての列を文字列として表示
  - 先頭ゼロを保持
  - 長い番号を指数表示しない
  - 日付を自動変換しない
- 引用符内のカンマ、改行、二重引用符に対応
- 空行を空レコードとして保持
- 全列を対象にした文字列検索
- 読み込んだCSVを仮想テーブルとして検索できるSQLコンソール
- 列ヘッダーをクリックした文字列ソート（昇順 → 降順 → 解除）
- 列数不一致、CSV構文エラー、空・重複ヘッダーの検出
- CSV構文エラーのあるレコードも原文を保持して表示・保存
- 問題のあるレコードを赤色表示
- 問題一覧のダブルクリックによる該当レコードへの移動
- UTF-8、Shift_JIS、UTF-16およびCRLF/LFを指定した別名保存
- 検索結果だけを保存可能
- 元ファイルと同じ保存先を選んだ場合の上書き確認

## ビルド

1. Visual Studioで CsvPreviewer.sln を開きます。
2. .NET Framework 4.8 Developer Packが未導入の場合は追加します。
3. Release / Any CPU でビルドします。
4. CsvPreviewer\bin\Release\CSViewer.exe を実行します。

Visual Studio Developer Command Promptでは build.bat でもビルドできます。

## テスト

外部テストフレームワークを使用しないスモークテストを同梱しています。Visual Studio Developer Command Promptで run-tests.bat を実行してください。

次の内容を確認します。

- 引用符内のカンマと改行
- 不正な引用符の検出
- 末尾の空項目
- 列数不一致
- タブ区切りの自動判定
- UTF-8とShift_JISの自動判定
- 先頭ゼロの保持
- CSVエスケープ
- UTF-8 BOM付き保存

## 操作

- Ctrl + O: CSVを開く
- Ctrl + F: 検索欄へ移動
- Ctrl + Q: SQLコンソールを開く
- F5: 現在の設定で再読込
- Ctrl + S: 別名保存

グリッド左端の数字は、表示順ではなく元CSVの物理行番号です。引用符内に改行がある場合も、レコードの開始行を示します。

列ヘッダーを繰り返しクリックすると、昇順、降順、ソート解除の順に切り替わります。解除すると元CSVの行順へ戻ります。

## SQLコンソール

CSVを開いた後に「SQL...」ボタンまたは Ctrl + Q で起動します。現在読み込んでいるCSVは、読み取り専用の仮想テーブル `csv` として参照できます。

```sql
SELECT C1 AS コード, [名称]
FROM csv
WHERE [都道府県] = '東京都'
ORDER BY C1
LIMIT 100;
```

- 列は `C1`、`C2`…または一意なヘッダー名で参照できます。
- 空白やSQL予約語を含むヘッダーは `[ヘッダー名]` のように角括弧で囲みます。
- `SELECT`、`WHERE`、`LIKE`、`IN`、`ORDER BY`、`DISTINCT`、`TOP`、`LIMIT`、`COUNT(*)` に対応します。
- SQL式では `LEN`、`TRIM`、`SUBSTRING`、`CONVERT`、`ISNULL`、`IIF` も使用できます。
- `SELECT`、`WHERE`、`ORDER BY`で `LTRIM`、`RTRIM`、`CONCAT`、`TO_CHAR`、`TO_NUMBER`、`CASE WHEN`、`LPAD`、`RPAD` を使用できます。
- `TO_CHAR`は文字列化に加え、数値書式や `YYYY/MM/DD` などの日時書式を指定できます。
- `TO_NUMBER(値 [, 書式])`でCSVの文字列を数値化し、数値として抽出・並べ替えできます。書式では `G`（桁区切り）と `D`（小数点）を指定できます。
- 元CSVの値はすべて文字列として比較されます。`INSERT`、`UPDATE`、`DELETE`などの更新SQLは実行できません。

```sql
SELECT
    LPAD(C1, 8, '0') AS コード,
    CONCAT(LTRIM([姓]), ' ', RTRIM([名])) AS 氏名,
    CASE WHEN [状態] = '1' THEN '有効' ELSE '無効' END AS 状態名
FROM csv
WHERE LTRIM([都道府県]) = '東京都';
```

数値が文字列として格納された列を数値順に扱う例です。

```sql
SELECT C1, TO_NUMBER([金額]) AS 金額
FROM csv
WHERE TO_NUMBER([金額]) >= 1000
ORDER BY TO_NUMBER([金額]) DESC;
```

## 制限事項

- 初版は閲覧専用です。セルを直接編集する機能はありません。
- 文字コードの自動判定は、BOMを優先し、BOMなしで妥当なUTF-8でなければShift_JISとして扱います。
- ASCIIだけのファイルはUTF-8として判定されます。
- 区切り文字の自動判定は先頭30レコードを使用します。
- 列ヘッダーの並べ替えは文字列順です。数値順が必要な場合はSQLコンソールで `ORDER BY TO_NUMBER(列)` を使用します。
