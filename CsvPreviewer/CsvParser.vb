Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text

Public NotInheritable Class CsvParser
    Private Sub New()
    End Sub

    Public Shared Function Load(filePath As String,
                                options As CsvLoadOptions) As CsvDocument
        If String.IsNullOrWhiteSpace(filePath) Then
            Throw New ArgumentException("ファイルパスが指定されていません。", "filePath")
        End If
        If options Is Nothing Then
            Throw New ArgumentNullException("options")
        End If

        Dim decoded As DecodedCsvText = CsvTextCodec.DecodeFile(filePath, options.Encoding)
        Dim delimiter As String
        If options.Delimiter = CsvDelimiterOption.AutoDetect Then
            delimiter = DetectDelimiter(decoded.Text)
        Else
            delimiter = CsvDelimiterResolver.Resolve(options.Delimiter)
        End If

        Dim document As CsvDocument = ParseText(decoded.Text, delimiter, options.HasHeader)
        Dim fileInfo As New FileInfo(filePath)
        document.FilePath = fileInfo.FullName
        document.FileSize = fileInfo.Length
        document.LastWriteTime = fileInfo.LastWriteTime
        document.LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
        document.EncodingKind = decoded.EncodingKind
        document.EncodingDisplayName = decoded.EncodingDisplayName
        document.HasBom = decoded.HasBom
        document.IsLossyDecode = decoded.UsedReplacementCharacter
        document.LineEnding = DetectRecordLineEndings(decoded.Text, delimiter)

        If decoded.UsedReplacementCharacter Then
            document.Issues.Insert(
                0,
                New CsvIssue(
                    CsvIssueSeverity.[Error],
                    0,
                    0,
                    "文字コード",
                    "指定された文字コードで解釈できないバイトがあり、代替文字で表示しました。"))
        End If

        If Not String.IsNullOrEmpty(decoded.DetectionWarning) Then
            document.Issues.Insert(
                0,
                New CsvIssue(
                    CsvIssueSeverity.Warning,
                    0,
                    0,
                    "文字コード",
                    decoded.DetectionWarning))
        End If

        Return document
    End Function

    Public Shared Function ParseText(text As String,
                                     delimiter As String,
                                     hasHeader As Boolean) As CsvDocument
        If text Is Nothing Then
            Throw New ArgumentNullException("text")
        End If
        If String.IsNullOrEmpty(delimiter) Then
            Throw New ArgumentException("区切り文字が指定されていません。", "delimiter")
        End If

        Dim document As New CsvDocument() With {
            .Delimiter = delimiter,
            .HasHeader = hasHeader,
            .LineEnding = DetectRecordLineEndings(text, delimiter)
        }

        ParseRecords(text, delimiter, Integer.MaxValue, document)

        AnalyzeColumnCounts(document)
        AnalyzeHeader(document)

        If document.Records.Count = 0 Then
            document.Issues.Add(
                New CsvIssue(
                    CsvIssueSeverity.Warning,
                    0,
                    0,
                    "空ファイル",
                    "表示できるレコードがありません。"))
        End If

        Return document
    End Function

    Public Shared Function DetectDelimiter(text As String) As String
        Dim candidates As String() = {",", ControlChars.Tab, ";", "|"}
        Dim bestDelimiter As String = ","
        Dim bestScore As Integer = Integer.MinValue

        For Each candidate As String In candidates
            Dim counts As List(Of Integer) = ReadColumnCounts(text, candidate, 30)
            Dim score As Integer = ScoreColumnCounts(counts)
            If score > bestScore Then
                bestScore = score
                bestDelimiter = candidate
            End If
        Next

        Return bestDelimiter
    End Function

    Public Shared Function DetectRecordLineEndings(text As String,
                                                   delimiter As String) As LineEndingInfo
        If text Is Nothing Then Throw New ArgumentNullException("text")
        If String.IsNullOrEmpty(delimiter) Then
            Throw New ArgumentException("区切り文字が指定されていません。", "delimiter")
        End If

        Dim crLfCount As Integer = 0
        Dim lfCount As Integer = 0
        Dim crCount As Integer = 0
        Dim index As Integer = 0
        Dim atFieldStart As Boolean = True
        Dim inQuotes As Boolean = False
        Dim afterClosingQuote As Boolean = False
        Dim malformed As Boolean = False

        While index < text.Length
            Dim newLineLength As Integer = GetNewLineLength(text, index)

            If inQuotes Then
                If text(index) = ControlChars.Quote Then
                    If index + 1 < text.Length AndAlso
                       text(index + 1) = ControlChars.Quote Then
                        index += 2
                    Else
                        inQuotes = False
                        afterClosingQuote = True
                        index += 1
                    End If
                ElseIf newLineLength > 0 Then
                    index += newLineLength
                Else
                    index += 1
                End If
                Continue While
            End If

            If malformed Then
                If newLineLength > 0 Then
                    CountLineEnding(text, index, newLineLength, crLfCount, lfCount, crCount)
                    index += newLineLength
                    atFieldStart = True
                    afterClosingQuote = False
                    malformed = False
                Else
                    index += 1
                End If
                Continue While
            End If

            If afterClosingQuote Then
                If IsDelimiterAt(text, index, delimiter) Then
                    index += delimiter.Length
                    atFieldStart = True
                    afterClosingQuote = False
                ElseIf newLineLength > 0 Then
                    CountLineEnding(text, index, newLineLength, crLfCount, lfCount, crCount)
                    index += newLineLength
                    atFieldStart = True
                    afterClosingQuote = False
                ElseIf text(index) = " "c OrElse text(index) = ControlChars.Tab Then
                    index += 1
                Else
                    malformed = True
                    afterClosingQuote = False
                    index += 1
                End If
                Continue While
            End If

            If newLineLength > 0 Then
                CountLineEnding(text, index, newLineLength, crLfCount, lfCount, crCount)
                index += newLineLength
                atFieldStart = True
                Continue While
            End If

            If IsDelimiterAt(text, index, delimiter) Then
                index += delimiter.Length
                atFieldStart = True
                Continue While
            End If

            If text(index) = ControlChars.Quote Then
                If atFieldStart Then
                    inQuotes = True
                    atFieldStart = False
                Else
                    malformed = True
                End If
                index += 1
                Continue While
            End If

            atFieldStart = False
            index += 1
        End While

        Return CreateLineEndingInfo(crLfCount, lfCount, crCount)
    End Function

    Private Shared Sub CountLineEnding(text As String,
                                       index As Integer,
                                       length As Integer,
                                       ByRef crLfCount As Integer,
                                       ByRef lfCount As Integer,
                                       ByRef crCount As Integer)
        If length = 2 Then
            crLfCount += 1
        ElseIf text(index) = ControlChars.Lf Then
            lfCount += 1
        Else
            crCount += 1
        End If
    End Sub

    Private Shared Function CreateLineEndingInfo(crLfCount As Integer,
                                                 lfCount As Integer,
                                                 crCount As Integer) As LineEndingInfo
        Dim kinds As Integer = 0
        If crLfCount > 0 Then kinds += 1
        If lfCount > 0 Then kinds += 1
        If crCount > 0 Then kinds += 1

        Dim preferred As String = Environment.NewLine
        If lfCount > crLfCount AndAlso lfCount >= crCount Then
            preferred = ControlChars.Lf
        ElseIf crCount > crLfCount AndAlso crCount > lfCount Then
            preferred = ControlChars.Cr
        ElseIf crLfCount > 0 Then
            preferred = ControlChars.CrLf
        End If

        Dim displayName As String
        If kinds = 0 Then
            displayName = "改行なし"
        ElseIf kinds > 1 Then
            displayName = String.Format(
                "混在（CRLF:{0} / LF:{1} / CR:{2}）",
                crLfCount,
                lfCount,
                crCount)
        ElseIf crLfCount > 0 Then
            displayName = "CRLF"
        ElseIf lfCount > 0 Then
            displayName = "LF"
        Else
            displayName = "CR"
        End If

        Return New LineEndingInfo(displayName, preferred, crLfCount, lfCount, crCount)
    End Function

    Private Shared Function ReadColumnCounts(text As String,
                                             delimiter As String,
                                             maximumRecords As Integer) As List(Of Integer)
        Dim counts As New List(Of Integer)()

        Dim parsed As New CsvDocument()
        ParseRecords(text, delimiter, maximumRecords, parsed)
        For Each record As CsvRecord In parsed.Records
            counts.Add(If(record.IsMalformed, 0, record.Fields.Length))
        Next

        Return counts
    End Function

    Private Shared Sub ParseRecords(text As String,
                                    delimiter As String,
                                    maximumRecords As Integer,
                                    document As CsvDocument)
        Dim fields As New List(Of String)()
        Dim field As New StringBuilder()
        Dim index As Integer = 0
        Dim recordStartIndex As Integer = 0
        Dim recordStartLine As Long = 1
        Dim currentLine As Long = 1
        Dim recordNumber As Integer = 1
        Dim atFieldStart As Boolean = True
        Dim inQuotes As Boolean = False
        Dim afterClosingQuote As Boolean = False
        Dim malformed As Boolean = False
        Dim malformedLine As Long = 0

        While index < text.Length AndAlso document.Records.Count < maximumRecords
            Dim newLineLength As Integer = GetNewLineLength(text, index)

            If inQuotes Then
                If text(index) = ControlChars.Quote Then
                    If index + 1 < text.Length AndAlso
                       text(index + 1) = ControlChars.Quote Then
                        field.Append(ControlChars.Quote)
                        index += 2
                    Else
                        inQuotes = False
                        afterClosingQuote = True
                        index += 1
                    End If
                Else
                    If newLineLength > 0 Then
                        field.Append(text, index, newLineLength)
                        currentLine += 1
                        index += newLineLength
                    Else
                        field.Append(text(index))
                        index += 1
                    End If
                End If
                Continue While
            End If

            If malformed Then
                If newLineLength > 0 Then
                    fields.Add(field.ToString())
                    AddParsedRecord(
                        document,
                        recordNumber,
                        recordStartLine,
                        fields,
                        text.Substring(recordStartIndex, index - recordStartIndex),
                        True,
                        malformedLine)
                    recordNumber += 1
                    index += newLineLength
                    currentLine += 1
                    recordStartIndex = index
                    recordStartLine = currentLine
                    fields = New List(Of String)()
                    field = New StringBuilder()
                    atFieldStart = True
                    malformed = False
                    malformedLine = 0
                Else
                    index += 1
                End If
                Continue While
            End If

            If afterClosingQuote Then
                If IsDelimiterAt(text, index, delimiter) Then
                    fields.Add(field.ToString())
                    field = New StringBuilder()
                    atFieldStart = True
                    afterClosingQuote = False
                    index += delimiter.Length
                ElseIf newLineLength > 0 Then
                    fields.Add(field.ToString())
                    AddParsedRecord(
                        document,
                        recordNumber,
                        recordStartLine,
                        fields,
                        Nothing,
                        False,
                        0)
                    recordNumber += 1
                    index += newLineLength
                    currentLine += 1
                    recordStartIndex = index
                    recordStartLine = currentLine
                    fields = New List(Of String)()
                    field = New StringBuilder()
                    atFieldStart = True
                    afterClosingQuote = False
                ElseIf text(index) = " "c OrElse text(index) = ControlChars.Tab Then
                    index += 1
                Else
                    malformed = True
                    malformedLine = currentLine
                    afterClosingQuote = False
                    index += 1
                End If
                Continue While
            End If

            If newLineLength > 0 Then
                fields.Add(field.ToString())
                AddParsedRecord(
                    document,
                    recordNumber,
                    recordStartLine,
                    fields,
                    Nothing,
                    False,
                    0)
                recordNumber += 1
                index += newLineLength
                currentLine += 1
                recordStartIndex = index
                recordStartLine = currentLine
                fields = New List(Of String)()
                field = New StringBuilder()
                atFieldStart = True
                Continue While
            End If

            If IsDelimiterAt(text, index, delimiter) Then
                fields.Add(field.ToString())
                field = New StringBuilder()
                atFieldStart = True
                index += delimiter.Length
                Continue While
            End If

            If text(index) = ControlChars.Quote Then
                If atFieldStart Then
                    inQuotes = True
                    atFieldStart = False
                    index += 1
                Else
                    malformed = True
                    malformedLine = currentLine
                    index += 1
                End If
                Continue While
            End If

            field.Append(text(index))
            atFieldStart = False
            index += 1
        End While

        If document.Records.Count >= maximumRecords Then Return
        If recordStartIndex >= text.Length Then Return

        If inQuotes Then
            malformed = True
            malformedLine = recordStartLine
        End If

        fields.Add(field.ToString())
        Dim originalText As String = Nothing
        If malformed Then originalText = text.Substring(recordStartIndex)
        AddParsedRecord(
            document,
            recordNumber,
            recordStartLine,
            fields,
            originalText,
            malformed,
            malformedLine)
    End Sub

    Private Shared Sub AddParsedRecord(document As CsvDocument,
                                       recordNumber As Integer,
                                       startLine As Long,
                                       fields As List(Of String),
                                       originalText As String,
                                       malformed As Boolean,
                                       malformedLine As Long)
        Dim record As New CsvRecord(
            recordNumber,
            startLine,
            fields.ToArray(),
            If(malformed, originalText, Nothing),
            malformed)
        document.Records.Add(record)

        If malformed Then
            document.Issues.Add(
                New CsvIssue(
                    CsvIssueSeverity.[Error],
                    If(malformedLine > 0, malformedLine, startLine),
                    recordNumber,
                    "CSV構文",
                    "ダブルクォートの対応など、CSVの形式が正しくありません。保存用に原文を保持しています。"))
        End If
    End Sub

    Private Shared Function GetNewLineLength(text As String,
                                             index As Integer) As Integer
        If text(index) = ControlChars.Cr Then
            If index + 1 < text.Length AndAlso
               text(index + 1) = ControlChars.Lf Then
                Return 2
            End If
            Return 1
        End If
        If text(index) = ControlChars.Lf Then Return 1
        Return 0
    End Function

    Private Shared Function IsDelimiterAt(text As String,
                                          index As Integer,
                                          delimiter As String) As Boolean
        If index + delimiter.Length > text.Length Then Return False
        Return String.CompareOrdinal(text, index, delimiter, 0, delimiter.Length) = 0
    End Function

    Private Shared Function ScoreColumnCounts(counts As List(Of Integer)) As Integer
        If counts.Count = 0 Then Return -100000

        Dim frequencies As New Dictionary(Of Integer, Integer)()
        For Each count As Integer In counts
            If count <= 0 Then Continue For
            If Not frequencies.ContainsKey(count) Then frequencies.Add(count, 0)
            frequencies(count) += 1
        Next

        If frequencies.Count = 0 Then Return -50000

        Dim modeColumns As Integer = 0
        Dim modeFrequency As Integer = 0
        For Each pair As KeyValuePair(Of Integer, Integer) In frequencies
            If pair.Value > modeFrequency OrElse
               (pair.Value = modeFrequency AndAlso pair.Key > modeColumns) Then
                modeColumns = pair.Key
                modeFrequency = pair.Value
            End If
        Next

        If modeColumns <= 1 Then
            Return modeFrequency
        End If

        Dim inconsistentCount As Integer = counts.Count - modeFrequency
        Return (modeFrequency * 1000) + (modeColumns * 10) - (inconsistentCount * 100)
    End Function

    Private Shared Sub AnalyzeColumnCounts(document As CsvDocument)
        If document.Records.Count = 0 Then
            document.ExpectedColumnCount = 0
            Return
        End If

        If document.HasHeader AndAlso Not document.Records(0).IsMalformed Then
            document.ExpectedColumnCount = document.Records(0).Fields.Length
        Else
            document.ExpectedColumnCount = InferExpectedColumnCount(document)
        End If

        For index As Integer = document.DataStartIndex To document.Records.Count - 1
            Dim record As CsvRecord = document.Records(index)
            If record.IsMalformed Then Continue For
            If record.Fields.Length <> document.ExpectedColumnCount Then
                record.HasIssue = True
                document.Issues.Add(
                    New CsvIssue(
                        CsvIssueSeverity.Warning,
                        record.StartLineNumber,
                        record.RecordNumber,
                        "列数不一致",
                        String.Format(
                            "期待列数は{0}列ですが、このレコードは{1}列です。",
                            document.ExpectedColumnCount,
                            record.Fields.Length)))
            End If
        Next
    End Sub

    Private Shared Function InferExpectedColumnCount(document As CsvDocument) As Integer
        Dim frequencies As New Dictionary(Of Integer, Integer)()
        For index As Integer = document.DataStartIndex To document.Records.Count - 1
            Dim record As CsvRecord = document.Records(index)
            If record.IsMalformed Then Continue For
            Dim count As Integer = record.Fields.Length
            If Not frequencies.ContainsKey(count) Then frequencies.Add(count, 0)
            frequencies(count) += 1
        Next

        Dim bestCount As Integer = 0
        Dim bestFrequency As Integer = 0
        For Each pair As KeyValuePair(Of Integer, Integer) In frequencies
            If pair.Value > bestFrequency OrElse
               (pair.Value = bestFrequency AndAlso pair.Key > bestCount) Then
                bestCount = pair.Key
                bestFrequency = pair.Value
            End If
        Next

        If bestFrequency > 0 Then Return bestCount
        If document.Records.Count > 0 Then Return document.Records(0).Fields.Length
        Return 0
    End Function

    Private Shared Sub AnalyzeHeader(document As CsvDocument)
        If Not document.HasHeader OrElse document.Records.Count = 0 Then Return

        Dim header As CsvRecord = document.Records(0)
        Dim names As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

        For index As Integer = 0 To header.Fields.Length - 1
            Dim name As String = header.Fields(index)
            If String.IsNullOrWhiteSpace(name) Then
                document.Issues.Add(
                    New CsvIssue(
                        CsvIssueSeverity.Warning,
                        header.StartLineNumber,
                        header.RecordNumber,
                        "ヘッダー",
                        String.Format("{0}列目のヘッダー名が空です。", index + 1),
                        index))
                Continue For
            End If

            If names.ContainsKey(name) Then
                document.Issues.Add(
                    New CsvIssue(
                        CsvIssueSeverity.Warning,
                        header.StartLineNumber,
                        header.RecordNumber,
                        "ヘッダー",
                        String.Format("ヘッダー名「{0}」が重複しています。", name),
                        index))
            Else
                names.Add(name, index)
            End If
        Next
    End Sub
End Class

Public NotInheritable Class CsvDelimiterResolver
    Private Sub New()
    End Sub

    Public Shared Function Resolve(optionValue As CsvDelimiterOption) As String
        Select Case optionValue
            Case CsvDelimiterOption.Tab
                Return ControlChars.Tab
            Case CsvDelimiterOption.Semicolon
                Return ";"
            Case CsvDelimiterOption.Pipe
                Return "|"
            Case Else
                Return ","
        End Select
    End Function

    Public Shared Function GetDisplayName(delimiter As String) As String
        If delimiter = ControlChars.Tab Then Return "タブ"
        If delimiter = ";" Then Return "セミコロン"
        If delimiter = "|" Then Return "パイプ"
        Return "カンマ"
    End Function
End Class
