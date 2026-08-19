Imports System.Drawing
Imports System.IO
Imports System.Reflection

Public NotInheritable Class AppIcon
    Private Const ResourceName As String =
        "CsvPreviewer.Assets.CSViewer.ico"

    Private Sub New()
    End Sub

    Public Shared Function Create() As Icon
        Dim assembly As Assembly = Assembly.GetExecutingAssembly()
        Using stream As Stream = assembly.GetManifestResourceStream(ResourceName)
            If stream Is Nothing Then Return Nothing

            Using sourceIcon As New Icon(stream)
                Return DirectCast(sourceIcon.Clone(), Icon)
            End Using
        End Using
    End Function
End Class
