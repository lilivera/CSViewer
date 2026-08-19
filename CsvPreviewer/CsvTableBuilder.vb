Imports System
Imports System.Data

Public NotInheritable Class CsvTableBuilder
    Public Const RecordNumberColumn As String = "__RecordNumber"
    Public Const PhysicalLineColumn As String = "__PhysicalLine"
    Public Const OriginalFieldCountColumn As String = "__OriginalFieldCount"
    Public Const OriginalRecordTextColumn As String = "__OriginalRecordText"
    Public Const IsMalformedColumn As String = "__IsMalformed"
    Public Const HasIssueColumn As String = "__HasIssue"
    Public Const SearchMatchColumn As String = "__SearchMatch"
    Public Const OriginalHeaderFieldsProperty As String = "OriginalHeaderFields"
    Public Const OriginalHeaderTextProperty As String = "OriginalHeaderText"

    Private Sub New()
    End Sub

    Public Shared Function Build(document As CsvDocument) As DataTable
        If document Is Nothing Then Throw New ArgumentNullException("document")

        Dim table As New DataTable("CsvData")
        table.CaseSensitive = False

        If document.HasHeader AndAlso document.Records.Count > 0 Then
            Dim header As CsvRecord = document.Records(0)
            table.ExtendedProperties(OriginalHeaderFieldsProperty) =
                CType(header.Fields.Clone(), String())
            If header.IsMalformed Then
                table.ExtendedProperties(OriginalHeaderTextProperty) =
                    If(header.OriginalText, String.Empty)
            End If
        End If

        Dim columnCount As Integer = document.ExpectedColumnCount
        For index As Integer = document.DataStartIndex To document.Records.Count - 1
            columnCount = Math.Max(columnCount, document.Records(index).Fields.Length)
        Next

        For index As Integer = 0 To columnCount - 1
            Dim column As New DataColumn("C" & (index + 1).ToString(), GetType(String))
            column.Caption = GetColumnCaption(document, index)
            column.DefaultValue = String.Empty
            column.ExtendedProperties("Internal") = False
            table.Columns.Add(column)
        Next

        AddInternalColumn(table, RecordNumberColumn, GetType(Integer))
        AddInternalColumn(table, PhysicalLineColumn, GetType(Long))
        AddInternalColumn(table, OriginalFieldCountColumn, GetType(Integer))
        AddInternalColumn(table, OriginalRecordTextColumn, GetType(String))
        AddInternalColumn(table, IsMalformedColumn, GetType(Boolean))
        AddInternalColumn(table, HasIssueColumn, GetType(Boolean))
        AddInternalColumn(table, SearchMatchColumn, GetType(Boolean))

        For index As Integer = document.DataStartIndex To document.Records.Count - 1
            Dim record As CsvRecord = document.Records(index)
            Dim row As DataRow = table.NewRow()

            For columnIndex As Integer = 0 To columnCount - 1
                If columnIndex < record.Fields.Length Then
                    row(columnIndex) = If(record.Fields(columnIndex), String.Empty)
                Else
                    row(columnIndex) = String.Empty
                End If
            Next

            row(RecordNumberColumn) = record.RecordNumber
            row(PhysicalLineColumn) = record.StartLineNumber
            row(OriginalFieldCountColumn) = record.Fields.Length
            row(OriginalRecordTextColumn) = If(record.OriginalText, String.Empty)
            row(IsMalformedColumn) = record.IsMalformed
            row(HasIssueColumn) = record.HasIssue
            row(SearchMatchColumn) = True
            table.Rows.Add(row)
        Next

        Return table
    End Function

    Public Shared Function GetVisibleColumnCount(table As DataTable) As Integer
        Dim count As Integer = 0
        For Each column As DataColumn In table.Columns
            If Not IsInternalColumn(column) Then count += 1
        Next
        Return count
    End Function

    Public Shared Function IsInternalColumn(column As DataColumn) As Boolean
        If column Is Nothing Then Return False
        If Not column.ExtendedProperties.Contains("Internal") Then Return False
        Return Convert.ToBoolean(column.ExtendedProperties("Internal"))
    End Function

    Private Shared Sub AddInternalColumn(table As DataTable,
                                         name As String,
                                         dataType As Type)
        Dim column As New DataColumn(name, dataType)
        column.ExtendedProperties("Internal") = True
        table.Columns.Add(column)
    End Sub

    Private Shared Function GetColumnCaption(document As CsvDocument,
                                             index As Integer) As String
        If document.HasHeader AndAlso document.Records.Count > 0 Then
            Dim header As String() = document.Records(0).Fields
            If index < header.Length AndAlso Not String.IsNullOrWhiteSpace(header(index)) Then
                Return header(index)
            End If
        End If

        Return "列" & (index + 1).ToString()
    End Function
End Class
