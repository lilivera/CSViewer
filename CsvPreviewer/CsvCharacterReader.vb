Imports System
Imports System.IO
Imports System.Text

' Bounded lookahead; only the current record is retained for malformed CSV recovery.
Friend NotInheritable Class CsvCharacterReader
    Private ReadOnly _reader As TextReader
    Private ReadOnly _buffer As Char()
    Private _offset As Integer
    Private _count As Integer
    Private _original As New StringBuilder()

    Public Sub New(reader As TextReader, delimiterLength As Integer)
        _reader = reader
        _buffer = New Char(Math.Max(4096, delimiterLength) - 1) {}
    End Sub

    Public Function Peek(Optional lookahead As Integer = 0) As Integer
        While _offset + lookahead >= _count
            Dim remaining As Integer = _count - _offset
            Array.Copy(_buffer, _offset, _buffer, 0, remaining)
            _offset = 0
            _count = remaining
            Dim read As Integer = _reader.Read(_buffer, _count, _buffer.Length - _count)
            If read = 0 Then Return -1
            _count += read
        End While
        Return AscW(_buffer(_offset + lookahead)) And &HFFFF
    End Function

    Public Function IsDelimiter(delimiter As String) As Boolean
        For index As Integer = 0 To delimiter.Length - 1
            If Peek(index) <> (AscW(delimiter(index)) And &HFFFF) Then Return False
        Next
        Return True
    End Function

    Public Function NewLineLength() As Integer
        If Peek() = 13 Then Return If(Peek(1) = 10, 2, 1)
        If Peek() = 10 Then Return 1
        Return 0
    End Function

    Public Sub Advance(count As Integer)
        For index As Integer = 1 To count
            Dim value As Integer = Peek()
            If value < 0 Then Throw New EndOfStreamException()
            _original.Append(ChrW(value))
            _offset += 1
        Next
    End Sub

    Public ReadOnly Property HasRecordText As Boolean
        Get
            Return _original.Length > 0
        End Get
    End Property

    Public Function OriginalText() As String
        Return _original.ToString()
    End Function

    Public Sub StartRecord()
        If _original.Capacity > 65536 Then
            _original = New StringBuilder()
        Else
            _original.Clear()
        End If
    End Sub
End Class
