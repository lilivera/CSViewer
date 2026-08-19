Imports System
Imports System.Data
Imports System.Windows.Forms

Public Class BufferedDataGridView
    Inherits DataGridView

    Private _sortedPropertyName As String = String.Empty
    Private _sortOrder As SortOrder = SortOrder.None

    Public Sub New()
        DoubleBuffered = True
    End Sub

    Protected Overrides Sub OnDataSourceChanged(e As EventArgs)
        _sortedPropertyName = String.Empty
        _sortOrder = SortOrder.None
        ClearSortGlyphs()
        MyBase.OnDataSourceChanged(e)
    End Sub

    Protected Overrides Sub OnColumnHeaderMouseClick(
        e As DataGridViewCellMouseEventArgs)

        If e.Button = MouseButtons.Left AndAlso
           e.ColumnIndex >= 0 AndAlso
           e.ColumnIndex < Columns.Count Then
            Dim column As DataGridViewColumn = Columns(e.ColumnIndex)
            If column.SortMode = DataGridViewColumnSortMode.Programmatic Then
                ApplyThreeStateSort(column)
            End If
        End If

        MyBase.OnColumnHeaderMouseClick(e)
    End Sub

    Private Sub ApplyThreeStateSort(column As DataGridViewColumn)
        Dim view As DataView = GetBoundView()
        If view Is Nothing OrElse
           String.IsNullOrEmpty(column.DataPropertyName) OrElse
           Not view.Table.Columns.Contains(column.DataPropertyName) Then
            Return
        End If

        Dim nextOrder As SortOrder = SortOrder.Ascending
        If String.Equals(
            _sortedPropertyName,
            column.DataPropertyName,
            StringComparison.Ordinal) Then
            Select Case _sortOrder
                Case SortOrder.Ascending
                    nextOrder = SortOrder.Descending
                Case SortOrder.Descending
                    nextOrder = SortOrder.None
            End Select
        End If

        ClearSortGlyphs()
        If nextOrder = SortOrder.None Then
            _sortedPropertyName = String.Empty
            _sortOrder = SortOrder.None
            view.Sort = String.Empty
        Else
            _sortedPropertyName = column.DataPropertyName
            _sortOrder = nextOrder
            view.Sort =
                QuoteColumnName(column.DataPropertyName) &
                If(nextOrder = SortOrder.Ascending, " ASC", " DESC")
            column.HeaderCell.SortGlyphDirection = nextOrder
        End If

        OnSorted(EventArgs.Empty)
    End Sub

    Private Function GetBoundView() As DataView
        Dim view As DataView = TryCast(DataSource, DataView)
        If view IsNot Nothing Then Return view

        Dim table As DataTable = TryCast(DataSource, DataTable)
        If table IsNot Nothing Then Return table.DefaultView
        Return Nothing
    End Function

    Private Sub ClearSortGlyphs()
        For Each column As DataGridViewColumn In Columns
            column.HeaderCell.SortGlyphDirection = SortOrder.None
        Next
    End Sub

    Private Shared Function QuoteColumnName(name As String) As String
        Return "[" &
               name.Replace("\", "\\").Replace("]", "\]") &
               "]"
    End Function
End Class
