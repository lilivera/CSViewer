Imports System
Imports System.Data
Imports System.IO
Imports System.Text

Public NotInheritable Class CsvExporter
    Private Sub New()
    End Sub

    Public Shared Sub Export(filePath As String,
                             view As DataView,
                             visibleColumnCount As Integer,
                             delimiter As String,
                             includeHeader As Boolean,
                             encodingKind As CsvTextEncoding,
                             newLine As String)
        If String.IsNullOrWhiteSpace(filePath) Then
            Throw New ArgumentException("保存先が指定されていません。", "filePath")
        End If
        If view Is Nothing Then Throw New ArgumentNullException("view")
        If visibleColumnCount < 0 Then
            Throw New ArgumentOutOfRangeException("visibleColumnCount")
        End If
        If String.IsNullOrEmpty(delimiter) Then
            Throw New ArgumentException("区切り文字が指定されていません。", "delimiter")
        End If
        If String.IsNullOrEmpty(newLine) Then newLine = Environment.NewLine

        Dim encoding As Encoding = CsvTextCodec.GetEncodingForWrite(encodingKind)
        Dim directory As String = Path.GetDirectoryName(Path.GetFullPath(filePath))
        Dim temporaryPath As String =
            Path.Combine(directory,
                         "." & Path.GetFileName(filePath) & "." &
                         Guid.NewGuid().ToString("N") & ".tmp")

        Try
            Using writer As New StreamWriter(temporaryPath, False, encoding)
                If includeHeader Then
                    If view.Table.ExtendedProperties.Contains(
                        CsvTableBuilder.OriginalHeaderTextProperty) Then
                        WriteRawRecord(
                            writer,
                            Convert.ToString(
                                view.Table.ExtendedProperties(
                                    CsvTableBuilder.OriginalHeaderTextProperty)),
                            newLine)
                    Else
                        Dim headers As String() =
                            GetHeaderFields(view.Table, visibleColumnCount)
                        If headers.Length > 0 Then
                            WriteRow(writer, headers, delimiter, newLine)
                        End If
                    End If
                End If

                For Each rowView As DataRowView In view
                    If view.Table.Columns.Contains(
                        CsvTableBuilder.IsMalformedColumn) AndAlso
                       Convert.ToBoolean(
                           rowView(CsvTableBuilder.IsMalformedColumn)) Then
                        WriteRawRecord(
                            writer,
                            Convert.ToString(
                                rowView(
                                    CsvTableBuilder.OriginalRecordTextColumn)),
                            newLine)
                        Continue For
                    End If

                    Dim fieldCount As Integer = visibleColumnCount
                    If view.Table.Columns.Contains(
                        CsvTableBuilder.OriginalFieldCountColumn) Then
                        fieldCount = Math.Min(
                            visibleColumnCount,
                            Convert.ToInt32(
                                rowView(CsvTableBuilder.OriginalFieldCountColumn)))
                    End If

                    If fieldCount > 0 Then
                        Dim fields(fieldCount - 1) As String
                        For index As Integer = 0 To fieldCount - 1
                            If rowView(index) Is DBNull.Value Then
                                fields(index) = String.Empty
                            Else
                                fields(index) = Convert.ToString(rowView(index))
                            End If
                        Next
                        WriteRow(writer, fields, delimiter, newLine)
                    End If
                Next
            End Using

            CommitTemporaryFile(temporaryPath, filePath)
        Finally
            If File.Exists(temporaryPath) Then
                File.Delete(temporaryPath)
            End If
        End Try
    End Sub

    Private Shared Function GetHeaderFields(table As DataTable,
                                            visibleColumnCount As Integer) As String()
        If table.ExtendedProperties.Contains(
            CsvTableBuilder.OriginalHeaderFieldsProperty) Then
            Dim originalHeader As String() =
                TryCast(
                    table.ExtendedProperties(
                        CsvTableBuilder.OriginalHeaderFieldsProperty),
                    String())
            If originalHeader IsNot Nothing Then
                Return CType(originalHeader.Clone(), String())
            End If
        End If

        If visibleColumnCount <= 0 Then Return New String() {}

        Dim headers(visibleColumnCount - 1) As String
        For index As Integer = 0 To visibleColumnCount - 1
            headers(index) = table.Columns(index).Caption
        Next
        Return headers
    End Function

    Public Shared Function EscapeField(value As String,
                                       delimiter As String) As String
        If value Is Nothing Then value = String.Empty

        Dim needsQuotes As Boolean =
            value.Contains(delimiter) OrElse
            value.IndexOf(ControlChars.Quote) >= 0 OrElse
            value.IndexOf(ControlChars.Cr) >= 0 OrElse
            value.IndexOf(ControlChars.Lf) >= 0 OrElse
            value.Length <> value.Trim().Length

        If Not needsQuotes Then Return value
        Return ControlChars.Quote &
               value.Replace(ControlChars.Quote,
                             ControlChars.Quote & ControlChars.Quote) &
               ControlChars.Quote
    End Function

    Private Shared Sub WriteRow(writer As StreamWriter,
                                fields As String(),
                                delimiter As String,
                                newLine As String)
        For index As Integer = 0 To fields.Length - 1
            If index > 0 Then writer.Write(delimiter)
            writer.Write(
                EscapeField(
                    NormalizeNewLines(fields(index), newLine),
                    delimiter))
        Next
        writer.Write(newLine)
    End Sub

    Private Shared Sub WriteRawRecord(writer As StreamWriter,
                                      originalText As String,
                                      newLine As String)
        writer.Write(NormalizeNewLines(originalText, newLine))
        writer.Write(newLine)
    End Sub

    Private Shared Function NormalizeNewLines(value As String,
                                              newLine As String) As String
        If value Is Nothing Then Return String.Empty

        Dim normalized As String =
            value.Replace(ControlChars.CrLf, ControlChars.Lf).
                  Replace(ControlChars.Cr, ControlChars.Lf)
        If newLine <> ControlChars.Lf Then
            normalized = normalized.Replace(ControlChars.Lf, newLine)
        End If
        Return normalized
    End Function

    Private Shared Sub CommitTemporaryFile(temporaryPath As String,
                                           destinationPath As String)
        If Not File.Exists(destinationPath) Then
            File.Move(temporaryPath, destinationPath)
            Return
        End If

        Try
            File.Replace(temporaryPath, destinationPath, Nothing)
        Catch ex As PlatformNotSupportedException
            File.Copy(temporaryPath, destinationPath, True)
            File.Delete(temporaryPath)
        Catch ex As IOException
            File.Copy(temporaryPath, destinationPath, True)
            File.Delete(temporaryPath)
        End Try
    End Sub
End Class
