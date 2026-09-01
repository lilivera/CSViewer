Imports System
Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public NotInheritable Class MainForm
    Inherits Form

    Private Const ApplicationName As String = "CSViewer"

    Private ReadOnly _filePathTextBox As TextBox
    Private ReadOnly _openButton As Button
    Private ReadOnly _reloadButton As Button
    Private ReadOnly _exportButton As Button
    Private ReadOnly _encodingComboBox As ComboBox
    Private ReadOnly _delimiterComboBox As ComboBox
    Private ReadOnly _headerCheckBox As CheckBox
    Private ReadOnly _searchTextBox As TextBox
    Private ReadOnly _searchButton As Button
    Private ReadOnly _clearSearchButton As Button
    Private ReadOnly _sqlButton As Button
    Private ReadOnly _grid As BufferedDataGridView
    Private ReadOnly _issuesListView As ListView
    Private ReadOnly _splitContainer As SplitContainer
    Private ReadOnly _statusFileLabel As ToolStripStatusLabel
    Private ReadOnly _statusRowsLabel As ToolStripStatusLabel
    Private ReadOnly _statusFormatLabel As ToolStripStatusLabel
    Private ReadOnly _statusIssueLabel As ToolStripStatusLabel

    Private _document As CsvDocument
    Private _table As DataTable
    Private _view As DataView
    Private _loading As Boolean
    Private _searching As Boolean

    Private NotInheritable Class LoadedCsv
        Public Sub New(document As CsvDocument, table As DataTable)
            Me.Document = document
            Me.Table = table
        End Sub

        Public ReadOnly Property Document As CsvDocument
        Public ReadOnly Property Table As DataTable
    End Class

    Public Sub New()
        Text = ApplicationName
        Dim applicationIcon As Icon = AppIcon.Create()
        If applicationIcon IsNot Nothing Then Icon = applicationIcon
        StartPosition = FormStartPosition.CenterScreen
        ClientSize = New Size(1180, 760)
        MinimumSize = New Size(960, 600)
        Font = New Font("Meiryo UI", 9.0F)
        AllowDrop = True
        KeyPreview = True

        Dim rootLayout As New TableLayoutPanel()
        rootLayout.Dock = DockStyle.Fill
        rootLayout.Padding = New Padding(8, 8, 8, 0)
        rootLayout.ColumnCount = 1
        rootLayout.RowCount = 3
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 40.0F))
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim fileLayout As New TableLayoutPanel()
        fileLayout.Dock = DockStyle.Fill
        fileLayout.ColumnCount = 5
        fileLayout.RowCount = 1
        fileLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        fileLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        fileLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        fileLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        fileLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim fileLabel As New Label()
        fileLabel.Text = "CSVファイル"
        fileLabel.AutoSize = True
        fileLabel.Anchor = AnchorStyles.Left
        fileLabel.Margin = New Padding(0, 0, 8, 0)

        _filePathTextBox = New TextBox()
        _filePathTextBox.ReadOnly = True
        _filePathTextBox.Dock = DockStyle.Fill
        _filePathTextBox.Margin = New Padding(0, 5, 8, 5)

        _openButton = New Button()
        _openButton.Text = "開く..."
        _openButton.AutoSize = True
        _openButton.MinimumSize = New Size(78, 30)
        _openButton.Margin = New Padding(0, 2, 6, 2)

        _reloadButton = New Button()
        _reloadButton.Text = "再読込"
        _reloadButton.AutoSize = True
        _reloadButton.MinimumSize = New Size(78, 30)
        _reloadButton.Enabled = False
        _reloadButton.Margin = New Padding(0, 2, 6, 2)

        _exportButton = New Button()
        _exportButton.Text = "別名保存..."
        _exportButton.AutoSize = True
        _exportButton.MinimumSize = New Size(96, 30)
        _exportButton.Enabled = False
        _exportButton.Margin = New Padding(0, 2, 0, 2)

        fileLayout.Controls.Add(fileLabel, 0, 0)
        fileLayout.Controls.Add(_filePathTextBox, 1, 0)
        fileLayout.Controls.Add(_openButton, 2, 0)
        fileLayout.Controls.Add(_reloadButton, 3, 0)
        fileLayout.Controls.Add(_exportButton, 4, 0)

        Dim optionsPanel As New FlowLayoutPanel()
        optionsPanel.Dock = DockStyle.Fill
        optionsPanel.FlowDirection = FlowDirection.LeftToRight
        optionsPanel.WrapContents = False
        optionsPanel.AutoScroll = True
        optionsPanel.Padding = New Padding(0, 4, 0, 2)

        optionsPanel.Controls.Add(CreateInlineLabel("文字コード"))

        _encodingComboBox = New ComboBox()
        _encodingComboBox.DropDownStyle = ComboBoxStyle.DropDownList
        _encodingComboBox.Width = 165
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("自動判定", CsvTextEncoding.AutoDetect))
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("UTF-8", CsvTextEncoding.Utf8NoBom))
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("Shift_JIS", CsvTextEncoding.ShiftJis))
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("UTF-16 LE", CsvTextEncoding.Utf16LittleEndian))
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("UTF-16 BE", CsvTextEncoding.Utf16BigEndian))
        _encodingComboBox.SelectedIndex = 0
        _encodingComboBox.Margin = New Padding(0, 0, 14, 0)
        optionsPanel.Controls.Add(_encodingComboBox)

        optionsPanel.Controls.Add(CreateInlineLabel("区切り"))

        _delimiterComboBox = New ComboBox()
        _delimiterComboBox.DropDownStyle = ComboBoxStyle.DropDownList
        _delimiterComboBox.Width = 110
        _delimiterComboBox.Items.Add(New OptionItem(Of CsvDelimiterOption)("自動判定", CsvDelimiterOption.AutoDetect))
        _delimiterComboBox.Items.Add(New OptionItem(Of CsvDelimiterOption)("カンマ", CsvDelimiterOption.Comma))
        _delimiterComboBox.Items.Add(New OptionItem(Of CsvDelimiterOption)("タブ", CsvDelimiterOption.Tab))
        _delimiterComboBox.Items.Add(New OptionItem(Of CsvDelimiterOption)("セミコロン", CsvDelimiterOption.Semicolon))
        _delimiterComboBox.Items.Add(New OptionItem(Of CsvDelimiterOption)("パイプ", CsvDelimiterOption.Pipe))
        _delimiterComboBox.SelectedIndex = 0
        _delimiterComboBox.Margin = New Padding(0, 0, 12, 0)
        optionsPanel.Controls.Add(_delimiterComboBox)

        _headerCheckBox = New CheckBox()
        _headerCheckBox.Text = "先頭行をヘッダーとして扱う"
        _headerCheckBox.Checked = True
        _headerCheckBox.AutoSize = True
        _headerCheckBox.Margin = New Padding(0, 4, 18, 0)
        optionsPanel.Controls.Add(_headerCheckBox)

        optionsPanel.Controls.Add(CreateInlineLabel("検索"))

        _searchTextBox = New TextBox()
        _searchTextBox.Width = 210
        _searchTextBox.Margin = New Padding(0, 0, 5, 0)
        optionsPanel.Controls.Add(_searchTextBox)

        _searchButton = New Button()
        _searchButton.Text = "絞り込み"
        _searchButton.AutoSize = True
        _searchButton.MinimumSize = New Size(78, 27)
        _searchButton.Margin = New Padding(0, 0, 5, 0)
        optionsPanel.Controls.Add(_searchButton)

        _clearSearchButton = New Button()
        _clearSearchButton.Text = "解除"
        _clearSearchButton.AutoSize = True
        _clearSearchButton.MinimumSize = New Size(62, 27)
        _clearSearchButton.Margin = New Padding(0)
        optionsPanel.Controls.Add(_clearSearchButton)

        _sqlButton = New Button()
        _sqlButton.Text = "SQL..."
        _sqlButton.AutoSize = True
        _sqlButton.MinimumSize = New Size(72, 27)
        _sqlButton.Enabled = False
        _sqlButton.Margin = New Padding(10, 0, 0, 0)
        optionsPanel.Controls.Add(_sqlButton)

        _grid = New BufferedDataGridView()
        _grid.Dock = DockStyle.Fill
        _grid.ReadOnly = True
        _grid.AllowUserToAddRows = False
        _grid.AllowUserToDeleteRows = False
        _grid.AllowUserToOrderColumns = True
        _grid.AllowUserToResizeRows = False
        _grid.AutoGenerateColumns = True
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        _grid.BackgroundColor = Color.White
        _grid.BorderStyle = BorderStyle.Fixed3D
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(52, 104, 171)
        _grid.DefaultCellStyle.SelectionForeColor = Color.White
        _grid.RowsDefaultCellStyle.BackColor = Color.White
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 252)
        _grid.MultiSelect = True
        _grid.RowHeadersWidth = 72
        _grid.RowTemplate.Height = 24
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect

        _issuesListView = New ListView()
        _issuesListView.Dock = DockStyle.Fill
        _issuesListView.View = View.Details
        _issuesListView.FullRowSelect = True
        _issuesListView.GridLines = True
        _issuesListView.HideSelection = False
        _issuesListView.Columns.Add("重要度", 80)
        _issuesListView.Columns.Add("行", 70)
        _issuesListView.Columns.Add("レコード", 80)
        _issuesListView.Columns.Add("分類", 110)
        _issuesListView.Columns.Add("内容", 720)

        _splitContainer = New SplitContainer()
        _splitContainer.Dock = DockStyle.Fill
        _splitContainer.Orientation = Orientation.Horizontal
        _splitContainer.Size = New Size(800, 600)
        _splitContainer.Panel1MinSize = 200
        _splitContainer.Panel2MinSize = 100
        _splitContainer.SplitterDistance = 440
        _splitContainer.Panel1.Controls.Add(_grid)
        _splitContainer.Panel2.Controls.Add(_issuesListView)
        _splitContainer.Panel2Collapsed = True

        rootLayout.Controls.Add(fileLayout, 0, 0)
        rootLayout.Controls.Add(optionsPanel, 0, 1)
        rootLayout.Controls.Add(_splitContainer, 0, 2)

        Dim statusStrip As New StatusStrip()
        _statusFileLabel = New ToolStripStatusLabel("ファイルをドラッグ＆ドロップするか、「開く」を押してください。")
        _statusFileLabel.Spring = True
        _statusFileLabel.TextAlign = ContentAlignment.MiddleLeft
        _statusRowsLabel = New ToolStripStatusLabel("行: -")
        _statusFormatLabel = New ToolStripStatusLabel("形式: -")
        _statusIssueLabel = New ToolStripStatusLabel("問題: -")
        statusStrip.Items.Add(_statusFileLabel)
        statusStrip.Items.Add(New ToolStripStatusLabel() With {.BorderSides = ToolStripStatusLabelBorderSides.Left})
        statusStrip.Items.Add(_statusRowsLabel)
        statusStrip.Items.Add(New ToolStripStatusLabel() With {.BorderSides = ToolStripStatusLabelBorderSides.Left})
        statusStrip.Items.Add(_statusFormatLabel)
        statusStrip.Items.Add(New ToolStripStatusLabel() With {.BorderSides = ToolStripStatusLabelBorderSides.Left})
        statusStrip.Items.Add(_statusIssueLabel)

        Controls.Add(rootLayout)
        Controls.Add(statusStrip)

        AddHandler _openButton.Click, AddressOf OpenButtonClick
        AddHandler _reloadButton.Click, AddressOf ReloadButtonClick
        AddHandler _exportButton.Click, AddressOf ExportButtonClick
        AddHandler _searchButton.Click, AddressOf SearchButtonClick
        AddHandler _clearSearchButton.Click, AddressOf ClearSearchButtonClick
        AddHandler _sqlButton.Click, AddressOf SqlButtonClick
        AddHandler _searchTextBox.KeyDown, AddressOf SearchTextBoxKeyDown
        AddHandler _encodingComboBox.SelectionChangeCommitted, AddressOf LoadOptionChanged
        AddHandler _delimiterComboBox.SelectionChangeCommitted, AddressOf LoadOptionChanged
        AddHandler _headerCheckBox.CheckedChanged, AddressOf LoadOptionChanged
        AddHandler _grid.DataBindingComplete, AddressOf GridDataBindingComplete
        AddHandler _grid.RowPostPaint, AddressOf GridRowPostPaint
        AddHandler _grid.CellFormatting, AddressOf GridCellFormatting
        AddHandler _grid.Sorted, AddressOf GridSorted
        AddHandler _issuesListView.DoubleClick, AddressOf IssuesListViewDoubleClick
        AddHandler DragEnter, AddressOf MainFormDragEnter
        AddHandler DragDrop, AddressOf MainFormDragDrop
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)

        Dim arguments As String() = Environment.GetCommandLineArgs()
        If arguments.Length >= 2 AndAlso File.Exists(arguments(1)) Then
            LoadCsv(arguments(1))
        End If
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message,
                                               keyData As Keys) As Boolean
        If keyData = (Keys.Control Or Keys.O) Then
            OpenFileUsingDialog()
            Return True
        End If
        If keyData = (Keys.Control Or Keys.S) Then
            ExportCurrentView()
            Return True
        End If
        If keyData = Keys.F5 Then
            ReloadCurrentFile()
            Return True
        End If
        If keyData = (Keys.Control Or Keys.F) Then
            _searchTextBox.Focus()
            _searchTextBox.SelectAll()
            Return True
        End If
        If keyData = (Keys.Control Or Keys.Q) Then
            ShowSqlQuery()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Private Shared Function CreateInlineLabel(textValue As String) As Label
        Dim label As New Label()
        label.Text = textValue
        label.AutoSize = True
        label.Margin = New Padding(0, 4, 6, 0)
        Return label
    End Function

    Private Sub OpenButtonClick(sender As Object, e As EventArgs)
        OpenFileUsingDialog()
    End Sub

    Private Sub ReloadButtonClick(sender As Object, e As EventArgs)
        ReloadCurrentFile()
    End Sub

    Private Sub ExportButtonClick(sender As Object, e As EventArgs)
        ExportCurrentView()
    End Sub

    Private Sub SearchButtonClick(sender As Object, e As EventArgs)
        ApplySearch()
    End Sub

    Private Sub ClearSearchButtonClick(sender As Object, e As EventArgs)
        _searchTextBox.Clear()
        ApplySearch()
    End Sub

    Private Sub SqlButtonClick(sender As Object, e As EventArgs)
        ShowSqlQuery()
    End Sub

    Private Sub SearchTextBoxKeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            ApplySearch()
            e.SuppressKeyPress = True
        End If
    End Sub

    Private Sub LoadOptionChanged(sender As Object, e As EventArgs)
        If _loading OrElse _document Is Nothing Then Return
        ReloadCurrentFile()
    End Sub

    Private Sub GridDataBindingComplete(sender As Object,
                                        e As DataGridViewBindingCompleteEventArgs)
        ConfigureGridColumns()
        _grid.Invalidate()
        UpdateStatus()
    End Sub

    Private Sub GridSorted(sender As Object, e As EventArgs)
        _grid.Invalidate()
        UpdateStatus()
    End Sub

    Private Sub GridCellFormatting(sender As Object,
                                   e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 OrElse e.RowIndex >= _grid.Rows.Count Then Return
        Dim rowView As DataRowView =
            TryCast(_grid.Rows(e.RowIndex).DataBoundItem, DataRowView)
        If rowView Is Nothing Then Return
        If Convert.ToBoolean(rowView(CsvTableBuilder.HasIssueColumn)) Then
            e.CellStyle.BackColor = Color.MistyRose
        End If
    End Sub

    Private Sub GridRowPostPaint(sender As Object,
                                 e As DataGridViewRowPostPaintEventArgs)
        If e.RowIndex < 0 OrElse e.RowIndex >= _grid.Rows.Count Then Return

        Dim textValue As String = (e.RowIndex + 1).ToString()
        Dim rowView As DataRowView =
            TryCast(_grid.Rows(e.RowIndex).DataBoundItem, DataRowView)
        If rowView IsNot Nothing Then
            textValue = Convert.ToString(rowView(CsvTableBuilder.PhysicalLineColumn))
        End If

        Dim bounds As New Rectangle(
            e.RowBounds.Left,
            e.RowBounds.Top,
            _grid.RowHeadersWidth - 6,
            e.RowBounds.Height)

        TextRenderer.DrawText(
            e.Graphics,
            textValue,
            _grid.RowHeadersDefaultCellStyle.Font,
            bounds,
            _grid.RowHeadersDefaultCellStyle.ForeColor,
            TextFormatFlags.Right Or TextFormatFlags.VerticalCenter)
    End Sub

    Private Sub IssuesListViewDoubleClick(sender As Object, e As EventArgs)
        If _issuesListView.SelectedItems.Count = 0 Then Return

        Dim issue As CsvIssue = TryCast(_issuesListView.SelectedItems(0).Tag, CsvIssue)
        If issue Is Nothing Then Return

        If issue.Category = "ヘッダー" AndAlso issue.ColumnIndex >= 0 Then
            ShowHeaderIssue(issue)
            Return
        End If

        If issue.RecordNumber <= 0 Then
            ShowIssueDetails(issue)
            Return
        End If

        If TrySelectRecord(issue.RecordNumber) Then
            ShowMalformedOriginalIfNeeded(issue.RecordNumber)
            Return
        End If

        If _view IsNot Nothing AndAlso Not String.IsNullOrEmpty(_view.RowFilter) Then
            _searchTextBox.Clear()
            _view.RowFilter = String.Empty
            UpdateStatus()
            If TrySelectRecord(issue.RecordNumber) Then
                ShowMalformedOriginalIfNeeded(issue.RecordNumber)
                Return
            End If
        End If

        ShowIssueDetails(issue)
    End Sub

    Private Function TrySelectRecord(recordNumber As Integer) As Boolean
        For Each gridRow As DataGridViewRow In _grid.Rows
            Dim rowView As DataRowView = TryCast(gridRow.DataBoundItem, DataRowView)
            If rowView Is Nothing Then Continue For
            If Convert.ToInt32(rowView(CsvTableBuilder.RecordNumberColumn)) <> recordNumber Then
                Continue For
            End If

            _grid.ClearSelection()
            If gridRow.Cells.Count > 0 Then
                gridRow.Cells(0).Selected = True
                _grid.CurrentCell = gridRow.Cells(0)
            End If
            _grid.FirstDisplayedScrollingRowIndex = gridRow.Index
            Return True
        Next
        Return False
    End Function

    Private Sub ShowHeaderIssue(issue As CsvIssue)
        If issue.ColumnIndex >= 0 AndAlso issue.ColumnIndex < _grid.Columns.Count Then
            Dim column As DataGridViewColumn = _grid.Columns(issue.ColumnIndex)
            If column.Visible Then
                _grid.FirstDisplayedScrollingColumnIndex = column.Index
                If _grid.Rows.Count > 0 Then
                    _grid.ClearSelection()
                    _grid.CurrentCell = _grid.Rows(0).Cells(column.Index)
                    _grid.CurrentCell.Selected = True
                End If
            End If
        End If

        Dim detail As String = issue.Message
        If _table IsNot Nothing AndAlso
           _table.ExtendedProperties.Contains(CsvTableBuilder.OriginalHeaderTextProperty) Then
            Dim rawHeader As String = Convert.ToString(
                _table.ExtendedProperties(CsvTableBuilder.OriginalHeaderTextProperty))
            If rawHeader.Length > 0 Then
                detail &= Environment.NewLine & Environment.NewLine &
                          "ヘッダー原文:" & Environment.NewLine & rawHeader
            End If
        End If
        MessageBox.Show(Me, detail, "ヘッダーの問題", MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub

    Private Sub ShowIssueDetails(issue As CsvIssue)
        MessageBox.Show(
            Me,
            issue.Message,
            issue.Category,
            MessageBoxButtons.OK,
            If(issue.Severity = CsvIssueSeverity.[Error],
               MessageBoxIcon.Error,
               MessageBoxIcon.Warning))
    End Sub

    Private Sub ShowMalformedOriginalIfNeeded(recordNumber As Integer)
        If _table Is Nothing Then Return
        For Each row As DataRow In _table.Rows
            If Convert.ToInt32(row(CsvTableBuilder.RecordNumberColumn)) <> recordNumber Then
                Continue For
            End If
            If Not Convert.ToBoolean(row(CsvTableBuilder.IsMalformedColumn)) Then Return
            Dim originalText As String = Convert.ToString(row(CsvTableBuilder.OriginalRecordTextColumn))
            If originalText.Length = 0 Then Return
            MessageBox.Show(
                Me,
                originalText,
                "CSV構文エラー行の原文",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information)
            Return
        Next
    End Sub

    Private Sub MainFormDragEnter(sender As Object, e As DragEventArgs)
        If e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        Else
            e.Effect = DragDropEffects.None
        End If
    End Sub

    Private Sub MainFormDragDrop(sender As Object, e As DragEventArgs)
        If e.Data Is Nothing Then Return
        Dim paths As String() = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If paths Is Nothing OrElse paths.Length = 0 Then Return
        If File.Exists(paths(0)) Then LoadCsv(paths(0))
    End Sub

    Private Sub OpenFileUsingDialog()
        If _loading OrElse _searching Then Return

        Using dialog As New OpenFileDialog()
            dialog.Title = "CSVファイルを開く"
            dialog.Filter =
                "CSV・テキストファイル (*.csv;*.txt;*.tsv)|*.csv;*.txt;*.tsv|" &
                "CSVファイル (*.csv)|*.csv|" &
                "すべてのファイル (*.*)|*.*"
            dialog.CheckFileExists = True
            dialog.Multiselect = False

            If _document IsNot Nothing AndAlso Not String.IsNullOrEmpty(_document.FilePath) Then
                dialog.InitialDirectory = Path.GetDirectoryName(_document.FilePath)
            End If

            If dialog.ShowDialog(Me) = DialogResult.OK Then
                LoadCsv(dialog.FileName)
            End If
        End Using
    End Sub

    Private Sub ReloadCurrentFile()
        If _document Is Nothing OrElse String.IsNullOrEmpty(_document.FilePath) Then Return
        LoadCsv(_document.FilePath)
    End Sub

    Private Async Sub LoadCsv(filePath As String,
                              Optional optionsOverride As CsvLoadOptions = Nothing)
        If _loading OrElse _searching Then Return

        If Not File.Exists(filePath) Then
            MessageBox.Show(
                Me,
                "指定されたファイルが見つかりません。",
                ApplicationName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning)
            Return
        End If

        Dim options As CsvLoadOptions = If(optionsOverride, BuildLoadOptions())
        SetLoadingState(True)
        If optionsOverride IsNot Nothing Then ApplyLoadOptionsToControls(optionsOverride)
        _statusFileLabel.Text = "読み込み中..."

        Try
            Dim loaded As LoadedCsv =
                Await Task.Run(
                    Function() As LoadedCsv
                        Dim document As CsvDocument = CsvParser.Load(filePath, options)
                        Dim table As DataTable = CsvTableBuilder.Build(document)
                        document.ReleaseRecordStorage()
                        Return New LoadedCsv(document, table)
                    End Function)

            If IsDisposed Then Return

            _document = loaded.Document
            _table = loaded.Table
            _view = loaded.Table.DefaultView

            _searchTextBox.Clear()
            _view.RowFilter = String.Empty
            _grid.DataSource = _view
            _filePathTextBox.Text = loaded.Document.FilePath
            Text = Path.GetFileName(loaded.Document.FilePath) & " - " & ApplicationName

            PopulateIssues()
            ConfigureGridColumns()
            _grid.Invalidate()
            UpdateStatus()
        Catch ex As Exception
            If Not IsDisposed Then
                MessageBox.Show(
                    Me,
                    "CSVファイルを読み込めませんでした。" &
                    Environment.NewLine & Environment.NewLine & ex.Message,
                    "読み込みエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error)
                _statusFileLabel.Text = "読み込みに失敗しました。"
            End If
        Finally
            If Not IsDisposed Then SetLoadingState(False)
        End Try
    End Sub

    Private Sub ApplyLoadOptionsToControls(options As CsvLoadOptions)
        SelectOptionValue(_encodingComboBox,
                          If(options.Encoding = CsvTextEncoding.Utf8Bom,
                             CsvTextEncoding.Utf8NoBom,
                             options.Encoding))
        SelectOptionValue(_delimiterComboBox, options.Delimiter)
        _headerCheckBox.Checked = options.HasHeader
    End Sub

    Private Shared Sub SelectOptionValue(Of T)(comboBox As ComboBox, value As T)
        For index As Integer = 0 To comboBox.Items.Count - 1
            Dim item As OptionItem(Of T) = TryCast(comboBox.Items(index), OptionItem(Of T))
            If item IsNot Nothing AndAlso EqualityComparer(Of T).Default.Equals(item.Value, value) Then
                comboBox.SelectedIndex = index
                Return
            End If
        Next
    End Sub

    Private Sub SetLoadingState(isLoading As Boolean)
        _loading = isLoading
        Cursor = If(isLoading, Cursors.WaitCursor, Cursors.Default)
        Dim enabled As Boolean = Not isLoading AndAlso Not _searching
        _openButton.Enabled = enabled
        _reloadButton.Enabled = enabled AndAlso _document IsNot Nothing
        _exportButton.Enabled = enabled AndAlso _document IsNot Nothing
        _encodingComboBox.Enabled = enabled
        _delimiterComboBox.Enabled = enabled
        _headerCheckBox.Enabled = enabled
        _searchTextBox.Enabled = enabled
        _searchButton.Enabled = enabled
        _clearSearchButton.Enabled = enabled
        _sqlButton.Enabled =
            enabled AndAlso _table IsNot Nothing AndAlso
            CsvTableBuilder.GetVisibleColumnCount(_table) > 0
    End Sub

    Private Sub SetSearchingState(searching As Boolean)
        _searching = searching
        Cursor = If(searching, Cursors.WaitCursor, Cursors.Default)
        Dim enabled As Boolean = Not searching AndAlso Not _loading
        _openButton.Enabled = enabled
        _reloadButton.Enabled = enabled AndAlso _document IsNot Nothing
        _exportButton.Enabled = enabled AndAlso _document IsNot Nothing
        _encodingComboBox.Enabled = enabled
        _delimiterComboBox.Enabled = enabled
        _headerCheckBox.Enabled = enabled
        _searchTextBox.Enabled = enabled
        _searchButton.Enabled = enabled
        _clearSearchButton.Enabled = enabled
        _sqlButton.Enabled =
            enabled AndAlso _table IsNot Nothing AndAlso
            CsvTableBuilder.GetVisibleColumnCount(_table) > 0
        If searching Then _statusFileLabel.Text = "検索中..."
    End Sub

    Private Sub ShowSqlQuery()
        If _loading OrElse _searching OrElse _table Is Nothing Then Return
        Dim visibleColumnCount As Integer = CsvTableBuilder.GetVisibleColumnCount(_table)
        If visibleColumnCount = 0 Then Return
        Using dialog As New SqlQueryForm(_table, visibleColumnCount)
            dialog.ShowDialog(Me)
        End Using
    End Sub

    Private Function BuildLoadOptions() As CsvLoadOptions
        Dim options As New CsvLoadOptions()
        Dim encodingItem As OptionItem(Of CsvTextEncoding) =
            TryCast(_encodingComboBox.SelectedItem, OptionItem(Of CsvTextEncoding))
        If encodingItem IsNot Nothing Then options.Encoding = encodingItem.Value

        Dim delimiterItem As OptionItem(Of CsvDelimiterOption) =
            TryCast(_delimiterComboBox.SelectedItem, OptionItem(Of CsvDelimiterOption))
        If delimiterItem IsNot Nothing Then options.Delimiter = delimiterItem.Value

        options.HasHeader = _headerCheckBox.Checked
        Return options
    End Function

    Private Sub ConfigureGridColumns()
        If _table Is Nothing Then Return

        For Each gridColumn As DataGridViewColumn In _grid.Columns
            If Not _table.Columns.Contains(gridColumn.DataPropertyName) Then Continue For

            Dim dataColumn As DataColumn = _table.Columns(gridColumn.DataPropertyName)
            If CsvTableBuilder.IsInternalColumn(dataColumn) Then
                gridColumn.Visible = False
            Else
                gridColumn.Visible = True
                gridColumn.HeaderText = dataColumn.Caption
                gridColumn.SortMode = DataGridViewColumnSortMode.Programmatic
                gridColumn.HeaderCell.ToolTipText =
                    "クリックで昇順、降順、ソート解除を切り替えます。"
                gridColumn.MinimumWidth = 70
                gridColumn.Width = Math.Min(
                    300,
                    Math.Max(110, TextRenderer.MeasureText(dataColumn.Caption, Font).Width + 34))
            End If
        Next
    End Sub

    Private Sub PopulateIssues()
        _issuesListView.BeginUpdate()
        Try
            _issuesListView.Items.Clear()
            For Each issue As CsvIssue In _document.Issues
                Dim item As New ListViewItem(GetSeverityText(issue.Severity))
                item.SubItems.Add(If(issue.LineNumber > 0, issue.LineNumber.ToString(), "-"))
                item.SubItems.Add(If(issue.RecordNumber > 0, issue.RecordNumber.ToString(), "-"))
                item.SubItems.Add(issue.Category)
                item.SubItems.Add(issue.Message)
                item.Tag = issue

                If issue.Severity = CsvIssueSeverity.[Error] Then
                    item.ForeColor = Color.DarkRed
                ElseIf issue.Severity = CsvIssueSeverity.Warning Then
                    item.ForeColor = Color.DarkOrange
                End If
                _issuesListView.Items.Add(item)
            Next
        Finally
            _issuesListView.EndUpdate()
        End Try

        If _document.Issues.Count = 0 Then
            _splitContainer.Panel2Collapsed = True
        Else
            _splitContainer.Panel2Collapsed = False
            If _splitContainer.Height >= 360 Then
                _splitContainer.SplitterDistance =
                    Math.Max(_splitContainer.Panel1MinSize, _splitContainer.Height - 150)
            End If
        End If
    End Sub

    Private Async Sub ApplySearch()
        If _loading OrElse _searching OrElse _view Is Nothing OrElse _table Is Nothing Then Return

        Dim searchText As String = _searchTextBox.Text
        If String.IsNullOrEmpty(searchText) Then
            _view.RowFilter = String.Empty
            _grid.Invalidate()
            UpdateStatus()
            Return
        End If

        Dim table As DataTable = _table
        Dim visibleColumnCount As Integer = CsvTableBuilder.GetVisibleColumnCount(table)
        SetSearchingState(True)

        Try
            Dim matches As Boolean() =
                Await Task.Run(
                    Function() As Boolean()
                        Dim result(table.Rows.Count - 1) As Boolean
                        For rowIndex As Integer = 0 To table.Rows.Count - 1
                            Dim row As DataRow = table.Rows(rowIndex)
                            Dim isMatch As Boolean = False
                            For columnIndex As Integer = 0 To visibleColumnCount - 1
                                Dim value As String = Convert.ToString(row(columnIndex))
                                If value.IndexOf(
                                    searchText,
                                    StringComparison.CurrentCultureIgnoreCase) >= 0 Then
                                    isMatch = True
                                    Exit For
                                End If
                            Next

                            If Not isMatch AndAlso
                               Convert.ToBoolean(row(CsvTableBuilder.IsMalformedColumn)) Then
                                Dim originalText As String =
                                    Convert.ToString(row(CsvTableBuilder.OriginalRecordTextColumn))
                                isMatch = originalText.IndexOf(
                                    searchText,
                                    StringComparison.CurrentCultureIgnoreCase) >= 0
                            End If
                            result(rowIndex) = isMatch
                        Next
                        Return result
                    End Function)

            If IsDisposed OrElse Not Object.ReferenceEquals(table, _table) Then Return

            _view.RowFilter = String.Empty
            table.BeginLoadData()
            Try
                For rowIndex As Integer = 0 To table.Rows.Count - 1
                    table.Rows(rowIndex)(CsvTableBuilder.SearchMatchColumn) = matches(rowIndex)
                Next
            Finally
                table.EndLoadData()
            End Try
            _view.RowFilter = "[" & CsvTableBuilder.SearchMatchColumn & "] = True"
            _grid.Invalidate()
            UpdateStatus()
        Catch ex As Exception
            If Not IsDisposed Then
                MessageBox.Show(
                    Me,
                    "検索中にエラーが発生しました。" & Environment.NewLine & Environment.NewLine & ex.Message,
                    "検索エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error)
            End If
        Finally
            If Not IsDisposed Then
                SetSearchingState(False)
                If _document IsNot Nothing Then UpdateStatus()
            End If
        End Try
    End Sub

    Private Sub ExportCurrentView()
        If _loading OrElse _searching Then Return
        If _document Is Nothing OrElse _table Is Nothing OrElse _view Is Nothing Then Return

        Dim isFiltered As Boolean = Not String.IsNullOrEmpty(_view.RowFilter)
        Using optionsDialog As New ExportOptionsForm(
            _document.EncodingKind,
            _document.LineEnding.PreferredNewLine,
            isFiltered)

            If optionsDialog.ShowDialog(Me) <> DialogResult.OK Then Return

            Using saveDialog As New SaveFileDialog()
                ConfigureSaveDialog(saveDialog)
                If saveDialog.ShowDialog(Me) <> DialogResult.OK Then Return

                Dim overwritesSource As Boolean = IsSamePath(saveDialog.FileName, _document.FilePath)
                If overwritesSource Then
                    If _document.IsLossyDecode Then
                        MessageBox.Show(
                            Me,
                            "現在の表示には文字コード変換で代替文字が含まれています。" &
                            Environment.NewLine &
                            "元ファイルを保護するため上書きできません。文字コードを正しく指定して再読込してください。",
                            "上書き禁止",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error)
                        Return
                    End If

                    If Not IsSourceFileUnchanged() Then
                        MessageBox.Show(
                            Me,
                            "元ファイルは読み込み後に更新または置換されています。" &
                            Environment.NewLine &
                            "他の変更を失わないよう、再読込するか別名で保存してください。",
                            "外部更新を検知しました",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
                        Return
                    End If

                    Dim overwriteResult As DialogResult = MessageBox.Show(
                        Me,
                        "元のファイルを上書きします。続行しますか？",
                        "上書き確認",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2)
                    If overwriteResult <> DialogResult.Yes Then Return
                ElseIf _document.IsLossyDecode Then
                    Dim lossyResult As DialogResult = MessageBox.Show(
                        Me,
                        "現在の表示には、復号できなかったバイトを置換した代替文字が含まれています。" &
                        Environment.NewLine &
                        "保存先は元データと同一にはなりません。別ファイルとして保存しますか？",
                        "代替文字を含む保存",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2)
                    If lossyResult <> DialogResult.Yes Then Return
                End If

                Dim exportView As DataView = _view
                If Not optionsDialog.VisibleRowsOnly Then
                    exportView = New DataView(_table)
                    exportView.Sort = _view.Sort
                End If

                Cursor = Cursors.WaitCursor
                Try
                    CsvExporter.Export(
                        saveDialog.FileName,
                        exportView,
                        CsvTableBuilder.GetVisibleColumnCount(_table),
                        _document.Delimiter,
                        _document.HasHeader,
                        optionsDialog.SelectedEncoding,
                        optionsDialog.SelectedNewLine)

                    If overwritesSource Then
                        Dim reloadOptions As New CsvLoadOptions() With {
                            .Encoding = NormalizeReloadEncoding(optionsDialog.SelectedEncoding),
                            .Delimiter = DelimiterToOption(_document.Delimiter),
                            .HasHeader = _document.HasHeader
                        }
                        MessageBox.Show(
                            Me,
                            "元ファイルを保存しました。保存後の内容を再読込します。",
                            ApplicationName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
                        LoadCsv(saveDialog.FileName, reloadOptions)
                        Return
                    End If

                    _statusFileLabel.Text = "保存しました: " & saveDialog.FileName
                    MessageBox.Show(
                        Me,
                        "ファイルを保存しました。",
                        ApplicationName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information)
                Catch ex As Exception
                    MessageBox.Show(
                        Me,
                        "ファイルを保存できませんでした。" &
                        Environment.NewLine & Environment.NewLine & ex.Message,
                        "保存エラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error)
                Finally
                    Cursor = Cursors.Default
                End Try
            End Using
        End Using
    End Sub

    Private Sub ConfigureSaveDialog(saveDialog As SaveFileDialog)
        Dim extension As String = GetDefaultSaveExtension()
        saveDialog.Title = "区切りテキストを別名保存"
        If extension = "tsv" Then
            saveDialog.Filter = "TSVファイル (*.tsv)|*.tsv|すべてのファイル (*.*)|*.*"
        ElseIf extension = "csv" Then
            saveDialog.Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*"
        Else
            saveDialog.Filter = "テキストファイル (*." & extension & ")|*." & extension & "|すべてのファイル (*.*)|*.*"
        End If
        saveDialog.AddExtension = True
        saveDialog.DefaultExt = extension
        saveDialog.OverwritePrompt = True
        saveDialog.InitialDirectory = Path.GetDirectoryName(_document.FilePath)
        saveDialog.FileName =
            Path.GetFileNameWithoutExtension(_document.FilePath) & "_preview." & extension
    End Sub

    Private Function GetDefaultSaveExtension() As String
        If _document.Delimiter = ControlChars.Tab Then Return "tsv"
        If _document.Delimiter = "," Then Return "csv"

        Dim sourceExtension As String = Path.GetExtension(_document.FilePath)
        If Not String.IsNullOrWhiteSpace(sourceExtension) Then
            Return sourceExtension.TrimStart("."c)
        End If
        Return "txt"
    End Function

    Private Function IsSourceFileUnchanged() As Boolean
        Try
            If Not File.Exists(_document.FilePath) Then Return False
            Dim info As New FileInfo(_document.FilePath)
            Return info.Length = _document.FileSize AndAlso
                   info.LastWriteTimeUtc = _document.LastWriteTimeUtc
        Catch ex As IOException
            Return False
        Catch ex As UnauthorizedAccessException
            Return False
        End Try
    End Function

    Private Shared Function IsSamePath(left As String, right As String) As Boolean
        Return String.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function NormalizeReloadEncoding(value As CsvTextEncoding) As CsvTextEncoding
        If value = CsvTextEncoding.Utf8Bom Then Return CsvTextEncoding.Utf8NoBom
        Return value
    End Function

    Private Shared Function DelimiterToOption(delimiter As String) As CsvDelimiterOption
        If delimiter = ControlChars.Tab Then Return CsvDelimiterOption.Tab
        If delimiter = ";" Then Return CsvDelimiterOption.Semicolon
        If delimiter = "|" Then Return CsvDelimiterOption.Pipe
        Return CsvDelimiterOption.Comma
    End Function

    Private Sub UpdateStatus()
        If _document Is Nothing OrElse _view Is Nothing Then Return

        _statusFileLabel.Text =
            String.Format(
                "{0}  ({1})",
                Path.GetFileName(_document.FilePath),
                FormatFileSize(_document.FileSize))

        _statusRowsLabel.Text =
            String.Format(
                "行: {0:N0}/{1:N0}  列: {2:N0}",
                _view.Count,
                _document.DataRowCount,
                CsvTableBuilder.GetVisibleColumnCount(_table))

        _statusFormatLabel.Text =
            String.Format(
                "{0} / {1} / {2}",
                _document.EncodingDisplayName,
                CsvDelimiterResolver.GetDisplayName(_document.Delimiter),
                _document.LineEnding.DisplayName)

        Dim errorCount As Integer = 0
        Dim warningCount As Integer = 0
        For Each issue As CsvIssue In _document.Issues
            If issue.Severity = CsvIssueSeverity.[Error] Then
                errorCount += 1
            ElseIf issue.Severity = CsvIssueSeverity.Warning Then
                warningCount += 1
            End If
        Next

        _statusIssueLabel.Text =
            String.Format("問題: エラー {0} / 警告 {1}", errorCount, warningCount)
    End Sub

    Private Shared Function GetSeverityText(severity As CsvIssueSeverity) As String
        Select Case severity
            Case CsvIssueSeverity.[Error]
                Return "エラー"
            Case CsvIssueSeverity.Warning
                Return "警告"
            Case Else
                Return "情報"
        End Select
    End Function

    Private Shared Function FormatFileSize(size As Long) As String
        If size >= 1024L * 1024L * 1024L Then
            Return (size / (1024.0R * 1024.0R * 1024.0R)).ToString("0.00") & " GB"
        End If
        If size >= 1024L * 1024L Then
            Return (size / (1024.0R * 1024.0R)).ToString("0.00") & " MB"
        End If
        If size >= 1024L Then
            Return (size / 1024.0R).ToString("0.00") & " KB"
        End If
        Return size.ToString("N0") & " bytes"
    End Function
End Class
