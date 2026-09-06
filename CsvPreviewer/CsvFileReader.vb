Imports System
Imports System.IO
Imports System.Text

' Flush the decoder at EOF as well as preserving partial characters between reads.
' .NET Framework StreamReader can otherwise omit an incomplete trailing character.
Public NotInheritable Class CsvFileReader
    Inherits TextReader

    Private ReadOnly _stream As FileStream
    Private ReadOnly _decoder As Decoder
    Private ReadOnly _bytes(65535) As Byte
    Private ReadOnly _characters As Char()
    Private ReadOnly _preambleLength As Integer
    Private _offset As Integer
    Private _count As Integer
    Private _atEnd As Boolean
    Private _disposed As Boolean

    Friend Sub New(stream As FileStream, encoding As Encoding, preambleLength As Integer)
        _stream = stream
        _decoder = encoding.GetDecoder()
        _characters = New Char(encoding.GetMaxCharCount(_bytes.Length) - 1) {}
        _preambleLength = preambleLength
        Restart()
    End Sub

    Friend Sub Restart()
        If _disposed Then Throw New ObjectDisposedException("CsvFileReader")
        _stream.Position = Math.Min(CLng(_preambleLength), _stream.Length)
        _decoder.Reset()
        _offset = 0
        _count = 0
        _atEnd = False
    End Sub

    Private Function EnsureCharacters() As Boolean
        If _disposed Then Throw New ObjectDisposedException("CsvFileReader")
        While _offset >= _count
            If _atEnd Then Return False
            Dim read As Integer = _stream.Read(_bytes, 0, _bytes.Length)
            _atEnd = read = 0
            _offset = 0
            _count = _decoder.GetChars(_bytes, 0, read, _characters, 0, _atEnd)
        End While
        Return True
    End Function

    Public Overrides Function Peek() As Integer
        If Not EnsureCharacters() Then Return -1
        Return AscW(_characters(_offset)) And &HFFFF
    End Function

    Public Overrides Function Read() As Integer
        Dim value As Integer = Peek()
        If value >= 0 Then _offset += 1
        Return value
    End Function

    Public Overrides Function Read(buffer As Char(), index As Integer, count As Integer) As Integer
        If buffer Is Nothing Then Throw New ArgumentNullException("buffer")
        If index < 0 OrElse count < 0 OrElse index > buffer.Length - count Then
            Throw New ArgumentOutOfRangeException()
        End If
        If count = 0 Then Return 0
        If Not EnsureCharacters() Then Return 0
        Dim length As Integer = Math.Min(count, _count - _offset)
        Array.Copy(_characters, _offset, buffer, index, length)
        _offset += length
        Return length
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso Not _disposed Then
            _disposed = True
            _stream.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
