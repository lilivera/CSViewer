Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions

Public NotInheritable Class CsvSqlException
    Inherits Exception

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub

    Public Sub New(message As String, innerException As Exception)
        MyBase.New(message, innerException)
    End Sub
End Class

Public NotInheritable Class CsvSqlResult
    Public Sub New(table As DataTable,
                   matchedRowCount As Integer,
                   returnedRowCount As Integer)
        Me.Table = table
        Me.MatchedRowCount = matchedRowCount
        Me.ReturnedRowCount = returnedRowCount
    End Sub

    Public ReadOnly Property Table As DataTable
    Public ReadOnly Property MatchedRowCount As Integer
    Public ReadOnly Property ReturnedRowCount As Integer
End Class

Public NotInheritable Class CsvSqlEngine
    Private Sub New()
    End Sub

    Public Shared Function Execute(source As DataTable,
                                   visibleColumnCount As Integer,
                                   sql As String) As CsvSqlResult
        If source Is Nothing Then Throw New ArgumentNullException("source")
        If visibleColumnCount < 0 OrElse
           visibleColumnCount > source.Columns.Count Then
            Throw New ArgumentOutOfRangeException("visibleColumnCount")
        End If
        If String.IsNullOrWhiteSpace(sql) Then
            Throw New CsvSqlException("SQLを入力してください。")
        End If
        If visibleColumnCount = 0 Then
            Throw New CsvSqlException("SQLで参照できるCSV列がありません。")
        End If

        Dim query As ParsedQuery = SqlParser.Parse(sql)
        Dim resolver As New ColumnResolver(source, visibleColumnCount)
        Dim selectedColumns As List(Of SelectedColumn) =
            ResolveSelectedColumns(query.SelectTokens, resolver)
        Dim countOnly As Boolean = IsCountOnly(selectedColumns)
        Dim whereExpression As SqlExpression = Nothing
        If query.WhereTokens.Count > 0 Then
            whereExpression =
                New ExpressionParser(
                    query.WhereTokens,
                    resolver).ParseConditionComplete()
        End If

        Dim rows As New List(Of RowEntry)()
        For index As Integer = 0 To source.Rows.Count - 1
            Dim row As DataRow = source.Rows(index)
            If whereExpression Is Nothing OrElse
               EvaluateCondition(whereExpression, row) Then
                rows.Add(New RowEntry(row, index))
            End If
        Next

        Dim matchedCount As Integer = rows.Count
        Dim aliasMap As Dictionary(Of String, SqlExpression) =
            BuildExpressionAliasMap(selectedColumns)
        Dim orderSpecifications As List(Of OrderSpecification) =
            ResolveOrderBy(query.OrderTokens, resolver, aliasMap)
        ApplyOrdering(rows, orderSpecifications)

        If countOnly Then
            Dim countTable As New DataTable("SqlResult")
            countTable.Columns.Add(selectedColumns(0).OutputName, GetType(Long))
            If GetMaximumRows(query) > 0 Then
                countTable.Rows.Add(CLng(matchedCount))
            End If
            Return New CsvSqlResult(
                countTable,
                matchedCount,
                countTable.Rows.Count)
        End If

        Dim resultTable As DataTable = CreateResultTable(selectedColumns)
        Dim maximumRows As Integer = GetMaximumRows(query)
        Dim distinctKeys As HashSet(Of String) = Nothing
        If query.IsDistinct Then
            distinctKeys = New HashSet(Of String)(StringComparer.Ordinal)
        End If

        For Each rowEntry As RowEntry In rows
            If resultTable.Rows.Count >= maximumRows Then Exit For

            Dim values(selectedColumns.Count - 1) As Object
            For index As Integer = 0 To selectedColumns.Count - 1
                values(index) = ToSqlText(
                    EvaluateExpression(
                        selectedColumns(index).Expression,
                        rowEntry.Row))
            Next

            If distinctKeys IsNot Nothing Then
                Dim key As String = BuildDistinctKey(values)
                If Not distinctKeys.Add(key) Then Continue For
            End If
            resultTable.Rows.Add(values)
        Next

        Return New CsvSqlResult(
            resultTable,
            matchedCount,
            resultTable.Rows.Count)
    End Function

    Public Shared Function GetColumnGuide(source As DataTable,
                                          visibleColumnCount As Integer) As String
        If source Is Nothing Then Return String.Empty

        Dim items As New List(Of String)()
        For index As Integer = 0 To Math.Min(
            visibleColumnCount,
            source.Columns.Count) - 1
            Dim caption As String = source.Columns(index).Caption
            If String.IsNullOrWhiteSpace(caption) OrElse
               String.Equals(
                   caption,
                   "C" & (index + 1).ToString(CultureInfo.InvariantCulture),
                   StringComparison.OrdinalIgnoreCase) Then
                items.Add("C" & (index + 1).ToString(CultureInfo.InvariantCulture))
            Else
                items.Add(
                    "C" & (index + 1).ToString(CultureInfo.InvariantCulture) &
                    "=" & caption)
            End If
        Next
        Return String.Join(" / ", items.ToArray())
    End Function

    Private Shared Function ResolveSelectedColumns(
        tokens As List(Of SqlToken),
        resolver As ColumnResolver) As List(Of SelectedColumn)

        Dim items As List(Of List(Of SqlToken)) = SplitByComma(tokens)
        If items.Count = 0 Then
            Throw New CsvSqlException("SELECTする列を指定してください。")
        End If

        Dim result As New List(Of SelectedColumn)()
        For Each item As List(Of SqlToken) In items
            If item.Count = 1 AndAlso item(0).Kind = TokenKind.Star Then
                If items.Count <> 1 Then
                    Throw New CsvSqlException("* は単独で指定してください。")
                End If
                For index As Integer = 0 To resolver.ColumnCount - 1
                    result.Add(
                        New SelectedColumn(
                            New ColumnExpression(index),
                            resolver.GetDefaultOutputName(index),
                            False))
                Next
                Continue For
            End If

            Dim aliasName As String = Nothing
            Dim expressionTokens As List(Of SqlToken) = item
            Dim asIndex As Integer = FindTopLevelKeyword(item, "AS")
            If asIndex >= 0 Then
                If asIndex <> item.Count - 2 OrElse
                   Not IsIdentifierToken(item(item.Count - 1)) Then
                    Throw New CsvSqlException(
                        "列別名は「列名 AS 別名」の形式で指定してください。")
                End If
                aliasName = item(item.Count - 1).Value
                expressionTokens = item.GetRange(0, asIndex)
            End If

            If IsCountExpression(expressionTokens) Then
                If items.Count <> 1 Then
                    Throw New CsvSqlException(
                        "COUNT(*) は他の列と同時に指定できません。")
                End If
                result.Add(
                    New SelectedColumn(
                        Nothing,
                        If(aliasName, "COUNT(*)"),
                        True))
                Continue For
            End If

            Dim expression As SqlExpression =
                New ExpressionParser(
                    expressionTokens,
                    resolver).ParseValueComplete()
            Dim outputName As String = aliasName
            Dim columnExpressionValue As ColumnExpression =
                TryCast(expression, ColumnExpression)
            If String.IsNullOrEmpty(outputName) Then
                If columnExpressionValue IsNot Nothing Then
                    outputName = resolver.GetDefaultOutputName(
                        columnExpressionValue.SourceIndex)
                Else
                    outputName = "式" & (result.Count + 1).ToString(
                        CultureInfo.InvariantCulture)
                End If
            End If
            result.Add(
                New SelectedColumn(
                    expression,
                    outputName,
                    False))
        Next

        Return result
    End Function

    Private Shared Function IsCountOnly(
        selectedColumns As List(Of SelectedColumn)) As Boolean
        Return selectedColumns.Count = 1 AndAlso selectedColumns(0).IsCount
    End Function

    Private Shared Function IsCountExpression(
        tokens As List(Of SqlToken)) As Boolean
        Return tokens.Count = 4 AndAlso
               tokens(0).IsKeyword("COUNT") AndAlso
               tokens(1).Kind = TokenKind.OpenParenthesis AndAlso
               tokens(2).Kind = TokenKind.Star AndAlso
               tokens(3).Kind = TokenKind.CloseParenthesis
    End Function

    Private Shared Function CreateResultTable(
        columns As List(Of SelectedColumn)) As DataTable
        Dim table As New DataTable("SqlResult")
        For Each column As SelectedColumn In columns
            Dim name As String = MakeUniqueColumnName(table, column.OutputName)
            table.Columns.Add(name, GetType(String))
        Next
        Return table
    End Function

    Private Shared Function MakeUniqueColumnName(table As DataTable,
                                                 requestedName As String) As String
        Dim baseName As String = If(requestedName, String.Empty).Trim()
        If baseName.Length = 0 Then baseName = "列"
        If Not table.Columns.Contains(baseName) Then Return baseName

        Dim suffix As Integer = 2
        While table.Columns.Contains(baseName & "_" & suffix.ToString())
            suffix += 1
        End While
        Return baseName & "_" & suffix.ToString()
    End Function

    Private Shared Function BuildAliasMap(
        columns As List(Of SelectedColumn)) As Dictionary(Of String, Integer)
        Dim result As New Dictionary(Of String, Integer)(
            StringComparer.OrdinalIgnoreCase)
        For Each column As SelectedColumn In columns
            If column.IsCount OrElse String.IsNullOrWhiteSpace(column.OutputName) Then
                Continue For
            End If
            If result.ContainsKey(column.OutputName) Then
                result(column.OutputName) = -1
            Else
                result.Add(column.OutputName, column.SourceIndex)
            End If
        Next
        Return result
    End Function

    Private Shared Function BuildExpressionAliasMap(
        columns As List(Of SelectedColumn)) As Dictionary(Of String, SqlExpression)
        Dim result As New Dictionary(Of String, SqlExpression)(
            StringComparer.OrdinalIgnoreCase)
        For Each column As SelectedColumn In columns
            If column.IsCount OrElse String.IsNullOrWhiteSpace(column.OutputName) Then
                Continue For
            End If
            If result.ContainsKey(column.OutputName) Then
                result(column.OutputName) = Nothing
            Else
                result.Add(column.OutputName, column.Expression)
            End If
        Next
        Return result
    End Function

    Private Shared Function ResolveOrderBy(
        tokens As List(Of SqlToken),
        resolver As ColumnResolver,
        aliasMap As Dictionary(Of String, SqlExpression)) As List(Of OrderSpecification)

        Dim result As New List(Of OrderSpecification)()
        If tokens.Count = 0 Then Return result

        For Each item As List(Of SqlToken) In SplitByComma(tokens)
            If item.Count = 0 Then
                Throw New CsvSqlException("ORDER BY句の式を指定してください。")
            End If

            Dim descending As Boolean = False
            If item(item.Count - 1).IsKeyword("ASC") OrElse
               item(item.Count - 1).IsKeyword("DESC") Then
                descending = item(item.Count - 1).IsKeyword("DESC")
                item = item.GetRange(0, item.Count - 1)
            End If
            If item.Count = 0 Then
                Throw New CsvSqlException("ORDER BY句の式を指定してください。")
            End If

            Dim expression As SqlExpression = Nothing
            If item.Count = 1 AndAlso IsIdentifierToken(item(0)) AndAlso
               aliasMap.ContainsKey(item(0).Value) Then
                expression = aliasMap(item(0).Value)
                If expression Is Nothing Then
                    Throw New CsvSqlException(
                        "ORDER BYの別名「" & item(0).Value & "」は重複しています。")
                End If
            Else
                expression =
                    New ExpressionParser(item, resolver).ParseValueComplete()
            End If
            result.Add(New OrderSpecification(expression, descending))
        Next
        Return result
    End Function

    Private Shared Sub ApplyOrdering(rows As List(Of RowEntry),
                                     specifications As List(Of OrderSpecification))
        If specifications.Count = 0 Then Return

        For Each row As RowEntry In rows
            ReDim row.OrderValues(specifications.Count - 1)
            For index As Integer = 0 To specifications.Count - 1
                row.OrderValues(index) =
                    EvaluateExpression(specifications(index).Expression, row.Row)
            Next
        Next

        rows.Sort(
            Function(left As RowEntry, right As RowEntry) As Integer
                For index As Integer = 0 To specifications.Count - 1
                    Dim comparison As Integer =
                        CompareSqlValues(
                            left.OrderValues(index),
                            right.OrderValues(index))
                    If specifications(index).Descending Then comparison = -comparison
                    If comparison <> 0 Then Return comparison
                Next
                Return left.OriginalIndex.CompareTo(right.OriginalIndex)
            End Function)
    End Sub

    Private Shared Function EvaluateExpression(expression As SqlExpression,
                                                row As DataRow) As Object
        Try
            Return expression.Evaluate(row)
        Catch ex As CsvSqlException
            Throw
        Catch ex As Exception
            Throw New CsvSqlException(
                "SQL式を評価できません: " & ex.Message,
                ex)
        End Try
    End Function

    Private Shared Function EvaluateCondition(expression As SqlExpression,
                                               row As DataRow) As Boolean
        Return ToSqlBoolean(EvaluateExpression(expression, row))
    End Function

    Private Shared Function ToSqlBoolean(value As Object) As Boolean
        If IsSqlNull(value) Then Return False
        If TypeOf value Is Boolean Then Return DirectCast(value, Boolean)

        Dim text As String = ToSqlText(value)
        Dim parsed As Boolean
        If Boolean.TryParse(text, parsed) Then Return parsed
        Return text.Length > 0
    End Function

    Private Shared Function IsSqlNull(value As Object) As Boolean
        Return value Is Nothing OrElse value Is DBNull.Value
    End Function

    Private Shared Function ToSqlText(value As Object) As String
        If IsSqlNull(value) Then Return String.Empty
        Return Convert.ToString(value, CultureInfo.CurrentCulture)
    End Function

    Private Shared Function CompareSqlValues(left As Object,
                                             right As Object) As Integer
        If IsSqlNull(left) Then Return If(IsSqlNull(right), 0, -1)
        If IsSqlNull(right) Then Return 1

        If IsNumericSqlValue(left) AndAlso IsNumericSqlValue(right) Then
            Dim leftNumber As Decimal =
                Convert.ToDecimal(left, CultureInfo.InvariantCulture)
            Dim rightNumber As Decimal =
                Convert.ToDecimal(right, CultureInfo.InvariantCulture)
            Return leftNumber.CompareTo(rightNumber)
        End If

        Return String.Compare(
            ToSqlText(left),
            ToSqlText(right),
            True,
            CultureInfo.CurrentCulture)
    End Function

    Private Shared Function IsNumericSqlValue(value As Object) As Boolean
        Select Case Type.GetTypeCode(value.GetType())
            Case TypeCode.Byte,
                 TypeCode.SByte,
                 TypeCode.Int16,
                 TypeCode.UInt16,
                 TypeCode.Int32,
                 TypeCode.UInt32,
                 TypeCode.Int64,
                 TypeCode.UInt64,
                 TypeCode.Single,
                 TypeCode.Double,
                 TypeCode.Decimal
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function RewriteWhere(tokens As List(Of SqlToken),
                                         resolver As ColumnResolver) As String
        Dim builder As New StringBuilder()
        For index As Integer = 0 To tokens.Count - 1
            Dim token As SqlToken = tokens(index)
            If builder.Length > 0 Then builder.Append(" ")

            If IsIdentifierToken(token) Then
                If token.Kind = TokenKind.Identifier AndAlso
                   (IsWhereKeyword(token.Value) OrElse
                    IsFunctionName(tokens, index)) Then
                    builder.Append(token.Text)
                Else
                    Dim sourceIndex As Integer = resolver.Resolve(token.Value)
                    builder.Append("[")
                    builder.Append(resolver.GetInternalName(sourceIndex))
                    builder.Append("]")
                End If
            ElseIf token.Kind = TokenKind.Operator AndAlso token.Text = "!=" Then
                builder.Append("<>")
            Else
                builder.Append(token.Text)
            End If
        Next
        Return builder.ToString()
    End Function

    Private Shared Function IsWhereKeyword(value As String) As Boolean
        Select Case value.ToUpperInvariant()
            Case "AND", "OR", "NOT", "LIKE", "IN", "IS", "NULL",
                 "TRUE", "FALSE", "BETWEEN"
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function IsFunctionName(tokens As List(Of SqlToken),
                                           index As Integer) As Boolean
        If index + 1 >= tokens.Count OrElse
           tokens(index + 1).Kind <> TokenKind.OpenParenthesis Then
            Return False
        End If

        Select Case tokens(index).Value.ToUpperInvariant()
            Case "LEN", "TRIM", "SUBSTRING", "CONVERT", "ISNULL", "IIF"
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function RewriteOrderBy(
        tokens As List(Of SqlToken),
        resolver As ColumnResolver,
        aliasMap As Dictionary(Of String, Integer)) As String

        Dim items As List(Of List(Of SqlToken)) = SplitByComma(tokens)
        Dim parts As New List(Of String)()
        For Each item As List(Of SqlToken) In items
            If item.Count < 1 OrElse item.Count > 2 OrElse
               Not IsIdentifierToken(item(0)) Then
                Throw New CsvSqlException(
                    "ORDER BY句は「列名 [ASC|DESC]」の形式で指定してください。")
            End If

            Dim sourceIndex As Integer
            If aliasMap.ContainsKey(item(0).Value) Then
                sourceIndex = aliasMap(item(0).Value)
                If sourceIndex < 0 Then
                    Throw New CsvSqlException(
                        "ORDER BYの別名「" & item(0).Value & "」は重複しています。")
                End If
            Else
                sourceIndex = resolver.Resolve(item(0).Value)
            End If

            Dim direction As String = "ASC"
            If item.Count = 2 Then
                If item(1).IsKeyword("ASC") Then
                    direction = "ASC"
                ElseIf item(1).IsKeyword("DESC") Then
                    direction = "DESC"
                Else
                    Throw New CsvSqlException(
                        "ORDER BYの並び順はASCまたはDESCを指定してください。")
                End If
            End If
            parts.Add(
                "[" & resolver.GetInternalName(sourceIndex) & "] " & direction)
        Next
        Return String.Join(", ", parts.ToArray())
    End Function

    Private Shared Function GetMaximumRows(query As ParsedQuery) As Integer
        Dim maximumRows As Integer = Integer.MaxValue
        If query.TopCount.HasValue Then maximumRows = query.TopCount.Value
        If query.LimitCount.HasValue Then
            maximumRows = Math.Min(maximumRows, query.LimitCount.Value)
        End If
        Return maximumRows
    End Function

    Private Shared Function BuildDistinctKey(values As Object()) As String
        Dim builder As New StringBuilder()
        For Each value As Object In values
            Dim text As String = Convert.ToString(value, CultureInfo.InvariantCulture)
            builder.Append(text.Length.ToString(CultureInfo.InvariantCulture))
            builder.Append(":"c)
            builder.Append(text)
            builder.Append(";"c)
        Next
        Return builder.ToString()
    End Function

    Private Shared Function SplitByComma(
        tokens As List(Of SqlToken)) As List(Of List(Of SqlToken))
        Dim result As New List(Of List(Of SqlToken))()
        Dim current As New List(Of SqlToken)()
        Dim depth As Integer = 0

        For Each token As SqlToken In tokens
            If token.Kind = TokenKind.OpenParenthesis Then depth += 1
            If token.Kind = TokenKind.CloseParenthesis Then depth -= 1
            If depth < 0 Then
                Throw New CsvSqlException("閉じ括弧が多すぎます。")
            End If

            If token.Kind = TokenKind.Comma AndAlso depth = 0 Then
                If current.Count = 0 Then
                    Throw New CsvSqlException("カンマの前に項目がありません。")
                End If
                result.Add(current)
                current = New List(Of SqlToken)()
            Else
                current.Add(token)
            End If
        Next

        If depth <> 0 Then Throw New CsvSqlException("括弧が対応していません。")
        If current.Count > 0 Then result.Add(current)
        Return result
    End Function

    Private Shared Function FindTopLevelKeyword(
        tokens As List(Of SqlToken),
        keyword As String) As Integer
        Dim depth As Integer = 0
        For index As Integer = 0 To tokens.Count - 1
            If tokens(index).Kind = TokenKind.OpenParenthesis Then depth += 1
            If tokens(index).Kind = TokenKind.CloseParenthesis Then depth -= 1
            If depth = 0 AndAlso tokens(index).IsKeyword(keyword) Then Return index
        Next
        Return -1
    End Function

    Private Shared Function IsIdentifierToken(token As SqlToken) As Boolean
        Return token.Kind = TokenKind.Identifier OrElse
               token.Kind = TokenKind.BracketIdentifier
    End Function

    Private NotInheritable Class RowEntry
        Public Sub New(row As DataRow, originalIndex As Integer)
            Me.Row = row
            Me.OriginalIndex = originalIndex
        End Sub

        Public ReadOnly Property Row As DataRow
        Public ReadOnly Property OriginalIndex As Integer
        Public Property OrderValues As Object()
    End Class

    Private NotInheritable Class OrderSpecification
        Public Sub New(expression As SqlExpression, descending As Boolean)
            Me.Expression = expression
            Me.Descending = descending
        End Sub

        Public ReadOnly Property Expression As SqlExpression
        Public ReadOnly Property Descending As Boolean
    End Class

    Private MustInherit Class SqlExpression
        Public MustOverride Function Evaluate(row As DataRow) As Object
    End Class

    Private NotInheritable Class ColumnExpression
        Inherits SqlExpression

        Public Sub New(sourceIndex As Integer)
            Me.SourceIndex = sourceIndex
        End Sub

        Public ReadOnly Property SourceIndex As Integer

        Public Overrides Function Evaluate(row As DataRow) As Object
            Return row(SourceIndex)
        End Function
    End Class

    Private NotInheritable Class LiteralExpression
        Inherits SqlExpression

        Public Sub New(value As Object)
            Me.Value = value
        End Sub

        Public ReadOnly Property Value As Object

        Public Overrides Function Evaluate(row As DataRow) As Object
            Return Value
        End Function
    End Class

    Private NotInheritable Class LogicalExpression
        Inherits SqlExpression

        Public Sub New(left As SqlExpression,
                       right As SqlExpression,
                       isAnd As Boolean)
            Me.Left = left
            Me.Right = right
            Me.IsAnd = isAnd
        End Sub

        Public ReadOnly Property Left As SqlExpression
        Public ReadOnly Property Right As SqlExpression
        Public ReadOnly Property IsAnd As Boolean

        Public Overrides Function Evaluate(row As DataRow) As Object
            Dim leftValue As Boolean = ToSqlBoolean(Left.Evaluate(row))
            If IsAnd Then
                If Not leftValue Then Return False
                Return ToSqlBoolean(Right.Evaluate(row))
            End If
            If leftValue Then Return True
            Return ToSqlBoolean(Right.Evaluate(row))
        End Function
    End Class

    Private NotInheritable Class NotExpression
        Inherits SqlExpression

        Public Sub New(operand As SqlExpression)
            Me.Operand = operand
        End Sub

        Public ReadOnly Property Operand As SqlExpression

        Public Overrides Function Evaluate(row As DataRow) As Object
            Return Not ToSqlBoolean(Operand.Evaluate(row))
        End Function
    End Class

    Private NotInheritable Class ComparisonExpression
        Inherits SqlExpression

        Public Sub New(left As SqlExpression,
                       right As SqlExpression,
                       comparisonOperator As String)
            Me.Left = left
            Me.Right = right
            Me.ComparisonOperator = comparisonOperator
        End Sub

        Public ReadOnly Property Left As SqlExpression
        Public ReadOnly Property Right As SqlExpression
        Public ReadOnly Property ComparisonOperator As String

        Public Overrides Function Evaluate(row As DataRow) As Object
            Dim comparison As Integer =
                CompareSqlValues(Left.Evaluate(row), Right.Evaluate(row))
            Select Case ComparisonOperator
                Case "="
                    Return comparison = 0
                Case "<>", "!="
                    Return comparison <> 0
                Case "<"
                    Return comparison < 0
                Case "<="
                    Return comparison <= 0
                Case ">"
                    Return comparison > 0
                Case ">="
                    Return comparison >= 0
                Case Else
                    Throw New CsvSqlException(
                        "比較演算子「" & ComparisonOperator & "」は使用できません。")
            End Select
        End Function
    End Class

    Private NotInheritable Class LikeExpression
        Inherits SqlExpression

        Public Sub New(valueExpression As SqlExpression,
                       patternExpression As SqlExpression,
                       negate As Boolean)
            Me.ValueExpression = valueExpression
            Me.PatternExpression = patternExpression
            Me.Negate = negate
        End Sub

        Public ReadOnly Property ValueExpression As SqlExpression
        Public ReadOnly Property PatternExpression As SqlExpression
        Public ReadOnly Property Negate As Boolean

        Public Overrides Function Evaluate(row As DataRow) As Object
            Dim value As String = ToSqlText(ValueExpression.Evaluate(row))
            Dim pattern As String = ToSqlText(PatternExpression.Evaluate(row))
            Dim regexPattern As String =
                "^" & Regex.Escape(pattern).
                    Replace("%", ".*").
                    Replace("_", ".") & "$"
            Dim matched As Boolean =
                Regex.IsMatch(
                    value,
                    regexPattern,
                    RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            Return If(Negate, Not matched, matched)
        End Function
    End Class

    Private NotInheritable Class InExpression
        Inherits SqlExpression

        Public Sub New(valueExpression As SqlExpression,
                       candidates As List(Of SqlExpression),
                       negate As Boolean)
            Me.ValueExpression = valueExpression
            Me.Candidates = candidates
            Me.Negate = negate
        End Sub

        Public ReadOnly Property ValueExpression As SqlExpression
        Public ReadOnly Property Candidates As List(Of SqlExpression)
        Public ReadOnly Property Negate As Boolean

        Public Overrides Function Evaluate(row As DataRow) As Object
            Dim value As Object = ValueExpression.Evaluate(row)
            Dim matched As Boolean = False
            For Each candidate As SqlExpression In Candidates
                If CompareSqlValues(value, candidate.Evaluate(row)) = 0 Then
                    matched = True
                    Exit For
                End If
            Next
            Return If(Negate, Not matched, matched)
        End Function
    End Class

    Private NotInheritable Class IsNullExpression
        Inherits SqlExpression

        Public Sub New(operand As SqlExpression, negate As Boolean)
            Me.Operand = operand
            Me.Negate = negate
        End Sub

        Public ReadOnly Property Operand As SqlExpression
        Public ReadOnly Property Negate As Boolean

        Public Overrides Function Evaluate(row As DataRow) As Object
            Dim result As Boolean = IsSqlNull(Operand.Evaluate(row))
            Return If(Negate, Not result, result)
        End Function
    End Class

    Private NotInheritable Class FunctionExpression
        Inherits SqlExpression

        Public Sub New(name As String, arguments As List(Of SqlExpression))
            Me.Name = name.ToUpperInvariant()
            Me.Arguments = arguments
            ValidateArgumentCount()
        End Sub

        Public ReadOnly Property Name As String
        Public ReadOnly Property Arguments As List(Of SqlExpression)

        Public Overrides Function Evaluate(row As DataRow) As Object
            Select Case Name
                Case "LTRIM"
                    Return TrimValue(row, True)
                Case "RTRIM"
                    Return TrimValue(row, False)
                Case "TRIM"
                    Return ToSqlText(Arguments(0).Evaluate(row)).Trim()
                Case "CONCAT"
                    Dim builder As New StringBuilder()
                    For Each argument As SqlExpression In Arguments
                        builder.Append(ToSqlText(argument.Evaluate(row)))
                    Next
                    Return builder.ToString()
                Case "TO_CHAR"
                    Return FormatToChar(row)
                Case "TO_NUMBER"
                    Return ConvertToNumber(row)
                Case "LPAD"
                    Return PadValue(row, True)
                Case "RPAD"
                    Return PadValue(row, False)
                Case "LEN"
                    Return ToSqlText(Arguments(0).Evaluate(row)).Length
                Case "SUBSTRING"
                    Return SubstringValue(row)
                Case "CONVERT"
                    Return ToSqlText(Arguments(0).Evaluate(row))
                Case "ISNULL"
                    Dim value As Object = Arguments(0).Evaluate(row)
                    Return If(IsSqlNull(value), Arguments(1).Evaluate(row), value)
                Case "IIF"
                    If ToSqlBoolean(Arguments(0).Evaluate(row)) Then
                        Return Arguments(1).Evaluate(row)
                    End If
                    Return Arguments(2).Evaluate(row)
                Case Else
                    Throw New CsvSqlException(
                        "関数「" & Name & "」は使用できません。")
            End Select
        End Function

        Private Sub ValidateArgumentCount()
            Dim minimum As Integer
            Dim maximum As Integer
            Select Case Name
                Case "LTRIM", "RTRIM", "TO_CHAR", "TO_NUMBER"
                    minimum = 1
                    maximum = 2
                Case "LPAD", "RPAD"
                    minimum = 2
                    maximum = 3
                Case "CONCAT"
                    minimum = 2
                    maximum = Integer.MaxValue
                Case "TRIM", "LEN"
                    minimum = 1
                    maximum = 1
                Case "SUBSTRING", "IIF"
                    minimum = 3
                    maximum = 3
                Case "CONVERT", "ISNULL"
                    minimum = 2
                    maximum = 2
                Case Else
                    Throw New CsvSqlException(
                        "関数「" & Name & "」は使用できません。")
            End Select

            If Arguments.Count < minimum OrElse Arguments.Count > maximum Then
                Throw New CsvSqlException(
                    String.Format(
                        CultureInfo.CurrentCulture,
                        "関数{0}の引数の数が正しくありません。",
                        Name))
            End If
        End Sub

        Private Function TrimValue(row As DataRow, trimStart As Boolean) As String
            Dim value As String = ToSqlText(Arguments(0).Evaluate(row))
            If Arguments.Count = 1 Then
                Return If(trimStart, value.TrimStart(), value.TrimEnd())
            End If

            Dim characters As String = ToSqlText(Arguments(1).Evaluate(row))
            If characters.Length = 0 Then Return value
            Return If(trimStart,
                      value.TrimStart(characters.ToCharArray()),
                      value.TrimEnd(characters.ToCharArray()))
        End Function

        Private Function PadValue(row As DataRow, padLeft As Boolean) As String
            Dim value As String = ToSqlText(Arguments(0).Evaluate(row))
            Dim width As Integer = ParseWidth(Arguments(1).Evaluate(row))
            If width <= 0 Then Return String.Empty
            If value.Length >= width Then Return value.Substring(0, width)

            Dim padding As String = " "
            If Arguments.Count = 3 Then
                padding = ToSqlText(Arguments(2).Evaluate(row))
            End If
            If padding.Length = 0 Then
                Throw New CsvSqlException("LPAD/RPADの埋め文字は空にできません。")
            End If

            Dim needed As Integer = width - value.Length
            Dim repeated As New StringBuilder()
            While repeated.Length < needed
                repeated.Append(padding)
            End While
            Dim padText As String = repeated.ToString(0, needed)
            Return If(padLeft, padText & value, value & padText)
        End Function

        Private Function SubstringValue(row As DataRow) As String
            Dim value As String = ToSqlText(Arguments(0).Evaluate(row))
            Dim startIndex As Integer = ParseWidth(Arguments(1).Evaluate(row)) - 1
            Dim length As Integer = ParseWidth(Arguments(2).Evaluate(row))
            If startIndex < 0 OrElse length <= 0 OrElse startIndex >= value.Length Then
                Return String.Empty
            End If
            Return value.Substring(startIndex, Math.Min(length, value.Length - startIndex))
        End Function

        Private Function FormatToChar(row As DataRow) As String
            Dim text As String = ToSqlText(Arguments(0).Evaluate(row))
            If Arguments.Count = 1 Then Return text

            Dim format As String = ToSqlText(Arguments(1).Evaluate(row))
            If format.Length = 0 Then Return text

            If Regex.IsMatch(format, "Y|D|HH24|MI|SS", RegexOptions.IgnoreCase) Then
                Dim dateValue As DateTime
                If DateTime.TryParse(
                    text,
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.None,
                    dateValue) OrElse
                   DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    dateValue) Then
                    Return dateValue.ToString(
                        ConvertDateFormat(format),
                        CultureInfo.CurrentCulture)
                End If
            End If

            Dim numericValue As Decimal
            If Decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                numericValue) OrElse
               Decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                numericValue) Then
                Try
                    Return numericValue.ToString(format, CultureInfo.CurrentCulture)
                Catch ex As FormatException
                    Throw New CsvSqlException(
                        "TO_CHARの書式「" & format & "」を解釈できません。",
                        ex)
                End Try
            End If
            Return text
        End Function

        Private Function ConvertToNumber(row As DataRow) As Object
            Dim value As Object = Arguments(0).Evaluate(row)
            If IsSqlNull(value) Then Return Nothing

            Dim text As String = ToSqlText(value).Trim()
            Dim format As String = String.Empty
            If Arguments.Count = 2 Then
                format = ToSqlText(Arguments(1).Evaluate(row)).Trim()
            End If

            Dim numericValue As Decimal
            If TryParseNumber(text, format, numericValue) Then
                Return numericValue
            End If

            Throw New CsvSqlException(
                "TO_NUMBERで数値に変換できません: " & text)
        End Function

        Private Shared Function TryParseNumber(text As String,
                                               format As String,
                                               ByRef result As Decimal) As Boolean
            If Decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                result) OrElse
               Decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                result) Then
                Return True
            End If

            If String.IsNullOrWhiteSpace(format) Then Return False

            Dim upperFormat As String = format.ToUpperInvariant()
            If upperFormat.IndexOf("D"c) < 0 AndAlso
               upperFormat.IndexOf("G"c) < 0 Then
                Return False
            End If

            Dim normalized As String = text.Trim()
            Dim decimalPosition As Integer = -1
            If upperFormat.IndexOf("D"c) >= 0 Then
                decimalPosition = Math.Max(
                    normalized.LastIndexOf("."c),
                    normalized.LastIndexOf(","c))
            End If

            Dim builder As New StringBuilder()
            For index As Integer = 0 To normalized.Length - 1
                Dim character As Char = normalized(index)
                If Char.IsDigit(character) OrElse
                   ((character = "+"c OrElse character = "-"c) AndAlso
                    builder.Length = 0) Then
                    builder.Append(character)
                ElseIf index = decimalPosition Then
                    builder.Append("."c)
                End If
            Next

            Return Decimal.TryParse(
                builder.ToString(),
                NumberStyles.Number Or NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                result)
        End Function

        Private Shared Function ConvertDateFormat(format As String) As String
            Dim result As String = format
            result = Regex.Replace(result, "HH24", "HH", RegexOptions.IgnoreCase)
            result = Regex.Replace(result, "YYYY", "yyyy", RegexOptions.IgnoreCase)
            result = Regex.Replace(result, "YY", "yy", RegexOptions.IgnoreCase)
            result = Regex.Replace(result, "DD", "dd", RegexOptions.IgnoreCase)
            result = Regex.Replace(result, "MI", "mm", RegexOptions.IgnoreCase)
            result = Regex.Replace(result, "SS", "ss", RegexOptions.IgnoreCase)
            Return result
        End Function

        Private Shared Function ParseWidth(value As Object) As Integer
            Dim result As Integer
            If Not Integer.TryParse(
                ToSqlText(value),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                result) Then
                Throw New CsvSqlException("文字数には整数を指定してください。")
            End If
            Return result
        End Function
    End Class

    Private NotInheritable Class CaseBranch
        Public Sub New(condition As SqlExpression, result As SqlExpression)
            Me.Condition = condition
            Me.Result = result
        End Sub

        Public ReadOnly Property Condition As SqlExpression
        Public ReadOnly Property Result As SqlExpression
    End Class

    Private NotInheritable Class CaseExpression
        Inherits SqlExpression

        Public Sub New(branches As List(Of CaseBranch),
                       elseExpression As SqlExpression)
            Me.Branches = branches
            Me.ElseExpression = elseExpression
        End Sub

        Public ReadOnly Property Branches As List(Of CaseBranch)
        Public ReadOnly Property ElseExpression As SqlExpression

        Public Overrides Function Evaluate(row As DataRow) As Object
            For Each branch As CaseBranch In Branches
                If ToSqlBoolean(branch.Condition.Evaluate(row)) Then
                    Return branch.Result.Evaluate(row)
                End If
            Next
            If ElseExpression Is Nothing Then Return Nothing
            Return ElseExpression.Evaluate(row)
        End Function
    End Class

    Private NotInheritable Class SelectedColumn
        Public Sub New(expression As SqlExpression,
                       outputName As String,
                       isCount As Boolean)
            Me.Expression = expression
            Me.OutputName = outputName
            Me.IsCount = isCount
        End Sub

        Public ReadOnly Property Expression As SqlExpression
        Public ReadOnly Property SourceIndex As Integer
            Get
                Dim columnExpressionValue As ColumnExpression =
                    TryCast(Expression, ColumnExpression)
                If columnExpressionValue Is Nothing Then Return -1
                Return columnExpressionValue.SourceIndex
            End Get
        End Property
        Public ReadOnly Property OutputName As String
        Public ReadOnly Property IsCount As Boolean
    End Class

    Private NotInheritable Class ColumnResolver
        Private ReadOnly _source As DataTable
        Private ReadOnly _columnCount As Integer
        Private ReadOnly _names As Dictionary(Of String, Integer)

        Public Sub New(source As DataTable, columnCount As Integer)
            _source = source
            _columnCount = columnCount
            _names = New Dictionary(Of String, Integer)(
                StringComparer.OrdinalIgnoreCase)

            For index As Integer = 0 To columnCount - 1
                AddName("C" & (index + 1).ToString(CultureInfo.InvariantCulture), index)
                AddName(source.Columns(index).ColumnName, index)
                If Not String.IsNullOrWhiteSpace(source.Columns(index).Caption) Then
                    AddName(source.Columns(index).Caption, index)
                End If
            Next
        End Sub

        Public ReadOnly Property ColumnCount As Integer
            Get
                Return _columnCount
            End Get
        End Property

        Public Function Resolve(name As String) As Integer
            Dim normalized As String = name
            Dim dotIndex As Integer = normalized.IndexOf("."c)
            If dotIndex >= 0 Then
                If Not String.Equals(
                    normalized.Substring(0, dotIndex),
                    "csv",
                    StringComparison.OrdinalIgnoreCase) Then
                    Throw New CsvSqlException(
                        "テーブル名「" & normalized.Substring(0, dotIndex) &
                        "」は使用できません。")
                End If
                normalized = normalized.Substring(dotIndex + 1)
            End If

            If Not _names.ContainsKey(normalized) Then
                Throw New CsvSqlException(
                    "列「" & normalized & "」が見つかりません。" &
                    " C1、C2…またはヘッダー名を指定してください。")
            End If
            If _names(normalized) < 0 Then
                Throw New CsvSqlException(
                    "列名「" & normalized & "」は重複しています。" &
                    " C1、C2…で指定してください。")
            End If
            Return _names(normalized)
        End Function

        Public Function GetInternalName(index As Integer) As String
            Return _source.Columns(index).ColumnName
        End Function

        Public Function GetDefaultOutputName(index As Integer) As String
            Dim caption As String = _source.Columns(index).Caption
            If String.IsNullOrWhiteSpace(caption) Then
                Return "C" & (index + 1).ToString(CultureInfo.InvariantCulture)
            End If
            Return caption
        End Function

        Private Sub AddName(name As String, index As Integer)
            If _names.ContainsKey(name) Then
                If _names(name) <> index Then _names(name) = -1
            Else
                _names.Add(name, index)
            End If
        End Sub
    End Class

    Private NotInheritable Class ExpressionParser
        Private ReadOnly _tokens As List(Of SqlToken)
        Private ReadOnly _resolver As ColumnResolver
        Private _index As Integer

        Public Sub New(tokens As List(Of SqlToken), resolver As ColumnResolver)
            _tokens = tokens
            _resolver = resolver
        End Sub

        Public Function ParseValueComplete() As SqlExpression
            Dim result As SqlExpression = ParseValue()
            EnsureComplete()
            Return result
        End Function

        Public Function ParseConditionComplete() As SqlExpression
            Dim result As SqlExpression = ParseOrExpression()
            EnsureComplete()
            Return result
        End Function

        Private Function ParseOrExpression() As SqlExpression
            Dim left As SqlExpression = ParseAndExpression()
            While MatchKeyword("OR")
                left = New LogicalExpression(left, ParseAndExpression(), False)
            End While
            Return left
        End Function

        Private Function ParseAndExpression() As SqlExpression
            Dim left As SqlExpression = ParseNotExpression()
            While MatchKeyword("AND")
                left = New LogicalExpression(left, ParseNotExpression(), True)
            End While
            Return left
        End Function

        Private Function ParseNotExpression() As SqlExpression
            If MatchKeyword("NOT") Then
                Return New NotExpression(ParseNotExpression())
            End If
            Return ParsePredicate()
        End Function

        Private Function ParsePredicate() As SqlExpression
            If Match(TokenKind.OpenParenthesis) Then
                Dim nested As SqlExpression = ParseOrExpression()
                Expect(TokenKind.CloseParenthesis, ")")
                Return nested
            End If

            Dim left As SqlExpression = ParseValue()
            If MatchKeyword("IS") Then
                Dim negate As Boolean = MatchKeyword("NOT")
                ExpectKeyword("NULL")
                Return New IsNullExpression(left, negate)
            End If

            Dim predicateNegated As Boolean = False
            If MatchKeyword("NOT") Then predicateNegated = True

            If MatchKeyword("LIKE") Then
                Return New LikeExpression(left, ParseValue(), predicateNegated)
            End If
            If MatchKeyword("IN") Then
                Return ParseInExpression(left, predicateNegated)
            End If
            If predicateNegated Then
                Throw New CsvSqlException("NOTの後にはLIKEまたはINを指定してください。")
            End If

            If HasMore AndAlso Current.Kind = TokenKind.Operator Then
                Dim comparisonOperator As String = Current.Text
                _index += 1
                Return New ComparisonExpression(
                    left,
                    ParseValue(),
                    comparisonOperator)
            End If
            Return left
        End Function

        Private Function ParseInExpression(left As SqlExpression,
                                           negate As Boolean) As SqlExpression
            Expect(TokenKind.OpenParenthesis, "(")
            Dim candidates As New List(Of SqlExpression)()
            If Match(TokenKind.CloseParenthesis) Then
                Throw New CsvSqlException("INには1つ以上の値を指定してください。")
            End If

            Do
                candidates.Add(ParseValue())
                If Match(TokenKind.CloseParenthesis) Then Exit Do
                Expect(TokenKind.Comma, ",")
            Loop
            Return New InExpression(left, candidates, negate)
        End Function

        Private Function ParseValue() As SqlExpression
            If Not HasMore Then
                Throw New CsvSqlException("SQL式が途中で終了しています。")
            End If

            If Current.IsKeyword("CASE") Then Return ParseCaseExpression()

            If Current.Kind = TokenKind.StringLiteral Then
                Dim text As String = Current.Text
                _index += 1
                Return New LiteralExpression(
                    text.Substring(1, text.Length - 2).Replace("''", "'"))
            End If

            If Current.Kind = TokenKind.Number Then
                Dim value As Decimal
                If Not Decimal.TryParse(
                    Current.Value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    value) Then
                    Throw New CsvSqlException(
                        "数値リテラルが大きすぎます: " & Current.Text)
                End If
                _index += 1
                Return New LiteralExpression(value)
            End If

            If Current.IsKeyword("TRUE") Then
                _index += 1
                Return New LiteralExpression(True)
            End If
            If Current.IsKeyword("FALSE") Then
                _index += 1
                Return New LiteralExpression(False)
            End If
            If Current.IsKeyword("NULL") Then
                _index += 1
                Return New LiteralExpression(Nothing)
            End If

            If IsIdentifierToken(Current) Then
                Dim token As SqlToken = Current
                _index += 1
                If token.Kind = TokenKind.Identifier AndAlso
                   HasMore AndAlso
                   Current.Kind = TokenKind.OpenParenthesis Then
                    Return ParseFunction(token.Value)
                End If
                Return New ColumnExpression(_resolver.Resolve(token.Value))
            End If

            If Match(TokenKind.OpenParenthesis) Then
                Dim nested As SqlExpression = ParseValue()
                Expect(TokenKind.CloseParenthesis, ")")
                Return nested
            End If

            Throw New CsvSqlException(
                "SQL式として解釈できません: " & Current.Text)
        End Function

        Private Function ParseFunction(name As String) As SqlExpression
            Expect(TokenKind.OpenParenthesis, "(")
            Dim arguments As New List(Of SqlExpression)()

            If String.Equals(name, "IIF", StringComparison.OrdinalIgnoreCase) Then
                arguments.Add(ParseOrExpression())
                Expect(TokenKind.Comma, ",")
                arguments.Add(ParseValue())
                Expect(TokenKind.Comma, ",")
                arguments.Add(ParseValue())
                Expect(TokenKind.CloseParenthesis, ")")
                Return New FunctionExpression(name, arguments)
            End If

            If Match(TokenKind.CloseParenthesis) Then
                Return New FunctionExpression(name, arguments)
            End If

            Do
                arguments.Add(ParseValue())
                If Match(TokenKind.CloseParenthesis) Then Exit Do
                Expect(TokenKind.Comma, ",")
            Loop
            Return New FunctionExpression(name, arguments)
        End Function

        Private Function ParseCaseExpression() As SqlExpression
            ExpectKeyword("CASE")
            Dim branches As New List(Of CaseBranch)()
            While MatchKeyword("WHEN")
                Dim condition As SqlExpression = ParseOrExpression()
                ExpectKeyword("THEN")
                Dim result As SqlExpression = ParseValue()
                branches.Add(New CaseBranch(condition, result))
            End While
            If branches.Count = 0 Then
                Throw New CsvSqlException("CASEにはWHEN句が必要です。")
            End If

            Dim elseExpression As SqlExpression = Nothing
            If MatchKeyword("ELSE") Then elseExpression = ParseValue()
            ExpectKeyword("END")
            Return New CaseExpression(branches, elseExpression)
        End Function

        Private Sub EnsureComplete()
            If HasMore Then
                Throw New CsvSqlException(
                    "SQL式の末尾に解釈できない内容があります: " & Current.Text)
            End If
        End Sub

        Private Sub ExpectKeyword(keyword As String)
            If Not MatchKeyword(keyword) Then
                Throw New CsvSqlException(keyword & "が必要です。")
            End If
        End Sub

        Private Function MatchKeyword(keyword As String) As Boolean
            If HasMore AndAlso Current.IsKeyword(keyword) Then
                _index += 1
                Return True
            End If
            Return False
        End Function

        Private Sub Expect(kind As TokenKind, displayText As String)
            If Not Match(kind) Then
                Throw New CsvSqlException(displayText & "が必要です。")
            End If
        End Sub

        Private Function Match(kind As TokenKind) As Boolean
            If HasMore AndAlso Current.Kind = kind Then
                _index += 1
                Return True
            End If
            Return False
        End Function

        Private ReadOnly Property HasMore As Boolean
            Get
                Return _index < _tokens.Count
            End Get
        End Property

        Private ReadOnly Property Current As SqlToken
            Get
                Return _tokens(_index)
            End Get
        End Property
    End Class

    Private Enum TokenKind
        Identifier
        BracketIdentifier
        StringLiteral
        Number
        Comma
        Star
        OpenParenthesis
        CloseParenthesis
        [Operator]
        Semicolon
    End Enum

    Private NotInheritable Class SqlToken
        Public Sub New(kind As TokenKind, text As String, value As String)
            Me.Kind = kind
            Me.Text = text
            Me.Value = value
        End Sub

        Public ReadOnly Property Kind As TokenKind
        Public ReadOnly Property Text As String
        Public ReadOnly Property Value As String

        Public Function IsKeyword(keyword As String) As Boolean
            Return Kind = TokenKind.Identifier AndAlso
                   String.Equals(Value, keyword, StringComparison.OrdinalIgnoreCase)
        End Function
    End Class

    Private NotInheritable Class ParsedQuery
        Public Property IsDistinct As Boolean
        Public Property TopCount As Integer?
        Public Property LimitCount As Integer?
        Public Property SelectTokens As List(Of SqlToken)
        Public Property WhereTokens As List(Of SqlToken)
        Public Property OrderTokens As List(Of SqlToken)
    End Class

    Private NotInheritable Class SqlParser
        Private ReadOnly _tokens As List(Of SqlToken)
        Private _index As Integer

        Private Sub New(sql As String)
            _tokens = Tokenize(sql)
        End Sub

        Public Shared Function Parse(sql As String) As ParsedQuery
            Return New SqlParser(sql).ParseQuery()
        End Function

        Private Function ParseQuery() As ParsedQuery
            ExpectKeyword("SELECT")
            Dim query As New ParsedQuery() With {
                .SelectTokens = New List(Of SqlToken)(),
                .WhereTokens = New List(Of SqlToken)(),
                .OrderTokens = New List(Of SqlToken)()
            }

            If MatchKeyword("DISTINCT") Then query.IsDistinct = True
            If MatchKeyword("TOP") Then query.TopCount = ReadNonNegativeInteger("TOP")

            query.SelectTokens = ReadUntilKeyword("FROM")
            If query.SelectTokens.Count = 0 Then
                Throw New CsvSqlException("SELECTする列を指定してください。")
            End If
            ExpectKeyword("FROM")

            Dim tableToken As SqlToken = ReadToken()
            If Not IsIdentifierToken(tableToken) OrElse
               Not String.Equals(
                   tableToken.Value,
                   "csv",
                   StringComparison.OrdinalIgnoreCase) Then
                Throw New CsvSqlException("FROM句にはテーブル名 csv を指定してください。")
            End If

            If MatchKeyword("WHERE") Then
                query.WhereTokens = ReadUntilClause("ORDER", "LIMIT")
                If query.WhereTokens.Count = 0 Then
                    Throw New CsvSqlException("WHERE句の条件を指定してください。")
                End If
            End If

            If MatchKeyword("ORDER") Then
                ExpectKeyword("BY")
                query.OrderTokens = ReadUntilClause("LIMIT")
                If query.OrderTokens.Count = 0 Then
                    Throw New CsvSqlException("ORDER BY句の列を指定してください。")
                End If
            End If

            If MatchKeyword("LIMIT") Then
                query.LimitCount = ReadNonNegativeInteger("LIMIT")
            End If

            If HasMore AndAlso Current.Kind = TokenKind.Semicolon Then _index += 1
            If HasMore Then
                Throw New CsvSqlException(
                    "SQLの末尾に解釈できない内容があります: " & Current.Text)
            End If
            Return query
        End Function

        Private Function ReadUntilKeyword(keyword As String) As List(Of SqlToken)
            Dim result As New List(Of SqlToken)()
            Dim depth As Integer = 0
            While HasMore
                If depth = 0 AndAlso Current.IsKeyword(keyword) Then Exit While
                If Current.Kind = TokenKind.OpenParenthesis Then depth += 1
                If Current.Kind = TokenKind.CloseParenthesis Then depth -= 1
                result.Add(Current)
                _index += 1
            End While
            Return result
        End Function

        Private Function ReadUntilClause(
            ParamArray clauseKeywords As String()) As List(Of SqlToken)
            Dim result As New List(Of SqlToken)()
            Dim depth As Integer = 0
            While HasMore
                If Current.Kind = TokenKind.Semicolon AndAlso depth = 0 Then Exit While
                If depth = 0 Then
                    For Each keyword As String In clauseKeywords
                        If Current.IsKeyword(keyword) Then Return result
                    Next
                End If
                If Current.Kind = TokenKind.OpenParenthesis Then depth += 1
                If Current.Kind = TokenKind.CloseParenthesis Then depth -= 1
                result.Add(Current)
                _index += 1
            End While
            Return result
        End Function

        Private Function ReadNonNegativeInteger(label As String) As Integer
            If Not HasMore OrElse Current.Kind <> TokenKind.Number Then
                Throw New CsvSqlException(label & "には0以上の整数を指定してください。")
            End If
            Dim value As Integer
            If Not Integer.TryParse(
                Current.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                value) Then
                Throw New CsvSqlException(label & "の値が大きすぎます。")
            End If
            _index += 1
            Return value
        End Function

        Private Sub ExpectKeyword(keyword As String)
            If Not MatchKeyword(keyword) Then
                Throw New CsvSqlException(keyword & "が必要です。")
            End If
        End Sub

        Private Function MatchKeyword(keyword As String) As Boolean
            If HasMore AndAlso Current.IsKeyword(keyword) Then
                _index += 1
                Return True
            End If
            Return False
        End Function

        Private Function ReadToken() As SqlToken
            If Not HasMore Then Throw New CsvSqlException("SQLが途中で終了しています。")
            Dim result As SqlToken = Current
            _index += 1
            Return result
        End Function

        Private ReadOnly Property HasMore As Boolean
            Get
                Return _index < _tokens.Count
            End Get
        End Property

        Private ReadOnly Property Current As SqlToken
            Get
                Return _tokens(_index)
            End Get
        End Property

        Private Shared Function Tokenize(sql As String) As List(Of SqlToken)
            Dim tokens As New List(Of SqlToken)()
            Dim index As Integer = 0

            While index < sql.Length
                Dim character As Char = sql(index)
                If Char.IsWhiteSpace(character) Then
                    index += 1
                    Continue While
                End If

                If character = "'"c Then
                    Dim start As Integer = index
                    index += 1
                    Dim closed As Boolean = False
                    While index < sql.Length
                        If sql(index) = "'"c Then
                            If index + 1 < sql.Length AndAlso sql(index + 1) = "'"c Then
                                index += 2
                            Else
                                index += 1
                                closed = True
                                Exit While
                            End If
                        Else
                            index += 1
                        End If
                    End While
                    If Not closed Then
                        Throw New CsvSqlException("文字列リテラルが閉じられていません。")
                    End If
                    Dim text As String = sql.Substring(start, index - start)
                    tokens.Add(New SqlToken(TokenKind.StringLiteral, text, text))
                    Continue While
                End If

                If character = "["c Then
                    Dim start As Integer = index
                    index += 1
                    Dim value As New StringBuilder()
                    Dim closed As Boolean = False
                    While index < sql.Length
                        If sql(index) = "]"c Then
                            If index + 1 < sql.Length AndAlso sql(index + 1) = "]"c Then
                                value.Append("]"c)
                                index += 2
                            Else
                                index += 1
                                closed = True
                                Exit While
                            End If
                        Else
                            value.Append(sql(index))
                            index += 1
                        End If
                    End While
                    If Not closed Then
                        Throw New CsvSqlException("列名の ] がありません。")
                    End If
                    tokens.Add(
                        New SqlToken(
                            TokenKind.BracketIdentifier,
                            sql.Substring(start, index - start),
                            value.ToString()))
                    Continue While
                End If

                If Char.IsLetter(character) OrElse character = "_"c Then
                    Dim start As Integer = index
                    index += 1
                    While index < sql.Length AndAlso _
                          (Char.IsLetterOrDigit(sql(index)) OrElse _
                           sql(index) = "_"c OrElse _
                           AscW(sql(index)) = &H2E)
                        index += 1
                    End While
                    Dim text As String = sql.Substring(start, index - start)
                    tokens.Add(New SqlToken(TokenKind.Identifier, text, text))
                    Continue While
                End If

                Dim beginsNumber As Boolean =
                    Char.IsDigit(character) OrElse
                    ((character = "+"c OrElse character = "-"c) AndAlso
                     index + 1 < sql.Length AndAlso
                     (Char.IsDigit(sql(index + 1)) OrElse
                      sql(index + 1) = "."c)) OrElse
                    (character = "."c AndAlso
                     index + 1 < sql.Length AndAlso
                     Char.IsDigit(sql(index + 1)))
                If beginsNumber Then
                    Dim start As Integer = index
                    If character = "+"c OrElse character = "-"c Then index += 1
                    While index < sql.Length AndAlso Char.IsDigit(sql(index))
                        index += 1
                    End While
                    If index < sql.Length AndAlso sql(index) = "."c Then
                        index += 1
                        While index < sql.Length AndAlso Char.IsDigit(sql(index))
                            index += 1
                        End While
                    End If
                    Dim text As String = sql.Substring(start, index - start)
                    tokens.Add(New SqlToken(TokenKind.Number, text, text))
                    Continue While
                End If

                Select Case character
                    Case ","c
                        tokens.Add(New SqlToken(TokenKind.Comma, ",", ","))
                        index += 1
                    Case "*"c
                        tokens.Add(New SqlToken(TokenKind.Star, "*", "*"))
                        index += 1
                    Case "("c
                        tokens.Add(New SqlToken(TokenKind.OpenParenthesis, "(", "("))
                        index += 1
                    Case ")"c
                        tokens.Add(New SqlToken(TokenKind.CloseParenthesis, ")", ")"))
                        index += 1
                    Case ";"c
                        tokens.Add(New SqlToken(TokenKind.Semicolon, ";", ";"))
                        index += 1
                    Case "="c, "<"c, ">"c, "!"c
                        Dim text As String = character.ToString()
                        If index + 1 < sql.Length Then
                            Dim pair As String = sql.Substring(index, 2)
                            If pair = "<=" OrElse pair = ">=" OrElse
                               pair = "<>" OrElse pair = "!=" Then
                                text = pair
                            End If
                        End If
                        tokens.Add(New SqlToken(TokenKind.Operator, text, text))
                        index += text.Length
                    Case Else
                        Throw New CsvSqlException(
                            "SQLに使用できない文字があります: " & character)
                End Select
            End While

            Return tokens
        End Function
    End Class
End Class
