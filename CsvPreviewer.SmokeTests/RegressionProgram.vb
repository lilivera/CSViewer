Imports System
Imports System.Data
Imports System.IO
Imports System.Text
Imports System.Threading
Imports CsvPreviewer

Friend Module RegressionProgram
    Private _passed As Integer

    <STAThread>
    Public Sub Main()
        Try
            RunTest("引用符・改行・末尾空項目", AddressOf ParseQuotedFields)
            RunTest("列数不一致の検出", AddressOf DetectColumnMismatch)
            RunTest("非引用フィールド中の裸引用符", AddressOf RejectBareQuote)
            RunTest("malformed先頭行を列数基準にしない", AddressOf IgnoreMalformedForExpectedColumns)
            RunTest("区切り文字の自動判定", AddressOf DetectTabDelimiter)
            RunTest("UTF-8とShift_JISの判定", AddressOf DetectTextEncoding)
            RunTest("正常BOMなしUTF-8を優先", AddressOf PreferValidUtf8)
            RunTest("BOMなしUTF-16の判定", AddressOf DetectBomlessUtf16)
            RunTest("日本語主体BOMなしUTF-16", AddressOf DetectJapaneseBomlessUtf16)
            RunTest("引用フィールド内改行をレコード改行に数えない", AddressOf DetectRecordLineEndingOnly)
            RunTest("DataTableで先頭ゼロを保持", AddressOf PreserveLeadingZeros)
            RunTest("CSVエスケープ", AddressOf EscapeCsvFields)
            RunTest("BOM付きUTF-8で保存", AddressOf ExportUtf8WithBom)
            RunTest("不揃い列数を保持して保存", AddressOf PreserveOriginalShapeOnExport)
            RunTest("不正レコードの原文保持", AddressOf PreserveMalformedRecordOnExport)
            RunTest("空レコードの保持", AddressOf PreserveBlankRecordOnExport)
            RunTest("SQLの抽出・並べ替え・LIMIT", AddressOf ExecuteSqlSelect)
            RunTest("SQL COUNT", AddressOf ExecuteSqlCount)
            RunTest("SQLのCn予約名を優先", AddressOf ResolveOrdinalColumnBeforeHeader)
            RunTest("SQL DISTINCTの大小文字比較", AddressOf DistinctUsesSqlComparisonRule)
            RunTest("SQL TO_NUMBER", AddressOf ExecuteSqlToNumber)
            RunTest("SQLキャンセル", AddressOf CancelSqlExecution)
            RunTest("lossy復号状態をDocumentへ保持", AddressOf PreserveLossyDecodeState)
            RunTest("ヘッダーIssueに対象列を保持", AddressOf HeaderIssueContainsColumnIndex)

            Console.WriteLine()
            Console.WriteLine("全{0}件の回帰テストに成功しました。", _passed)
            Environment.ExitCode = 0
        Catch ex As Exception
            Console.Error.WriteLine()
            Console.Error.WriteLine("回帰テスト失敗: " & ex.Message)
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
        AssertEqual(
            "1行目" & ControlChars.CrLf & "2行目",
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

    Private Sub RejectBareQuote()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "a,b" & ControlChars.CrLf & "1,abc""def",
                ",",
                True)

        AssertTrue(document.Records(1).IsMalformed,
                   "非引用フィールド中の引用符がmalformedになっていません。")
        AssertTrue(
            document.Issues.Exists(
                Function(issue As CsvIssue) issue.Category = "CSV構文"),
            "CSV構文Issueが追加されていません。")
        AssertEqual("1,abc""def", document.Records(1).OriginalText, "不正行原文")
    End Sub

    Private Sub IgnoreMalformedForExpectedColumns()
        Dim csv As String =
            "1,""bad""x" & ControlChars.CrLf &
            "2,3,4" & ControlChars.CrLf &
            "5,6,7"
        Dim document As CsvDocument = CsvParser.ParseText(csv, ",", False)

        AssertEqual(3, document.ExpectedColumnCount, "期待列数")
        AssertTrue(Not document.Records(1).HasIssue,
                   "正常な2行目が列数不一致扱いです。")
        AssertTrue(Not document.Records(2).HasIssue,
                   "正常な3行目が列数不一致扱いです。")
    End Sub

    Private Sub DetectTabDelimiter()
        Dim tsv As String =
            "code" & ControlChars.Tab & "name" & ControlChars.Tab & "note" & ControlChars.CrLf &
            "001" & ControlChars.Tab & "東京" & ControlChars.Tab & "確認"
        AssertEqual(ControlChars.Tab.ToString(), CsvParser.DetectDelimiter(tsv), "タブ区切り")
    End Sub

    Private Sub DetectTextEncoding()
        Dim original As String = "コード,名称" & ControlChars.CrLf & "0000001,東京"

        Dim utf8Bytes As Byte() = New UTF8Encoding(False).GetBytes(original)
        Dim utf8 As DecodedCsvText = CsvTextCodec.DecodeBytes(utf8Bytes, CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf8NoBom, utf8.EncodingKind, "UTF-8判定")
        AssertEqual(original, utf8.Text, "UTF-8復号")

        Dim shiftJisBytes As Byte() = Encoding.GetEncoding(932).GetBytes(original)
        Dim shiftJis As DecodedCsvText = CsvTextCodec.DecodeBytes(shiftJisBytes, CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.ShiftJis, shiftJis.EncodingKind, "Shift_JIS判定")
        AssertEqual(original, shiftJis.Text, "Shift_JIS復号")
    End Sub

    Private Sub PreferValidUtf8()
        Dim bytes As Byte() = New UTF8Encoding(False).GetBytes(
            "symbol" & ControlChars.CrLf & "©" & ControlChars.CrLf & "20°C")
        Dim decoded As DecodedCsvText = CsvTextCodec.DecodeBytes(bytes, CsvTextEncoding.AutoDetect)

        AssertEqual(CsvTextEncoding.Utf8NoBom, decoded.EncodingKind, "UTF-8判定")
        AssertTrue(decoded.Text.Contains("©"), "©がUTF-8として保持されていません。")
        AssertTrue(decoded.Text.Contains("20°C"), "°がUTF-8として保持されていません。")
    End Sub

    Private Sub DetectBomlessUtf16()
        Dim original As String = "a,b" & ControlChars.CrLf & "1,2"

        Dim little As DecodedCsvText =
            CsvTextCodec.DecodeBytes(Encoding.Unicode.GetBytes(original), CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf16LittleEndian, little.EncodingKind, "UTF-16 LE判定")
        AssertEqual(original, little.Text, "UTF-16 LE復号")

        Dim big As DecodedCsvText =
            CsvTextCodec.DecodeBytes(Encoding.BigEndianUnicode.GetBytes(original), CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf16BigEndian, big.EncodingKind, "UTF-16 BE判定")
        AssertEqual(original, big.Text, "UTF-16 BE復号")
    End Sub

    Private Sub DetectJapaneseBomlessUtf16()
        Dim original As String =
            "漢字漢字,漢字漢字" & ControlChars.CrLf &
            "漢字漢字,漢字漢字"

        Dim little As DecodedCsvText =
            CsvTextCodec.DecodeBytes(Encoding.Unicode.GetBytes(original), CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf16LittleEndian, little.EncodingKind, "日本語UTF-16 LE判定")
        AssertEqual(original, little.Text, "日本語UTF-16 LE復号")

        Dim big As DecodedCsvText =
            CsvTextCodec.DecodeBytes(Encoding.BigEndianUnicode.GetBytes(original), CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf16BigEndian, big.EncodingKind, "日本語UTF-16 BE判定")
        AssertEqual(original, big.Text, "日本語UTF-16 BE復号")
    End Sub

    Private Sub DetectRecordLineEndingOnly()
        Dim csv As String =
            "a,b" & ControlChars.CrLf &
            "1,""x" & ControlChars.Lf & "y" & ControlChars.Lf & "z""" & ControlChars.CrLf &
            "2,end"
        Dim info As LineEndingInfo = CsvParser.DetectRecordLineEndings(csv, ",")

        AssertEqual(ControlChars.CrLf, info.PreferredNewLine, "優先レコード改行")
        AssertEqual(2, info.CrLfCount, "CRLFレコード数")
        AssertEqual(0, info.LfCount, "フィールド内LFがレコード改行に混入")
    End Sub

    Private Sub PreserveLeadingZeros()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "code,name" & ControlChars.CrLf & "0000123,テスト",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        AssertEqual(GetType(String), table.Columns(0).DataType, "列型")
        AssertEqual("0000123", Convert.ToString(table.Rows(0)(0)), "先頭ゼロ")
    End Sub

    Private Sub EscapeCsvFields()
        AssertEqual("plain", CsvExporter.EscapeField("plain", ","), "通常項目")
        AssertEqual("""A,B""", CsvExporter.EscapeField("A,B", ","), "カンマ")
        AssertEqual("""A""""B""", CsvExporter.EscapeField("A""B", ","), "引用符")
        AssertEqual(""" 001 """, CsvExporter.EscapeField(" 001 ", ","), "前後空白")
    End Sub

    Private Sub ExportUtf8WithBom()
        Dim directory As String = CreateTemporaryDirectory("CSViewerBomTests_")
        Try
            Dim outputPath As String = Path.Combine(directory, "output.csv")
            Dim document As CsvDocument =
                CsvParser.ParseText(
                    "code,name" & ControlChars.CrLf & "0000001,""東京,本店""",
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
            AssertTrue(bytes.Length >= 3, "BOM出力が短すぎます。")
            AssertEqual(CByte(&HEF), bytes(0), "UTF-8 BOM 1")
            AssertEqual(CByte(&HBB), bytes(1), "UTF-8 BOM 2")
            AssertEqual(CByte(&HBF), bytes(2), "UTF-8 BOM 3")
        Finally
            Directory.Delete(directory, True)
        End Try
    End Sub

    Private Sub PreserveOriginalShapeOnExport()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                ",name,note" & ControlChars.CrLf &
                "0000001,東京" & ControlChars.CrLf &
                "0000002,大阪,確認,余分",
                ",",
                True)
        Dim output As String = ExportToTemporaryText(document, ControlChars.CrLf)
        Dim expected As String =
            ",name,note" & ControlChars.CrLf &
            "0000001,東京" & ControlChars.CrLf &
            "0000002,大阪,確認,余分" & ControlChars.CrLf
        AssertEqual(expected, output, "不揃い列数保存")
    End Sub

    Private Sub PreserveMalformedRecordOnExport()
        Dim csv As String =
            "a,b" & ControlChars.CrLf &
            "1,""bad""x" & ControlChars.CrLf &
            "2,ok"
        Dim document As CsvDocument = CsvParser.ParseText(csv, ",", True)
        AssertTrue(document.Records(1).IsMalformed, "不正行フラグ")
        AssertEqual("1,""bad""x", document.Records(1).OriginalText, "不正行原文")

        Dim output As String = ExportToTemporaryText(document, ControlChars.CrLf)
        AssertEqual(csv & ControlChars.CrLf, output, "不正行保存")
    End Sub

    Private Sub PreserveBlankRecordOnExport()
        Dim csv As String =
            "a,b" & ControlChars.CrLf & ControlChars.CrLf & "1,2"
        Dim document As CsvDocument = CsvParser.ParseText(csv, ",", True)
        AssertEqual(3, document.Records.Count, "空行含むレコード数")
        AssertEqual(String.Empty, document.Records(1).Fields(0), "空行値")

        Dim output As String = ExportToTemporaryText(document, ControlChars.CrLf)
        AssertEqual(csv & ControlChars.CrLf, output, "空行保存")
    End Sub

    Private Sub ExecuteSqlSelect()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "code,name" & ControlChars.CrLf &
                "003,C" & ControlChars.CrLf &
                "001,A" & ControlChars.CrLf &
                "002,B",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                CsvTableBuilder.GetVisibleColumnCount(table),
                "SELECT C1, C2 FROM csv WHERE C1 >= '002' ORDER BY C1 DESC LIMIT 1;")

        AssertEqual(2, result.MatchedRowCount, "SQL一致件数")
        AssertEqual(1, result.ReturnedRowCount, "SQL結果件数")
        AssertEqual("003", Convert.ToString(result.Table.Rows(0)(0)), "SQL並び順")
    End Sub

    Private Sub ExecuteSqlCount()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "value" & ControlChars.CrLf & "a" & ControlChars.CrLf & "b" & ControlChars.CrLf & "c",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                CsvTableBuilder.GetVisibleColumnCount(table),
                "SELECT COUNT(*) AS 件数 FROM csv WHERE C1 <> 'b';")

        AssertEqual(2, result.MatchedRowCount, "COUNT一致件数")
        AssertEqual(CLng(2), Convert.ToInt64(result.Table.Rows(0)(0)), "COUNT結果")
    End Sub

    Private Sub ResolveOrdinalColumnBeforeHeader()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "C2,name" & ControlChars.CrLf & "left,right",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                CsvTableBuilder.GetVisibleColumnCount(table),
                "SELECT C2 FROM csv;")

        AssertEqual(1, result.ReturnedRowCount, "SQL結果行数")
        AssertEqual("right", Convert.ToString(result.Table.Rows(0)(0)), "C2位置参照")
    End Sub

    Private Sub DistinctUsesSqlComparisonRule()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "value" & ControlChars.CrLf & "abc" & ControlChars.CrLf & "ABC",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                CsvTableBuilder.GetVisibleColumnCount(table),
                "SELECT DISTINCT C1 FROM csv;")

        AssertEqual(1, result.ReturnedRowCount, "DISTINCT大小文字")
    End Sub

    Private Sub ExecuteSqlToNumber()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "amount" & ControlChars.CrLf & "2" & ControlChars.CrLf & "10" & ControlChars.CrLf & "100",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        Dim result As CsvSqlResult =
            CsvSqlEngine.Execute(
                table,
                CsvTableBuilder.GetVisibleColumnCount(table),
                "SELECT amount FROM csv WHERE TO_NUMBER(amount) >= 10 ORDER BY TO_NUMBER(amount) DESC;")

        AssertEqual(2, result.ReturnedRowCount, "TO_NUMBER行数")
        AssertEqual("100", Convert.ToString(result.Table.Rows(0)(0)), "TO_NUMBER降順1")
        AssertEqual("10", Convert.ToString(result.Table.Rows(1)(0)), "TO_NUMBER降順2")
    End Sub

    Private Sub CancelSqlExecution()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "value" & ControlChars.CrLf & "1" & ControlChars.CrLf & "2",
                ",",
                True)
        Dim table As DataTable = CsvTableBuilder.Build(document)
        Dim cancellation As New CancellationTokenSource()
        cancellation.Cancel()

        Dim cancelled As Boolean = False
        Try
            CsvSqlEngine.Execute(
                table,
                CsvTableBuilder.GetVisibleColumnCount(table),
                "SELECT * FROM csv;",
                cancellation.Token)
        Catch ex As OperationCanceledException
            cancelled = True
        Finally
            cancellation.Dispose()
        End Try

        AssertTrue(cancelled, "キャンセル済みTokenでSQLが停止しません。")
    End Sub

    Private Sub PreserveLossyDecodeState()
        Dim directory As String = CreateTemporaryDirectory("CSViewerLossyTests_")
        Try
            Dim path As String = Path.Combine(directory, "shiftjis.csv")
            File.WriteAllBytes(
                path,
                Encoding.GetEncoding(932).GetBytes(
                    "code,name" & ControlChars.CrLf & "1,東京"))

            Dim options As New CsvLoadOptions() With {
                .Encoding = CsvTextEncoding.Utf8NoBom,
                .Delimiter = CsvDelimiterOption.Comma,
                .HasHeader = True
            }
            Dim document As CsvDocument = CsvParser.Load(path, options)
            AssertTrue(document.IsLossyDecode,
                       "復号エラー時にIsLossyDecodeが立っていません。")
            AssertTrue(
                document.Issues.Exists(
                    Function(issue As CsvIssue)
                        Return issue.Category = "文字コード" AndAlso
                               issue.Severity = CsvIssueSeverity.[Error]
                    End Function),
                "復号エラーIssueがありません。")
        Finally
            Directory.Delete(directory, True)
        End Try
    End Sub

    Private Sub HeaderIssueContainsColumnIndex()
        Dim document As CsvDocument =
            CsvParser.ParseText(
                "name,name," & ControlChars.CrLf & "1,2,3",
                ",",
                True)

        Dim duplicateIssue As CsvIssue =
            document.Issues.Find(
                Function(issue As CsvIssue)
                    Return issue.Category = "ヘッダー" AndAlso issue.Message.Contains("重複")
                End Function)
        Dim emptyIssue As CsvIssue =
            document.Issues.Find(
                Function(issue As CsvIssue)
                    Return issue.Category = "ヘッダー" AndAlso issue.Message.Contains("空")
                End Function)

        AssertTrue(duplicateIssue IsNot Nothing, "重複ヘッダーIssueなし")
        AssertTrue(emptyIssue IsNot Nothing, "空ヘッダーIssueなし")
        AssertEqual(1, duplicateIssue.ColumnIndex, "重複ヘッダー列")
        AssertEqual(2, emptyIssue.ColumnIndex, "空ヘッダー列")
    End Sub

    Private Function ExportToTemporaryText(document As CsvDocument,
                                           newLine As String) As String
        Dim directory As String = CreateTemporaryDirectory("CSViewerExportTests_")
        Try
            Dim path As String = Path.Combine(directory, "output.csv")
            Dim table As DataTable = CsvTableBuilder.Build(document)
            CsvExporter.Export(
                path,
                table.DefaultView,
                CsvTableBuilder.GetVisibleColumnCount(table),
                document.Delimiter,
                document.HasHeader,
                CsvTextEncoding.Utf8NoBom,
                newLine)
            Return File.ReadAllText(path, New UTF8Encoding(False))
        Finally
            Directory.Delete(directory, True)
        End Try
    End Function

    Private Function CreateTemporaryDirectory(prefix As String) As String
        Dim directory As String =
            Path.Combine(Path.GetTempPath(), prefix & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory)
        Return directory
    End Function

    Private Sub AssertTrue(condition As Boolean, message As String)
        If Not condition Then Throw New Exception(message)
    End Sub

    Private Sub AssertEqual(Of T)(expected As T, actual As T, label As String)
        If Not Object.Equals(expected, actual) Then
            Throw New Exception(
                String.Format(
                    "{0}: expected={1}, actual={2}",
                    label,
                    Convert.ToString(expected),
                    Convert.ToString(actual)))
        End If
    End Sub
End Module
