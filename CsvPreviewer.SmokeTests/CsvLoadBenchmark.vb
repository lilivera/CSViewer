Imports System
Imports System.Data
Imports System.Diagnostics
Imports System.IO
Imports System.Reflection
Imports System.Text
Imports CsvPreviewer

Friend Module CsvLoadBenchmark
    ' Also runs against v1.0.1's CSViewer.exe for a like-for-like comparison.
    Public Sub Run()
        Const rowCount As Integer = 100000
        Const columnCount As Integer = 30
        Dim path As String = System.IO.Path.GetTempFileName()
        Try
            Using writer As New StreamWriter(path, False, New UTF8Encoding(False), 65536)
                Dim cells(columnCount - 1) As String
                For column As Integer = 0 To columnCount - 1
                    cells(column) = "column" & column
                Next
                writer.WriteLine(String.Join(",", cells))
                For row As Integer = 1 To rowCount
                    For column As Integer = 0 To columnCount - 1
                        cells(column) = row.ToString("D7") & "-" & column.ToString("D2") & "-abcdefghijklmnopqrstuv"
                    Next
                    writer.WriteLine(String.Join(",", cells))
                Next
            End Using
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()
            Dim timer As Stopwatch = Stopwatch.StartNew()
            Dim document As CsvDocument = CsvParser.Load(path, New CsvLoadOptions())
            Dim parseMs As Long = timer.ElapsedMilliseconds
            Dim build As MethodInfo = GetType(CsvTableBuilder).GetMethod("Build")
            Dim arguments As Object() = If(build.GetParameters().Length = 2,
                                          New Object() {document, True}, New Object() {document})
            Using table As DataTable = DirectCast(build.Invoke(Nothing, arguments), DataTable)
                If build.GetParameters().Length = 1 Then
                    GetType(CsvDocument).GetMethod("ReleaseRecordStorage", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(document, Nothing)
                End If
                Dim loadMs As Long = timer.ElapsedMilliseconds
                Dim view As DataView = table.DefaultView
                Dim readyMs As Long = timer.ElapsedMilliseconds
                If view.Count <> rowCount OrElse CStr(view(rowCount - 1)(0)) <> "0100000-00-abcdefghijklmnopqrstuv" Then
                    Throw New Exception("Benchmark data mismatch")
                End If
                Dim managedBytes As Long = GC.GetTotalMemory(True)
                Using process As Process = Process.GetCurrentProcess()
                    Console.WriteLine("BENCHMARK rows={0} columns={1} file_bytes={2} parse_ms={3} load_ms={4} view_ready_ms={5} peak_working_set_bytes={6} managed_bytes={7} process_bits={8}",
                                      rowCount, columnCount, New FileInfo(path).Length, parseMs, loadMs, readyMs,
                                      process.PeakWorkingSet64, managedBytes, IntPtr.Size * 8)
                End Using
                GC.KeepAlive(document)
                GC.KeepAlive(table)
                GC.KeepAlive(view)
            End Using
        Finally
            File.Delete(path)
        End Try
    End Sub
End Module
