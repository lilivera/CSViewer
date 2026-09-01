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
        Program.Main()
        If Environment.ExitCode <> 0 Then Return

        Try
            RunTest("非引用フィールド中の裸引用符", AddressOf RejectBareQuote)
            RunTest("malformed先頭行を列数基準にしない", AddressOf IgnoreMalformedForExpectedColumns)
            RunTest("引用フィールド内改行をレコード改行に数えない", AddressOf DetectRecordLineEndingOnly)
            RunTest("正常BOMなしUTF-8を優先", AddressOf PreferValidUtf8)
            RunTest("日本語主体BOMなしUTF-16", AddressOf DetectJapaneseBomlessUtf16)
            RunTest("SQLのCn予約名を優先", AddressOf ResolveOrdinalColumnBeforeHeader)
            RunTest("SQL DISTINCTの大小文字比較", AddressOf DistinctUsesSqlComparisonRule)
            RunTest("SQLキャンセル", AddressOf CancelSqlExecution)
            RunTest("lossy復号状態をDocumentへ保持", AddressOf PreserveLossyDecodeState)

            Console.WriteLine()
            Console.WriteLine("追加回帰テスト 全{0}件に成功しました。", _passed)
            Environment.ExitCode = 0
        Catch ex As Exception
            Console.Error.WriteLine()
            Console.Error.WriteLine("追加回帰テスト失敗: " & ex.Message)
            Console.Error.WriteLine(ex.ToString())
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Sub RunTest(name As String, testAction As Action)
        testAction()
        _passed += 1
        Console.WriteLine("[OK] " & name)
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

    Private Sub PreferValidUtf8()
        Dim bytes As Byte() = New UTF8Encoding(False).GetBytes("symbol" & ControlChars.CrLf & "©")
        Dim decoded As DecodedCsvText =
            CsvTextCodec.DecodeBytes(bytes, CsvTextEncoding.AutoDetect)

        AssertEqual(CsvTextEncoding.Utf8NoBom, decoded.EncodingKind, "UTF-8判定")
        AssertTrue(decoded.Text.Contains("©"), "©がUTF-8として保持されていません。")
    End Sub

    Private Sub DetectJapaneseBomlessUtf16()
        Dim original As String =
            "漢字漢字,漢字漢字" & ControlChars.CrLf &
            "漢字漢字,漢字漢字"

        Dim little As DecodedCsvText =
            CsvTextCodec.DecodeBytes(
                Encoding.Unicode.GetBytes(original),
                CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf16LittleEndian,
                    little.EncodingKind,
                    "日本語UTF-16 LE判定")
        AssertEqual(original, little.Text, "日本語UTF-16 LE復号")

        Dim big As DecodedCsvText =
            CsvTextCodec.DecodeBytes(
                Encoding.BigEndianUnicode.GetBytes(original),
                CsvTextEncoding.AutoDetect)
        AssertEqual(CsvTextEncoding.Utf16BigEndian,
                    big.EncodingKind,
                    "日本語UTF-16 BE判定")
        AssertEqual(original, big.Text, "日本語UTF-16 BE復号")
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
        Dim temporaryDirectory As String =
            Path.Combine(Path.GetTempPath(), "CSViewerLossyTests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(temporaryDirectory)

        Try
            Dim path As String = Path.Combine(temporaryDirectory, "shiftjis.csv")
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
            If Directory.Exists(temporaryDirectory) Then
                Directory.Delete(temporaryDirectory, True)
            End If
        End Try
    End Sub

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
