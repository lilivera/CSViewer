Imports System
Imports System.Drawing
Imports System.Windows.Forms

Public NotInheritable Class ExportOptionsForm
    Inherits Form

    Private ReadOnly _encodingComboBox As ComboBox
    Private ReadOnly _newLineComboBox As ComboBox
    Private ReadOnly _visibleRowsOnlyCheckBox As CheckBox

    Public Sub New(sourceEncoding As CsvTextEncoding,
                   sourceNewLine As String,
                   isFiltered As Boolean)
        Text = "保存オプション"
        Dim applicationIcon As Icon = AppIcon.Create()
        If applicationIcon IsNot Nothing Then Icon = applicationIcon
        FormBorderStyle = FormBorderStyle.FixedDialog
        StartPosition = FormStartPosition.CenterParent
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        ClientSize = New Size(470, 210)
        Font = New Font("Meiryo UI", 9.0F)

        Dim layout As New TableLayoutPanel()
        layout.Dock = DockStyle.Fill
        layout.Padding = New Padding(14)
        layout.ColumnCount = 2
        layout.RowCount = 4
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim encodingLabel As New Label()
        encodingLabel.Text = "文字コード"
        encodingLabel.AutoSize = True
        encodingLabel.Anchor = AnchorStyles.Left

        _encodingComboBox = New ComboBox()
        _encodingComboBox.DropDownStyle = ComboBoxStyle.DropDownList
        _encodingComboBox.Width = 240
        _encodingComboBox.Anchor = AnchorStyles.Left
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("UTF-8（BOMなし）", CsvTextEncoding.Utf8NoBom))
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("UTF-8（BOMあり）", CsvTextEncoding.Utf8Bom))
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("Shift_JIS", CsvTextEncoding.ShiftJis))
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("UTF-16 LE", CsvTextEncoding.Utf16LittleEndian))
        _encodingComboBox.Items.Add(New OptionItem(Of CsvTextEncoding)("UTF-16 BE", CsvTextEncoding.Utf16BigEndian))
        SelectEncoding(sourceEncoding)

        Dim newLineLabel As New Label()
        newLineLabel.Text = "改行コード"
        newLineLabel.AutoSize = True
        newLineLabel.Anchor = AnchorStyles.Left

        _newLineComboBox = New ComboBox()
        _newLineComboBox.DropDownStyle = ComboBoxStyle.DropDownList
        _newLineComboBox.Width = 240
        _newLineComboBox.Anchor = AnchorStyles.Left
        _newLineComboBox.Items.Add(
            New OptionItem(Of String)(
                "元ファイルに合わせる（" & GetNewLineName(sourceNewLine) & "）",
                sourceNewLine))
        _newLineComboBox.Items.Add(New OptionItem(Of String)("CRLF（Windows）", ControlChars.CrLf))
        _newLineComboBox.Items.Add(New OptionItem(Of String)("LF", ControlChars.Lf))
        _newLineComboBox.SelectedIndex = 0

        _visibleRowsOnlyCheckBox = New CheckBox()
        _visibleRowsOnlyCheckBox.Text = "検索で絞り込まれた行だけ保存する"
        _visibleRowsOnlyCheckBox.AutoSize = True
        _visibleRowsOnlyCheckBox.Anchor = AnchorStyles.Left
        _visibleRowsOnlyCheckBox.Enabled = isFiltered
        _visibleRowsOnlyCheckBox.Checked = isFiltered

        Dim buttonPanel As New FlowLayoutPanel()
        buttonPanel.Dock = DockStyle.Fill
        buttonPanel.FlowDirection = FlowDirection.RightToLeft
        buttonPanel.WrapContents = False
        buttonPanel.Padding = New Padding(0, 8, 0, 0)

        Dim cancelButtonValue As New Button()
        cancelButtonValue.Text = "キャンセル"
        cancelButtonValue.DialogResult = DialogResult.Cancel
        cancelButtonValue.AutoSize = True
        cancelButtonValue.MinimumSize = New Size(96, 30)

        Dim okButton As New Button()
        okButton.Text = "保存先を選択"
        okButton.DialogResult = DialogResult.OK
        okButton.AutoSize = True
        okButton.MinimumSize = New Size(112, 30)

        buttonPanel.Controls.Add(cancelButtonValue)
        buttonPanel.Controls.Add(okButton)

        layout.Controls.Add(encodingLabel, 0, 0)
        layout.Controls.Add(_encodingComboBox, 1, 0)
        layout.Controls.Add(newLineLabel, 0, 1)
        layout.Controls.Add(_newLineComboBox, 1, 1)
        layout.Controls.Add(_visibleRowsOnlyCheckBox, 1, 2)
        layout.Controls.Add(buttonPanel, 0, 3)
        layout.SetColumnSpan(buttonPanel, 2)
        Controls.Add(layout)

        AcceptButton = okButton
        CancelButton = cancelButtonValue
    End Sub

    Public ReadOnly Property SelectedEncoding As CsvTextEncoding
        Get
            Dim item As OptionItem(Of CsvTextEncoding) =
                TryCast(_encodingComboBox.SelectedItem, OptionItem(Of CsvTextEncoding))
            If item Is Nothing Then Return CsvTextEncoding.Utf8NoBom
            Return item.Value
        End Get
    End Property

    Public ReadOnly Property SelectedNewLine As String
        Get
            Dim item As OptionItem(Of String) =
                TryCast(_newLineComboBox.SelectedItem, OptionItem(Of String))
            If item Is Nothing Then Return Environment.NewLine
            Return item.Value
        End Get
    End Property

    Public ReadOnly Property VisibleRowsOnly As Boolean
        Get
            Return _visibleRowsOnlyCheckBox.Enabled AndAlso
                   _visibleRowsOnlyCheckBox.Checked
        End Get
    End Property

    Private Sub SelectEncoding(sourceEncoding As CsvTextEncoding)
        Dim target As CsvTextEncoding = sourceEncoding
        If target = CsvTextEncoding.AutoDetect Then target = CsvTextEncoding.Utf8NoBom

        For index As Integer = 0 To _encodingComboBox.Items.Count - 1
            Dim item As OptionItem(Of CsvTextEncoding) =
                TryCast(_encodingComboBox.Items(index), OptionItem(Of CsvTextEncoding))
            If item IsNot Nothing AndAlso item.Value = target Then
                _encodingComboBox.SelectedIndex = index
                Return
            End If
        Next

        _encodingComboBox.SelectedIndex = 0
    End Sub

    Private Shared Function GetNewLineName(newLine As String) As String
        If newLine = ControlChars.Lf Then Return "LF"
        If newLine = ControlChars.Cr Then Return "CR"
        Return "CRLF"
    End Function
End Class
