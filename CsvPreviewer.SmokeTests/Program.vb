Imports System
Imports System.Data
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports CsvPreviewer

Friend Module Program
    Private _passed As Integer

    <STAThread>
    Public Sub Main()
        Try
            RunTest("引用符・改行・末尾空項目", AddressOf ParseQuotedFields)
            RunTest("列数不一致の検出", AddressOf DetectColumnMismatch)
            RunTest("不正な引用符の検出", AddressOf DetectMalformedQuotes)
            RunTest("区切り文字の自動判定", AddressOf DetectTabDelimiter)
            RunTest("UTF-8とShift_JISの判定", AddressOf DetectTextEncoding)
            RunTest("DataTableで先頭ゼロを保持", AddressOf PreserveLeadingZeros)
            RunTest("CSVエスケープ", AddressOf EscapeCsvFields)
            RunTest("BOM付きUTF-8で保存", AddressOf ExportUtf8WithBom)
            RunTest("不揃いな列数と空ヘッダーを保持して保存", AddressOf PreserveOriginalShapeOnExport)
            RunTest("不正レコードの原文保持", AddressOf PreserveMalformedRecordOnExport)
            RunTest("空レコードの保持", AddressOf PreserveBlankRecordOnExport)
            RunTest("曖昧なShift_JISの判定", AddressOf DetectAmbiguousShiftJis)
            RunTest("BOMなしUTF-16の判定", AddressOf DetectBomlessUtf16)
            RunTest("フィールド内改行コードの統一", AddressOf NormalizeEmbeddedNewLines)
            RunTest("ファイルのストリーミング復号", AddressOf DecodeFileUsingStreaming)
            RunTest("SQLの抽出・条件・並べ替え", AddressOf ExecuteSqlSelect)
            RunTest("SQLのDISTINCT・COUNT・LIMIT", AddressOf ExecuteSqlAggregates)
            RunTest("SQLの不正入力検出", AddressOf RejectInvalidSql)
            RunTest("SQL文字列関数", AddressOf ExecuteSqlStringFunctions)
            RunTest("SQLのCASE WHEN", AddressOf ExecuteSqlCaseWhen)
            RunTest("SQLのTO_NUMBER", AddressOf ExecuteSqlToNumber)
            RunTest("アプリアイコンの埋め込み", AddressOf LoadApplicationIcon)
            RunTest("列ヘッダーの3状態ソート", AddressOf CycleGridColumnSort)

            Console.WriteLine()
            Console.WriteLine("全{0}件のテストに成功しました。", _passed)
            Environment.ExitCode = 0
        Catch ex As Exception
            Console.Error.WriteLine()
            Console.Error.WriteLine("テスト失敗: " & ex.Message)
            Console.Error.WriteLine(ex.ToString())
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Sub RunTest(name As String, testAction As Action)
        testAction()
        _passed += 1
        Console.WriteLine("[OK] " & name)
    End Sub

    Private Sub ParseQuotedFields()
        Dim csv As String =
            "code,name,note" & ControlChars.CrLf &
            "0000123,""A,B"",""1行目" & ControlChars.CrLf & "2行目""" & ControlChars.CrLf &
            "9999999,終端,"

        Dim document As CsvDocument = CsvParser.ParseText(csv, ",", True)
        AssertEqual(3, document.ExpectedColumnCount, "列数")
        AssertEqual(2, document.DataRowCount, "データ行数")
        AssertEqual("0000123", document.Records(1).Fields(0), "先頭ゼロ")
        AssertEqual("A,B", document.Records(1).Fields(1), "引用符内カンマ")
        AssertEqual("1行目" & ControlChars.CrLf & "2行目",
                    document.Records(1).Fields(2),
                    "引用符内改行")
        AssertEqual(String.Empty, document.Records(2).Fields(2), "末尾空項目")
    End Sub

    Private Sub DetectColumnMismatch()
        Dim csv As String =
            "a,b,c" & ControlChars.CrLf &
            "1,2,3" & ControlChars.CrLf &
            "4,5"

        Dim document As CsvDocument = CsvParser.ParseText(csv, ",", True)
        AssertTrue(document.Records(2).HasIssue, "列不足行に問題フラグがありません。")
        AssertTrue(
            document.Issues.Exists(
                Function(issue As CsvIssue) issue.Category = "列数不一致"),
            "列数不一致が問題一覧にありません。")
    End Sub

    Private Sub DetectMalformedQuotes()
        Dim csv As String =
            "a,b" & ControlChars.CrLf &
            "1,""閉じていない項目"
        Dim document As CsvDocument = CsvParser.ParseText(csv, ",", True)

        AssertTrue(
            document.Issues.Exists(
                Function(issue As CsvIssue) issue.Category = "CSV構文"),
            "不正な引用符が問題一覧にありません。")
    End Sub

    Private Sub DetectTabDelimiter()
        Dim tsv As String =
            "code" & ControlChars.Tab & "name" & ControlChars.Tab & "note" & ControlChars.CrLf &
            "001" & ControlChars.Tab & "東京" & ControlChars.Tab & "確認"

        AssertEqual(ControlChars.Tab.ToString(),
                    CsvParser.DetectDelimiter(tsv),
                    "タブ区切りの判定")
    End Sub

    Private Sub DetectTextEncoding()
        Dim original As String = "コード,名称" & ControlChars.CrLf & "0000001,東京"

        Dim utf8Bytes As Byte() = New UTF8Encoding(False).GetBytes(original)
        Dim utf8 As DecodedCsvText =
            CsvTextCodec.DecodeBytes(utf8Bytes, CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf8NoBom, utf8.EncodingKind, "UTF-8判定")
        AssertEqual(original, utf8.Text, "UTF-8復号")

        Dim shiftJisBytes As Byte() = Encoding.GetEncoding(932).GetBytes(original)
        Dim shiftJis As DecodedCsvText =
            CsvTextCodec.DecodeBytes(shiftJisBytes, CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.ShiftJis, shiftJis.EncodingKind, "Shift_JIS判定")
        AssertEqual(original, shiftJis.Text, "Shift_JIS復号")
    End Sub

    Private Sub PreserveLeadingZeros()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "code,name" & ControlChars.CrLf & "0000123,テスト",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        AssertEqual(GetType(String), table.Columns(0).DataType, "列のデータ型")
        AssertEqual("0000123", Convert.ToString(table.Rows(0)(0)), "先頭ゼロ")
    End Sub

    Private Sub EscapeCsvFields()
        AssertEqual("plain", CsvExporter.EscapeField("plain", ","), "通常項目")
        AssertEqual("""A,B""", CsvExporter.EscapeField("A,B", ","), "カンマ")
        AssertEqual("""A""""B""", CsvExporter.EscapeField("A""B", ","), "引用符")
        AssertEqual(""" 001 """, CsvExporter.EscapeField(" 001 ", ","), "前後空白")
    End Sub

    Private Sub ExportUtf8WithBom()
        Dim temporaryDirectory As String =
            Path.Combine(
                Path.GetTempPath(),
                "CsvPreviewerTests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(temporaryDirectory)

        Try
            Dim outputPath As String = Path.Combine(temporaryDirectory, "output.csv")
            Dim document As CsvDocument =
                CsvParser.ParseText(
                    "code,name" & ControlChars.CrLf &
                    "0000001,""東京,本店""",
                    ",",
                    True)
            Dim table As DataTable = CsvTableBuilder.Build(document)

            CsvExporter.Export(
                outputPath,
                table.DefaultView,
                CsvTableBuilder.GetVisibleColumnCount(table),
                ",",
                True,
                CsvTextEncoding.Utf8Bom,
                ControlChars.CrLf)

            Dim bytes As Byte() = File.ReadAllBytes(outputPath)
            AssertTrue(bytes.Length >= 3, "出力ファイルが短すぎます。")
            AssertEqual(CByte(&HEF), bytes(0), "UTF-8 BOM 1バイト目")
            AssertEqual(CByte(&HBB), bytes(1), "UTF-8 BOM 2バイト目")
            AssertEqual(CByte(&HBF), bytes(2), "UTF-8 BOM 3バイト目")

            Dim outputText As String = File.ReadAllText(outputPath, Encoding.UTF8)
            AssertTrue(outputText.Contains("""東京,本店"""), "CSVの引用符が保存されていません。")
        Finally
            If Directory.Exists(temporaryDirectory) Then
                Directory.Delete(temporaryDirectory, True)
            End If
        End Try
    End Sub

    Private Sub PreserveOriginalShapeOnExport()
        Dim temporaryDirectory As String =
            Path.Combine(
                Path.GetTempPath(),
                "CsvPreviewerShapeTests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(temporaryDirectory)

        Try
            Dim outputPath As String = Path.Combine(temporaryDirectory, "shape.csv")
            Dim document As CsvDocument =
                CsvParser.ParseText(
                    ",name,note" & ControlChars.CrLf &
                    "0000001,東京" & ControlChars.CrLf &
                    "0000002,大阪,確認,余分",
                    ",",
                    True)
            Dim table As DataTable = CsvTableBuilder.Build(document)

            CsvExporter.Export(
                outputPath,
                table.DefaultView,
                CsvTableBuilder.GetVisibleColumnCount(table),
                ",",
                True,
                CsvTextEncoding.Utf8NoBom,
                ControlChars.CrLf)

            Dim lines As String() = File.ReadAllLines(outputPath, Encoding.UTF8)
            AssertEqual(",name,note", lines(0), "空ヘッダー")
            AssertEqual("0000001,東京", lines(1), "列不足行")
            AssertEqual("0000002,大阪,確認,余分", lines(2), "列超過行")
        Finally
            If Directory.Exists(temporaryDirectory) Then
                Directory.Delete(temporaryDirectory, True)
            End If
        End Try
    End Sub

    Private Sub PreserveMalformedRecordOnExport()
        Dim csv As String =
            "a,b" & ControlChars.CrLf &
            "1,""bad""x" & ControlChars.CrLf &
            "2,ok"
        Dim document As CsvDocument = CsvParser.ParseText(csv, ",", True)

        AssertEqual(3, document.Records.Count, "不正行を含むレコード数")
        AssertTrue(document.Records(1).IsMalformed,
                   "不正行に構文エラーフラグがありません。")
        AssertTrue(document.Records(1).HasIssue,
                   "不正行に問題フラグがありません。")
        AssertTrue(
            document.Issues.Exists(
                Function(issue As CsvIssue) issue.RecordNumber = 2),
            "構文エラーにレコード番号がありません。")

        Dim output As String = ExportToTemporaryText(
            document,
            ControlChars.CrLf)
        AssertEqual(
            csv & ControlChars.CrLf,
            output,
            "不正行を含む保存内容")
    End Sub

    Private Sub PreserveBlankRecordOnExport()
        Dim csv As String =
            "a,b" & ControlChars.CrLf &
            ControlChars.CrLf &
            "1,2"
        Dim document As CsvDocument = CsvParser.ParseText(csv, ",", True)

        AssertEqual(3, document.Records.Count, "空行を含むレコード数")
        AssertEqual(1, document.Records(1).Fields.Length, "空行の列数")
        AssertEqual(String.Empty, document.Records(1).Fields(0), "空行の値")

        Dim output As String = ExportToTemporaryText(
            document,
            ControlChars.CrLf)
        AssertEqual(
            csv & ControlChars.CrLf,
            output,
            "空行を含む保存内容")
    End Sub

    Private Sub DetectAmbiguousShiftJis()
        Dim bytes As Byte() = {CByte(&HC2), CByte(&HA9)}
        Dim decoded As DecodedCsvText =
            CsvTextCodec.DecodeBytes(bytes, CsvTextEncoding.AutoDetect)

        AssertEqual(CsvTextEncoding.ShiftJis,
                    decoded.EncodingKind,
                    "曖昧なShift_JIS判定")
        AssertEqual(Encoding.GetEncoding(932).GetString(bytes),
                    decoded.Text,
                    "曖昧なShift_JIS復号")
        AssertTrue(Not String.IsNullOrEmpty(decoded.DetectionWarning),
                   "曖昧な文字コードの警告がありません。")
    End Sub

    Private Sub DetectBomlessUtf16()
        Dim original As String = "a,b" & ControlChars.CrLf & "1,2"

        Dim littleEndian As DecodedCsvText =
            CsvTextCodec.DecodeBytes(
                Encoding.Unicode.GetBytes(original),
                CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf16LittleEndian,
                    littleEndian.EncodingKind,
                    "BOMなしUTF-16 LE判定")
        AssertEqual(original, littleEndian.Text, "BOMなしUTF-16 LE復号")

        Dim bigEndian As DecodedCsvText =
            CsvTextCodec.DecodeBytes(
                Encoding.BigEndianUnicode.GetBytes(original),
                CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf16BigEndian,
                    bigEndian.EncodingKind,
                    "BOMなしUTF-16 BE判定")
        AssertEqual(original, bigEndian.Text, "BOMなしUTF-16 BE復号")
    End Sub

    Private Sub NormalizeEmbeddedNewLines()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "a,b" & ControlChars.CrLf &
                "1,""x" & ControlChars.CrLf & "y""",
                ",",
                True)

        Dim output As String = ExportToTemporaryText(
            document,
            ControlChars.Lf)
        AssertTrue(output.IndexOf(ControlChars.Cr) < 0,
                   "LF指定の出力にCRが残っています。")
        AssertEqual(
            "a,b" & ControlChars.Lf &
            "1,""x" & ControlChars.Lf & "y""" & ControlChars.Lf,
            output,
            "フィールド内改行を含む保存内容")
    End Sub

    Private Sub DecodeFileUsingStreaming()
        Dim temporaryDirectory As String =
            Path.Combine(
                Path.GetTempPath(),
                "CSViewerStreamingTests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(temporaryDirectory)

        Try
            Dim filePath As String =
                Path.Combine(temporaryDirectory, "large.csv")
            Dim builder As New StringBuilder()
            builder.Append("code,name")
            For index As Integer = 1 To 20000
                builder.Append(ControlChars.CrLf)
                builder.Append(index.ToString("0000000"))
                builder.Append(",東京")
            Next
            Dim original As String = builder.ToString()
            File.WriteAllText(filePath, original, New UTF8Encoding(False))

            Dim decoded As DecodedCsvText =
                CsvTextCodec.DecodeFile(filePath, CsvTextEncoding.AutoDetect)
            AssertEqual(CsvTextEncoding.Utf8NoBom,
                        decoded.EncodingKind,
                        "ファイルのUTF-8判定")
            AssertEqual(original, decoded.Text, "ファイルのストリーミング復号")
        Finally
            Directory.Delete(temporaryDirectory, True)
        End Try
    End Sub

    Private Sub ExecuteSqlSelect()
        Dim table As DataTable = BuildSqlTestTable()
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                CsvTableBuilder.GetVisibleColumnCount(table),
                "SELECT C1 AS コード, [name] AS 名前 " &
                "FROM csv WHERE [city] = '東京' " &
                "ORDER BY コード DESC LIMIT 2;")

        AssertEqual(3, result.MatchedRowCount, "SQL一致行数")
        AssertEqual(2, result.ReturnedRowCount, "SQL結果行数")
        AssertEqual("コード", result.Table.Columns(0).ColumnName, "SQL列別名1")
        AssertEqual("名前", result.Table.Columns(1).ColumnName, "SQL列別名2")
        AssertEqual("002", Convert.ToString(result.Table.Rows(0)(0)), "SQL並べ替え")
        AssertEqual("B", Convert.ToString(result.Table.Rows(0)(1)), "SQLヘッダー参照")
    End Sub

    Private Sub ExecuteSqlAggregates()
        Dim table As DataTable = BuildSqlTestTable()
        Dim columnCount As Integer = CsvTableBuilder.GetVisibleColumnCount(table)

        Dim distinctResult As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                columnCount,
                "SELECT DISTINCT city FROM csv;")
        AssertEqual(2, distinctResult.ReturnedRowCount, "DISTINCT結果行数")

        Dim countResult As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                columnCount,
                "SELECT COUNT(*) AS 件数 FROM csv WHERE name LIKE '%B%';")
        AssertEqual(2L,
                    Convert.ToInt64(countResult.Table.Rows(0)(0)),
                    "COUNT結果")

        Dim inResult As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                columnCount,
                "SELECT COUNT(*) AS 件数 FROM csv " &
                "WHERE city IN ('東京', '大阪') AND LEN(C1) = 3;")
        AssertEqual(4L,
                    Convert.ToInt64(inResult.Table.Rows(0)(0)),
                    "IN・LEN結果")

        Dim topResult As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                columnCount,
                "SELECT TOP 1 * FROM csv ORDER BY C1;")
        AssertEqual(1, topResult.ReturnedRowCount, "TOP結果行数")
        AssertEqual("001", Convert.ToString(topResult.Table.Rows(0)(0)), "TOP結果")
    End Sub

    Private Sub RejectInvalidSql()
        Dim table As DataTable = BuildSqlTestTable()
        Dim columnCount As Integer = CsvTableBuilder.GetVisibleColumnCount(table)

        AssertThrowsCsvSql(
            Sub()
                CsvSqlEngine.Execute(
                    table,
                    columnCount,
                    "DELETE FROM csv;")
            End Sub,
            "更新SQL")
        AssertThrowsCsvSql(
            Sub()
                CsvSqlEngine.Execute(
                    table,
                    columnCount,
                    "SELECT unknown FROM csv;")
            End Sub,
            "存在しない列")
    End Sub

    Private Sub ExecuteSqlStringFunctions()
        Dim table As DataTable = BuildSqlFunctionTestTable()
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                CsvTableBuilder.GetVisibleColumnCount(table),
                "SELECT LTRIM(name) AS 左空白除去, " &
                "RTRIM(name) AS 右空白除去, " &
                "CONCAT('[', TRIM(name), ']') AS 結合, " &
                "TO_CHAR(code) AS 文字化, " &
                "TO_CHAR(amount, '00000') AS 金額書式, " &
                "TO_CHAR(date, 'YYYY/MM/DD') AS 日付書式, " &
                "LPAD(code, 5, '0') AS 左埋め, " &
                "RPAD(code, 6, 'ab') AS 右埋め " &
                "FROM csv " &
                "WHERE LTRIM(RTRIM(name)) = 'Alice' " &
                "ORDER BY 左埋め;")

        AssertEqual(1, result.ReturnedRowCount, "文字列関数の結果行数")
        AssertEqual("Alice  ", Convert.ToString(result.Table.Rows(0)(0)), "LTRIM")
        AssertEqual("  Alice", Convert.ToString(result.Table.Rows(0)(1)), "RTRIM")
        AssertEqual("[Alice]", Convert.ToString(result.Table.Rows(0)(2)), "CONCAT")
        AssertEqual("001", Convert.ToString(result.Table.Rows(0)(3)), "TO_CHAR")
        AssertEqual("00123", Convert.ToString(result.Table.Rows(0)(4)), "TO_CHAR数値書式")
        AssertEqual("2026/08/20", Convert.ToString(result.Table.Rows(0)(5)), "TO_CHAR日付書式")
        AssertEqual("00001", Convert.ToString(result.Table.Rows(0)(6)), "LPAD")
        AssertEqual("001aba", Convert.ToString(result.Table.Rows(0)(7)), "RPAD")
    End Sub

    Private Sub ExecuteSqlCaseWhen()
        Dim table As DataTable = BuildSqlFunctionTestTable()
        Dim columnCount As Integer = CsvTableBuilder.GetVisibleColumnCount(table)
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                columnCount,
                "SELECT code, " &
                "CASE " &
                "WHEN amount >= '100' THEN '大' " &
                "WHEN amount >= '050' THEN '中' " &
                "ELSE '小' END AS 区分 " &
                "FROM csv " &
                "WHERE CASE WHEN TRIM(name) = 'Alice' THEN '対象' ELSE '対象外' END = '対象';")

        AssertEqual(1, result.ReturnedRowCount, "CASE WHEN結果行数")
        AssertEqual("001", Convert.ToString(result.Table.Rows(0)(0)), "CASE WHENコード")
        AssertEqual("大", Convert.ToString(result.Table.Rows(0)(1)), "CASE WHEN分岐")
    End Sub

    Private Sub ExecuteSqlToNumber()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "amount" & ControlChars.CrLf &
                "2" & ControlChars.CrLf &
                "10" & ControlChars.CrLf &
                "100",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        Dim columnCount As Integer = CsvTableBuilder.GetVisibleColumnCount(table)
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                columnCount,
                "SELECT amount, TO_NUMBER(amount) AS 数値 " &
                "FROM csv " &
                "WHERE TO_NUMBER(amount) > 2.5 " &
                "ORDER BY TO_NUMBER(amount) DESC;")

        AssertEqual(2, result.ReturnedRowCount, "TO_NUMBER結果行数")
        AssertEqual("100", Convert.ToString(result.Table.Rows(0)(0)), "TO_NUMBER降順1件目")
        AssertEqual("10", Convert.ToString(result.Table.Rows(1)(0)), "TO_NUMBER降順2件目")

        Dim negativeDocument As CsvDocument =
            CsvParser.ParseText(
                "amount" & ControlChars.CrLf & "-1.5" & ControlChars.CrLf & "0",
                ",",
                True)
        Dim negativeTable As DataTable = CsvTableBuilder.Build(negativeDocument)
        Dim negativeResult As CsvSqlResult =
            CsvSqlEngine.Execute(
                negativeTable,
                CsvTableBuilder.GetVisibleColumnCount(negativeTable),
                "SELECT amount FROM csv WHERE TO_NUMBER(amount) < -1;")
        AssertEqual(1, negativeResult.ReturnedRowCount, "TO_NUMBER負数比較")
        AssertEqual("-1.5", Convert.ToString(negativeResult.Table.Rows(0)(0)), "TO_NUMBER負数")

        Dim formattedResult As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                columnCount,
                "SELECT TO_NUMBER('1.234,50', '9G999D99') AS 数値 " &
                "FROM csv LIMIT 1;")
        AssertEqual(
            1234.5D,
            Convert.ToDecimal(
                formattedResult.Table.Rows(0)(0),
                Globalization.CultureInfo.CurrentCulture),
            "TO_NUMBER書式指定")

        AssertThrowsCsvSql(
            Sub()
                CsvSqlEngine.Execute(
                    table,
                    columnCount,
                    "SELECT TO_NUMBER('abc') FROM csv;")
            End Sub,
            "TO_NUMBER変換失敗")
    End Sub

    Private Sub LoadApplicationIcon()
        Using icon As System.Drawing.Icon = AppIcon.Create()
            AssertTrue(icon IsNot Nothing, "埋め込みアイコンを読み込めません。")
            AssertTrue(icon.Width >= 16, "埋め込みアイコンの幅が不正です。")
            AssertTrue(icon.Height >= 16, "埋め込みアイコンの高さが不正です。")
        End Using
    End Sub

    Private Sub CycleGridColumnSort()
        Dim table As New DataTable()
        table.Columns.Add("C1", GetType(String))
        table.Rows.Add("B")
        table.Rows.Add("A")
        table.Rows.Add("C")

        Dim view As DataView = table.DefaultView
        Using grid As New SortTestGrid()
            grid.AutoGenerateColumns = True
            grid.BindingContext = New BindingContext()
            grid.DataSource = view
            AssertEqual(1, grid.Columns.Count, "ソートテスト列数")
            grid.Columns(0).SortMode = DataGridViewColumnSortMode.Programmatic

            grid.ClickColumnHeader(0)
            AssertEqual("A", Convert.ToString(view(0)("C1")), "昇順ソート")
            AssertEqual(SortOrder.Ascending,
                        grid.Columns(0).HeaderCell.SortGlyphDirection,
                        "昇順グリフ")

            grid.ClickColumnHeader(0)
            AssertEqual("C", Convert.ToString(view(0)("C1")), "降順ソート")
            AssertEqual(SortOrder.Descending,
                        grid.Columns(0).HeaderCell.SortGlyphDirection,
                        "降順グリフ")

            grid.ClickColumnHeader(0)
            AssertEqual(String.Empty, view.Sort, "ソート解除")
            AssertEqual("B", Convert.ToString(view(0)("C1")), "元の行順")
            AssertEqual(SortOrder.None,
                        grid.Columns(0).HeaderCell.SortGlyphDirection,
                        "解除グリフ")
        End Using
    End Sub

    Private NotInheritable Class SortTestGrid
        Inherits BufferedDataGridView

        Public Sub ClickColumnHeader(columnIndex As Integer)
            Dim mouseEvent As New MouseEventArgs(
                MouseButtons.Left,
                1,
                0,
                0,
                0)
            Dim cellEvent As New DataGridViewCellMouseEventArgs(
                columnIndex,
                -1,
                0,
                0,
                mouseEvent)
            MyBase.OnColumnHeaderMouseClick(cellEvent)
        End Sub
    End Class

    Private Function BuildSqlFunctionTestTable() As DataTable
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "code,name,date,amount" & ControlChars.CrLf &
                "001,  Alice  ,2026-08-20,123" & ControlChars.CrLf &
                "0123,Bob,2025-01-02,045",
                ",",
                True)
        Return CsvTableBuilder.Build(document)
    End Function

    Private Function BuildSqlTestTable() As DataTable
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "code,name,city" & ControlChars.CrLf &
                "003,C,大阪" & ControlChars.CrLf &
                "001,A,東京" & ControlChars.CrLf &
                "002,B,東京" & ControlChars.CrLf &
                "002,B,東京",
                ",",
                True)
        Return CsvTableBuilder.Build(document)
    End Function

    Private Sub AssertThrowsCsvSql(action As Action, label As String)
        Try
            action()
        Catch ex As CsvSqlException
            Return
        End Try
        Throw New InvalidOperationException(label & ": CsvSqlExceptionが発生しませんでした。")
    End Sub

    Private Function ExportToTemporaryText(document As CsvDocument,
                                           newLine As String) As String
        Dim temporaryDirectory As String =
            Path.Combine(
                Path.GetTempPath(),
                "CSViewerRegressionTests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(temporaryDirectory)

        Try
            Dim outputPath As String =
                Path.Combine(temporaryDirectory, "output.csv")
            Dim table As DataTable = CsvTableBuilder.Build(document)
            CsvExporter.Export(
                outputPath,
                table.DefaultView,
                CsvTableBuilder.GetVisibleColumnCount(table),
                document.Delimiter,
                document.HasHeader,
                CsvTextEncoding.Utf8NoBom,
                newLine)
            Return File.ReadAllText(outputPath, Encoding.UTF8)
        Finally
            Directory.Delete(temporaryDirectory, True)
        End Try
    End Function

    Private Sub AssertTrue(condition As Boolean, message As String)
        If Not condition Then Throw New InvalidOperationException(message)
    End Sub

    Private Sub AssertEqual(Of T)(expected As T,
                                  actual As T,
                                  label As String)
        If Not Object.Equals(expected, actual) Then
            Throw New InvalidOperationException(
                String.Format(
                    "{0}: 期待値=[{1}] 実際=[{2}]",
                    label,
                    expected,
                    actual))
        End If
    End Sub
End Module
