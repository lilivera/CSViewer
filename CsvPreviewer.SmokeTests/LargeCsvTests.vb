Imports System
Imports System.Data
Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Windows.Forms
Imports CsvPreviewer

Friend Module LargeCsvTests
    Public Sub BufferBoundaries()
        Dim path As String = System.IO.Path.GetTempFileName()
        Try
            For Each length As Integer In New Integer() {4093, 4094, 4095, 4096, 65533, 65534, 65535, 65536, 131073}
                Dim value As String = New String("x"c, length)
                Dim csv As String = """" & value & """""日本語" & vbCrLf & "終端"",0001," & vbCrLf &
                                    "bad""quote,原文" & vbCrLf & vbCrLf & "last,2,"
                File.WriteAllText(path, csv, New UTF8Encoding(False))
                Dim doc As CsvDocument = CsvParser.Load(path, New CsvLoadOptions With {
                    .Encoding = CsvTextEncoding.Utf8NoBom, .Delimiter = CsvDelimiterOption.Comma, .HasHeader = False})
                Equal(4, doc.DataRowCount, "境界でレコードが欠落")
                Equal(value & """日本語" & vbCrLf & "終端", doc.Records(0).Fields(0), "長い引用フィールド")
                Equal("0001", doc.Records(0).Fields(1), "先頭ゼロ")
                Equal("", doc.Records(0).Fields(2), "末尾空項目")
                Equal("bad""quote,原文", doc.Records(1).OriginalText, "不正行原文")
                Equal(3L, doc.Records(1).StartLineNumber, "物理行")
                Equal(1, doc.Records(2).Fields.Length, "空行")
                Equal(3, doc.LineEnding.CrLfCount, "引用符内改行を除外")
                Equal("last", doc.Records(3).Fields(0), "EOFの行")
            Next
            Dim multi As CsvDocument = CsvParser.ParseText(New String("x"c, 4095) & "||b||", "||", False)
            Equal(3, multi.Records(0).Fields.Length, "複数字区切りの境界")
            Dim unclosed As String = """" & New String("x"c, 100000) & vbCrLf & "末尾"
            File.WriteAllText(path, unclosed, New UTF8Encoding(False))
            Dim broken As CsvDocument = CsvParser.Load(path, New CsvLoadOptions With {.HasHeader = False})
            Equal(unclosed, broken.Records(0).OriginalText, "長い未閉鎖引用符の原文")
            Equal(0, broken.LineEnding.CrLfCount, "未閉鎖引用符内の改行")
        Finally
            File.Delete(path)
        End Try
    End Sub

    Public Sub FileEncodings()
        Dim path As String = System.IO.Path.GetTempFileName()
        Try
            Dim csv As String = "コード,名称" & vbCrLf & "0001," & New String("あ"c, 40000) & "終端"
            For Each encoding As Encoding In New Encoding() {
                New UTF8Encoding(False), New UTF8Encoding(True), Encoding.GetEncoding(932),
                New UnicodeEncoding(False, True), New UnicodeEncoding(True, True),
                New UnicodeEncoding(False, False), New UnicodeEncoding(True, False)}
                File.WriteAllText(path, csv, encoding)
                Dim bytes As Byte() = File.ReadAllBytes(path)
                For Each requested As CsvTextEncoding In New CsvTextEncoding() {
                    CsvTextEncoding.AutoDetect, CsvTextEncoding.Utf8NoBom, CsvTextEncoding.ShiftJis,
                    CsvTextEncoding.Utf16LittleEndian, CsvTextEncoding.Utf16BigEndian}
                    Dim expected As DecodedCsvText = CsvTextCodec.DecodeBytes(bytes, requested)
                    Dim info As DecodedCsvText = Nothing
                    Using reader As StreamReader = CsvTextCodec.OpenFileReader(path, requested, info)
                        Equal(expected.Text, reader.ReadToEnd(), "ストリーム復号")
                    End Using
                    Equal(expected.EncodingKind, info.EncodingKind, "文字コード")
                    Equal(expected.HasBom, info.HasBom, "BOM")
                    Equal(expected.UsedReplacementCharacter, info.UsedReplacementCharacter, "復号エラー")
                Next
                Dim document As CsvDocument = CsvParser.Load(path, New CsvLoadOptions())
                Equal(1, document.DataRowCount, "自動判定後の行数")
                Equal("0001", document.Records(1).Fields(0), "自動判定後のデータ")
                Equal(New String("あ"c, 40000) & "終端", document.Records(1).Fields(1), "日本語の境界")
            Next
            Dim lateBytes As Byte() = Encoding.ASCII.GetBytes("a,b" & vbCrLf & New String("x"c, 70000) & ",")
            Using stream As New FileStream(path, FileMode.Create)
                stream.Write(lateBytes, 0, lateBytes.Length)
                stream.WriteByte(&H82)
                stream.WriteByte(&HA0)
            End Using
            Equal(CsvTextEncoding.ShiftJis, CsvParser.Load(path, New CsvLoadOptions()).EncodingKind,
                  "先頭64K以降のShift_JISを検出")
            Dim lossy As CsvDocument = CsvParser.Load(path, New CsvLoadOptions With {.Encoding = CsvTextEncoding.Utf8NoBom})
            Equal(True, lossy.IsLossyDecode, "末尾の復号エラー")
        Finally
            File.Delete(path)
        End Try
    End Sub

    Public Sub ConsumingTableBuild()
        For Each csv As String In New String() {"", "a,b", "a,b" & vbCrLf & "0001,2" & vbCrLf & "3" & vbCrLf & "bad""quote,raw", "bad""header" & vbCrLf & "1,2,3"}
            For Each header As Boolean In New Boolean() {True, False}
                Dim original As CsvDocument = CsvParser.ParseText(csv, ",", header)
                Dim consumed As CsvDocument = CsvParser.ParseText(csv, ",", header)
                Using expected As DataTable = CsvTableBuilder.Build(original),
                      actual As DataTable = CsvTableBuilder.Build(consumed, releaseRecordStorage:=True)
                    Equal(original.DataRowCount, consumed.DataRowCount, "解放後の行数")
                    Equal(0, consumed.Records.Count, "解放後のレコード")
                    Equal(0, consumed.Records.Capacity, "解放後の配列容量")
                    Equal(expected.Columns.Count, actual.Columns.Count, "列数")
                    Equal(expected.Rows.Count, actual.Rows.Count, "表行数")
                    For row As Integer = 0 To expected.Rows.Count - 1
                        For column As Integer = 0 To expected.Columns.Count - 1
                            Equal(expected.Rows(row)(column), actual.Rows(row)(column), "表の値と内部情報")
                        Next
                    Next
                    Equal(ExportText(original, expected), ExportText(consumed, actual), "解放後の保存内容")
                End Using
            Next
        Next
    End Sub

    Public Sub LargeTableOperations()
        Dim path As String = System.IO.Path.GetTempFileName()
        Try
            Using writer As New StreamWriter(path, False, New UTF8Encoding(False))
                writer.WriteLine("code,value")
                For index As Integer = 1 To 100000
                    writer.WriteLine(index.ToString("D7") & ",日本語")
                Next
            End Using
            Dim doc As CsvDocument = CsvParser.Load(path, New CsvLoadOptions())
            Using table As DataTable = CsvTableBuilder.Build(doc, True)
                Equal(100000, table.Rows.Count, "大容量行数")
                Equal("0100000", CStr(table.Rows(99999)(0)), "最終行")
                Using view As New DataView(table)
                    view.RowFilter = "C1 = '0100000'"
                    Equal(1, view.Count, "末尾行の検索")
                    view.RowFilter = ""
                    view.Sort = "C1 DESC"
                    Equal("0100000", CStr(view(0)(0)), "ソート")
                End Using
                Dim result As CsvSqlResult = CsvSqlEngine.Execute(table, 2, "SELECT COUNT(*) FROM csv")
                Equal(100000, Convert.ToInt32(result.Table.Rows(0)(0)), "SQL件数")
            End Using
        Finally
            File.Delete(path)
        End Try
    End Sub

    Public Sub IssueNavigationKeepsRowsShared()
        Dim doc As CsvDocument = CsvParser.ParseText("a,b" & vbCrLf & String.Concat(System.Linq.Enumerable.Repeat("1,2" & vbCrLf, 10000)), ",", True)
        Using table As DataTable = CsvTableBuilder.Build(doc, True), form As New MainForm()
            Dim flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
            GetType(MainForm).GetField("_table", flags).SetValue(form, table)
            GetType(MainForm).GetField("_view", flags).SetValue(form, table.DefaultView)
            Dim grid As DataGridView = DirectCast(GetType(MainForm).GetField("_grid", flags).GetValue(form), DataGridView)
            grid.BindingContext = New BindingContext()
            grid.DataSource = table.DefaultView
            Dim selected As Object = GetType(MainForm).GetMethod("TrySelectRecord", flags).Invoke(form, New Object() {10001})
            Equal(True, CBool(selected), "末尾Issueへの移動")
            Equal(9999, grid.CurrentCell.RowIndex, "移動先")
            Dim unshared As Integer = 0
            For index As Integer = 0 To grid.Rows.Count - 1
                If grid.Rows.SharedRow(index).Index >= 0 Then unshared += 1
            Next
            If unshared > 10 Then Throw New Exception("Issue移動で不要な行が実体化されました: " & unshared)
        End Using
    End Sub

    Private Function ExportText(doc As CsvDocument, table As DataTable) As String
        Dim path As String = System.IO.Path.GetTempFileName()
        Try
            CsvExporter.Export(path, table.DefaultView, CsvTableBuilder.GetVisibleColumnCount(table),
                               doc.Delimiter, doc.HasHeader, CsvTextEncoding.Utf8NoBom, vbCrLf)
            Return File.ReadAllText(path, Encoding.UTF8)
        Finally
            File.Delete(path)
        End Try
    End Function

    Private Sub Equal(Of T)(expected As T, actual As T, label As String)
        If Not Object.Equals(expected, actual) Then Throw New Exception(label)
    End Sub
End Module
