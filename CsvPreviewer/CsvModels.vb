Imports System
Imports System.Collections.Generic

Public Enum CsvTextEncoding
    AutoDetect = 0
    Utf8NoBom = 1
    Utf8Bom = 2
    ShiftJis = 3
    Utf16LittleEndian = 4
    Utf16BigEndian = 5
End Enum

Public Enum CsvDelimiterOption
    AutoDetect = 0
    Comma = 1
    Tab = 2
    Semicolon = 3
    Pipe = 4
End Enum

Public Enum CsvIssueSeverity
    Information = 0
    Warning = 1
    [Error] = 2
End Enum

Public NotInheritable Class OptionItem(Of T)
    Public Sub New(displayText As String, value As T)
        Me.DisplayText = displayText
        Me.Value = value
    End Sub

    Public ReadOnly Property DisplayText As String
    Public ReadOnly Property Value As T

    Public Overrides Function ToString() As String
        Return DisplayText
    End Function
End Class

Public NotInheritable Class CsvLoadOptions
    Public Sub New()
        Encoding = CsvTextEncoding.AutoDetect
        Delimiter = CsvDelimiterOption.AutoDetect
        HasHeader = True
    End Sub

    Public Property Encoding As CsvTextEncoding
    Public Property Delimiter As CsvDelimiterOption
    Public Property HasHeader As Boolean
End Class

Public NotInheritable Class CsvRecord
    Public Sub New(recordNumber As Integer,
                   startLineNumber As Long,
                   fields As String(),
                   Optional originalText As String = Nothing,
                   Optional isMalformed As Boolean = False)
        Me.RecordNumber = recordNumber
        Me.StartLineNumber = startLineNumber
        Me.Fields = fields
        Me.OriginalText = originalText
        Me.IsMalformed = isMalformed
        Me.HasIssue = isMalformed
    End Sub

    Public ReadOnly Property RecordNumber As Integer
    Public ReadOnly Property StartLineNumber As Long
    Public ReadOnly Property Fields As String()
    Public ReadOnly Property OriginalText As String
    Public ReadOnly Property IsMalformed As Boolean
    Public Property HasIssue As Boolean
End Class

Public NotInheritable Class CsvIssue
    Public Sub New(severity As CsvIssueSeverity,
                   lineNumber As Long,
                   recordNumber As Integer,
                   category As String,
                   message As String)
        Me.Severity = severity
        Me.LineNumber = lineNumber
        Me.RecordNumber = recordNumber
        Me.Category = category
        Me.Message = message
    End Sub

    Public ReadOnly Property Severity As CsvIssueSeverity
    Public ReadOnly Property LineNumber As Long
    Public ReadOnly Property RecordNumber As Integer
    Public ReadOnly Property Category As String
    Public ReadOnly Property Message As String
End Class

Public NotInheritable Class LineEndingInfo
    Public Sub New(displayName As String,
                   preferredNewLine As String,
                   crLfCount As Integer,
                   lfCount As Integer,
                   crCount As Integer)
        Me.DisplayName = displayName
        Me.PreferredNewLine = preferredNewLine
        Me.CrLfCount = crLfCount
        Me.LfCount = lfCount
        Me.CrCount = crCount
    End Sub

    Public ReadOnly Property DisplayName As String
    Public ReadOnly Property PreferredNewLine As String
    Public ReadOnly Property CrLfCount As Integer
    Public ReadOnly Property LfCount As Integer
    Public ReadOnly Property CrCount As Integer
End Class

Public NotInheritable Class DecodedCsvText
    Public Property Text As String
    Public Property EncodingKind As CsvTextEncoding
    Public Property EncodingDisplayName As String
    Public Property HasBom As Boolean
    Public Property UsedReplacementCharacter As Boolean
    Public Property DetectionWarning As String
End Class

Public NotInheritable Class CsvDocument
    Private _releasedDataRowCount As Integer = -1

    Public Sub New()
        Records = New List(Of CsvRecord)()
        Issues = New List(Of CsvIssue)()
        LineEnding = New LineEndingInfo("改行なし", Environment.NewLine, 0, 0, 0)
    End Sub

    Public Property FilePath As String
    Public Property FileSize As Long
    Public Property LastWriteTime As DateTime
    Public Property EncodingKind As CsvTextEncoding
    Public Property EncodingDisplayName As String
    Public Property HasBom As Boolean
    Public Property Delimiter As String
    Public Property HasHeader As Boolean
    Public Property ExpectedColumnCount As Integer
    Public Property LineEnding As LineEndingInfo
    Public ReadOnly Property Records As List(Of CsvRecord)
    Public ReadOnly Property Issues As List(Of CsvIssue)

    Public ReadOnly Property DataStartIndex As Integer
        Get
            If HasHeader AndAlso Records.Count > 0 Then
                Return 1
            End If
            Return 0
        End Get
    End Property

    Public ReadOnly Property DataRowCount As Integer
        Get
            If _releasedDataRowCount >= 0 Then
                Return _releasedDataRowCount
            End If
            Return Math.Max(0, Records.Count - DataStartIndex)
        End Get
    End Property

    Friend Sub ReleaseRecordStorage()
        If _releasedDataRowCount >= 0 Then Return
        _releasedDataRowCount = DataRowCount
        Records.Clear()
    End Sub
End Class
