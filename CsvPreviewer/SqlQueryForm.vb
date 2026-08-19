Imports System
Imports System.Data
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public NotInheritable Class SqlQueryForm
    Inherits Form

    Private ReadOnly _source As DataTable
    Private ReadOnly _visibleColumnCount As Integer
    Private ReadOnly _queryTextBox As TextBox
    Private ReadOnly _executeButton As Button
    Private ReadOnly _resultGrid As BufferedDataGridView
    Private ReadOnly _statusLabel As ToolStripStatusLabel
    Private _executing As Boolean

    Public Sub New(source As DataTable, visibleColumnCount As Integer)
        If source Is Nothing Then Throw New ArgumentNullException("source")
        _source = source
        _visibleColumnCount = visibleColumnCount

        Text = "SQLクエリ - CSViewer"
        Dim applicationIcon As Icon = AppIcon.Create()
        If applicationIcon IsNot Nothing Then Icon = applicationIcon
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(1040, 700)
        MinimumSize = New Size(760, 500)
        Font = New Font("Meiryo UI", 9.0F)
        KeyPreview = True

        Dim rootLayout As New TableLayoutPanel()
        rootLayout.Dock = DockStyle.Fill
        rootLayout.Padding = New Padding(10, 10, 10, 0)
        rootLayout.ColumnCount = 1
        rootLayout.RowCount = 4
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 76.0F))
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 112.0F))
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim guideLabel As New Label()
        guideLabel.Dock = DockStyle.Fill
        guideLabel.AutoEllipsis = True
        guideLabel.Text =
            "テーブル名: csv    列: " &
            CsvSqlEngine.GetColumnGuide(source, visibleColumnCount) &
            Environment.NewLine &
            "対応: SELECT / WHERE / LIKE / IN / ORDER BY / DISTINCT / TOP / LIMIT / COUNT(*)" &
            Environment.NewLine &
            "式: LTRIM / RTRIM / CONCAT / TO_CHAR / TO_NUMBER / CASE WHEN / LPAD / RPAD"
        guideLabel.ForeColor = Color.FromArgb(55, 65, 80)

        _queryTextBox = New TextBox()
        _queryTextBox.Dock = DockStyle.Fill
        _queryTextBox.Multiline = True
        _queryTextBox.AcceptsReturn = True
        _queryTextBox.AcceptsTab = True
        _queryTextBox.ScrollBars = ScrollBars.Both
        _queryTextBox.WordWrap = False
        _queryTextBox.Font = New Font("Consolas", 10.0F)
        _queryTextBox.Text =
            "SELECT *" & Environment.NewLine &
            "FROM csv" & Environment.NewLine &
            "LIMIT 100;"

        Dim buttonPanel As New FlowLayoutPanel()
        buttonPanel.Dock = DockStyle.Fill
        buttonPanel.FlowDirection = FlowDirection.LeftToRight
        buttonPanel.WrapContents = False
        buttonPanel.Padding = New Padding(0, 5, 0, 4)

        _executeButton = New Button()
        _executeButton.Text = "実行 (F5)"
        _executeButton.AutoSize = True
        _executeButton.MinimumSize = New Size(105, 30)

        Dim countExampleButton As New Button()
        countExampleButton.Text = "件数SQL"
        countExampleButton.AutoSize = True
        countExampleButton.MinimumSize = New Size(88, 30)

        Dim filterExampleButton As New Button()
        filterExampleButton.Text = "検索SQL"
        filterExampleButton.AutoSize = True
        filterExampleButton.MinimumSize = New Size(88, 30)

        Dim closeButtonValue As New Button()
        closeButtonValue.Text = "閉じる"
        closeButtonValue.AutoSize = True
        closeButtonValue.MinimumSize = New Size(88, 30)
        closeButtonValue.DialogResult = DialogResult.Cancel

        buttonPanel.Controls.Add(_executeButton)
        buttonPanel.Controls.Add(countExampleButton)
        buttonPanel.Controls.Add(filterExampleButton)
        buttonPanel.Controls.Add(closeButtonValue)

        _resultGrid = New BufferedDataGridView()
        _resultGrid.Dock = DockStyle.Fill
        _resultGrid.ReadOnly = True
        _resultGrid.AllowUserToAddRows = False
        _resultGrid.AllowUserToDeleteRows = False
        _resultGrid.AllowUserToOrderColumns = True
        _resultGrid.AllowUserToResizeRows = False
        _resultGrid.AutoGenerateColumns = True
        _resultGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        _resultGrid.BackgroundColor = Color.White
        _resultGrid.ClipboardCopyMode =
            DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
        _resultGrid.SelectionMode = DataGridViewSelectionMode.CellSelect
        _resultGrid.MultiSelect = True

        rootLayout.Controls.Add(guideLabel, 0, 0)
        rootLayout.Controls.Add(_queryTextBox, 0, 1)
        rootLayout.Controls.Add(buttonPanel, 0, 2)
        rootLayout.Controls.Add(_resultGrid, 0, 3)

        Dim statusStrip As New StatusStrip()
        _statusLabel = New ToolStripStatusLabel(
            "SQLを入力してF5またはCtrl+Enterで実行してください。")
        _statusLabel.Spring = True
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft
        statusStrip.Items.Add(_statusLabel)

        Controls.Add(rootLayout)
        Controls.Add(statusStrip)
        CancelButton = closeButtonValue

        AddHandler _executeButton.Click, AddressOf ExecuteButtonClick
        AddHandler countExampleButton.Click, AddressOf CountExampleButtonClick
        AddHandler filterExampleButton.Click, AddressOf FilterExampleButtonClick
        AddHandler _resultGrid.DataBindingComplete, AddressOf ResultGridDataBindingComplete
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message,
                                               keyData As Keys) As Boolean
        If keyData = Keys.F5 OrElse
           keyData = (Keys.Control Or Keys.Enter) Then
            ExecuteQuery()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Private Sub ExecuteButtonClick(sender As Object, e As EventArgs)
        ExecuteQuery()
    End Sub

    Private Sub CountExampleButtonClick(sender As Object, e As EventArgs)
        _queryTextBox.Text =
            "SELECT COUNT(*) AS 件数" & Environment.NewLine &
            "FROM csv;"
        _queryTextBox.Focus()
    End Sub

    Private Sub FilterExampleButtonClick(sender As Object, e As EventArgs)
        _queryTextBox.Text =
            "SELECT *" & Environment.NewLine &
            "FROM csv" & Environment.NewLine &
            "WHERE C1 LIKE '%検索文字%'" & Environment.NewLine &
            "ORDER BY C1" & Environment.NewLine &
            "LIMIT 100;"
        _queryTextBox.Focus()
    End Sub

    Private Async Sub ExecuteQuery()
        If _executing Then Return
        Dim sql As String = _queryTextBox.Text
        SetExecutingState(True)

        Try
            Dim result As CsvSqlResult =
                Await Task.Run(
                    Function() CsvSqlEngine.Execute(
                        _source,
                        _visibleColumnCount,
                        sql))
            If IsDisposed Then Return

            _resultGrid.DataSource = result.Table
            _statusLabel.ForeColor = SystemColors.ControlText
            _statusLabel.Text =
                String.Format(
                    "一致 {0:N0} 行 / 結果 {1:N0} 行",
                    result.MatchedRowCount,
                    result.ReturnedRowCount)
        Catch ex As CsvSqlException
            If Not IsDisposed Then ShowQueryError(ex.Message)
        Catch ex As Exception
            If Not IsDisposed Then
                ShowQueryError("SQLの実行中にエラーが発生しました: " & ex.Message)
            End If
        Finally
            If Not IsDisposed Then SetExecutingState(False)
        End Try
    End Sub

    Private Sub SetExecutingState(executing As Boolean)
        _executing = executing
        _executeButton.Enabled = Not executing
        _queryTextBox.ReadOnly = executing
        Cursor = If(executing, Cursors.WaitCursor, Cursors.Default)
        If executing Then
            _statusLabel.ForeColor = SystemColors.ControlText
            _statusLabel.Text = "実行中..."
        End If
    End Sub

    Private Sub ShowQueryError(message As String)
        _statusLabel.ForeColor = Color.DarkRed
        _statusLabel.Text = message
        MessageBox.Show(
            Me,
            message,
            "SQLエラー",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning)
    End Sub

    Private Sub ResultGridDataBindingComplete(
        sender As Object,
        e As DataGridViewBindingCompleteEventArgs)
        For Each column As DataGridViewColumn In _resultGrid.Columns
            column.SortMode = DataGridViewColumnSortMode.Programmatic
            column.HeaderCell.ToolTipText =
                "クリックで昇順、降順、ソート解除を切り替えます。"
            column.MinimumWidth = 70
            column.Width = Math.Min(
                300,
                Math.Max(
                    110,
                    TextRenderer.MeasureText(column.HeaderText, Font).Width + 34))
        Next
    End Sub
End Class
