Imports System
Imports System.IO
Imports System.Text

Public NotInheritable Class CsvTextCodec
    Private Sub New()
    End Sub

    Public Shared Function DecodeFile(filePath As String,
                                      requestedEncoding As CsvTextEncoding) As DecodedCsvText
        Dim prefix As Byte() = ReadFilePrefix(filePath, 65536)
        Dim bomEncoding As CsvTextEncoding = DetectBom(prefix)

        If requestedEncoding <> CsvTextEncoding.AutoDetect Then
            Return DecodeFileUsing(
                filePath,
                requestedEncoding,
                IsMatchingBom(bomEncoding, requestedEncoding))
        End If

        If bomEncoding <> CsvTextEncoding.AutoDetect Then
            Return DecodeFileUsing(filePath, bomEncoding, True)
        End If

        Dim utf16Encoding As CsvTextEncoding = DetectBomlessUtf16(prefix)
        If utf16Encoding <> CsvTextEncoding.AutoDetect Then
            Return DecodeFileUsing(filePath, utf16Encoding, False)
        End If

        Try
            Dim result As DecodedCsvText =
                DecodeFileUsingStrict(
                    filePath,
                    CsvTextEncoding.Utf8NoBom,
                    False)

            Dim shiftJisSample As String = Nothing
            If ContainsNonAscii(prefix) AndAlso
               TryDecodeBytes(prefix, CsvTextEncoding.ShiftJis, shiftJisSample) Then
                result.DetectionWarning =
                    "UTF-8とShift_JISの両方として解釈可能なため、UTF-8として表示しています。必要に応じて文字コードを明示してください。"
            End If
            Return result
        Catch ex As DecoderFallbackException
            Return DecodeFileUsing(filePath, CsvTextEncoding.ShiftJis, False)
        End Try
    End Function

    Public Shared Function DecodeBytes(bytes As Byte(),
                                       requestedEncoding As CsvTextEncoding) As DecodedCsvText
        If bytes Is Nothing Then
            Throw New ArgumentNullException("bytes")
        End If

        Dim bomEncoding As CsvTextEncoding = DetectBom(bytes)

        If requestedEncoding = CsvTextEncoding.AutoDetect Then
            If bomEncoding <> CsvTextEncoding.AutoDetect Then
                Return DecodeUsing(bytes, bomEncoding, True)
            End If

            Dim utf16Encoding As CsvTextEncoding = DetectBomlessUtf16(bytes)
            If utf16Encoding <> CsvTextEncoding.AutoDetect Then
                Return DecodeUsing(bytes, utf16Encoding, False)
            End If

            Dim utf8Text As String = Nothing
            If TryDecodeBytes(bytes, CsvTextEncoding.Utf8NoBom, utf8Text) Then
                Dim result As DecodedCsvText =
                    DecodeUsing(bytes, CsvTextEncoding.Utf8NoBom, False)
                Dim shiftJisText As String = Nothing
                If ContainsNonAscii(bytes) AndAlso
                   TryDecodeBytes(bytes, CsvTextEncoding.ShiftJis, shiftJisText) Then
                    result.DetectionWarning =
                        "UTF-8とShift_JISの両方として解釈可能なため、UTF-8として表示しています。必要に応じて文字コードを明示してください。"
                End If
                Return result
            End If

            Return DecodeUsing(bytes, CsvTextEncoding.ShiftJis, False)
        End If

        Return DecodeUsing(
            bytes,
            requestedEncoding,
            IsMatchingBom(bomEncoding, requestedEncoding))
    End Function

    Public Shared Function GetEncodingForWrite(kind As CsvTextEncoding) As Encoding
        Select Case kind
            Case CsvTextEncoding.Utf8Bom
                Return New UTF8Encoding(True, True)
            Case CsvTextEncoding.ShiftJis
                Return Encoding.GetEncoding(
                    932,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback)
            Case CsvTextEncoding.Utf16LittleEndian
                Return New UnicodeEncoding(False, True, True)
            Case CsvTextEncoding.Utf16BigEndian
                Return New UnicodeEncoding(True, True, True)
            Case Else
                Return New UTF8Encoding(False, True)
        End Select
    End Function

    Public Shared Function DetectLineEndings(text As String) As LineEndingInfo
        Dim crLfCount As Integer = 0
        Dim lfCount As Integer = 0
        Dim crCount As Integer = 0
        Dim index As Integer = 0

        While index < text.Length
            If text(index) = ControlChars.Cr Then
                If index + 1 < text.Length AndAlso text(index + 1) = ControlChars.Lf Then
                    crLfCount += 1
                    index += 2
                    Continue While
                End If
                crCount += 1
            ElseIf text(index) = ControlChars.Lf Then
                lfCount += 1
            End If
            index += 1
        End While

        Dim kinds As Integer = 0
        If crLfCount > 0 Then kinds += 1
        If lfCount > 0 Then kinds += 1
        If crCount > 0 Then kinds += 1

        Dim preferred As String = Environment.NewLine
        If lfCount > crLfCount AndAlso lfCount >= crCount Then
            preferred = ControlChars.Lf
        ElseIf crCount > crLfCount AndAlso crCount > lfCount Then
            preferred = ControlChars.Cr
        ElseIf crLfCount > 0 Then
            preferred = ControlChars.CrLf
        End If

        Dim displayName As String
        If kinds = 0 Then
            displayName = "改行なし"
        ElseIf kinds > 1 Then
            displayName = String.Format(
                "混在（CRLF:{0} / LF:{1} / CR:{2}）",
                crLfCount,
                lfCount,
                crCount)
        ElseIf crLfCount > 0 Then
            displayName = "CRLF"
        ElseIf lfCount > 0 Then
            displayName = "LF"
        Else
            displayName = "CR"
        End If

        Return New LineEndingInfo(displayName, preferred, crLfCount, lfCount, crCount)
    End Function

    Private Shared Function ReadFilePrefix(filePath As String,
                                           maximumBytes As Integer) As Byte()
        Using stream As New FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite)
            Dim length As Integer =
                CInt(Math.Min(CLng(maximumBytes), stream.Length))
            If length = 0 Then Return New Byte() {}

            Dim bytes(length - 1) As Byte
            Dim offset As Integer = 0
            While offset < bytes.Length
                Dim read As Integer = stream.Read(bytes, offset, bytes.Length - offset)
                If read = 0 Then Exit While
                offset += read
            End While

            If offset = bytes.Length Then Return bytes
            If offset = 0 Then Return New Byte() {}
            Dim shortened(offset - 1) As Byte
            Buffer.BlockCopy(bytes, 0, shortened, 0, offset)
            Return shortened
        End Using
    End Function

    Private Shared Function DecodeFileUsing(filePath As String,
                                            kind As CsvTextEncoding,
                                            hasMatchingBom As Boolean) As DecodedCsvText
        Try
            Return DecodeFileUsingStrict(filePath, kind, hasMatchingBom)
        Catch ex As DecoderFallbackException
            Dim bomLength As Integer = GetBomLength(kind, hasMatchingBom)
            Return New DecodedCsvText() With {
                .Text = ReadFileText(
                    filePath,
                    GetEncodingForDecode(kind, False),
                    bomLength),
                .EncodingKind = NormalizeEncodingKind(kind, hasMatchingBom),
                .EncodingDisplayName = GetEncodingDisplayName(kind, hasMatchingBom),
                .HasBom = hasMatchingBom,
                .UsedReplacementCharacter = True
            }
        End Try
    End Function

    Private Shared Function DecodeFileUsingStrict(filePath As String,
                                                  kind As CsvTextEncoding,
                                                  hasMatchingBom As Boolean) As DecodedCsvText
        Dim bomLength As Integer = GetBomLength(kind, hasMatchingBom)
        Return New DecodedCsvText() With {
            .Text = ReadFileText(
                filePath,
                GetEncodingForDecode(kind, True),
                bomLength),
            .EncodingKind = NormalizeEncodingKind(kind, hasMatchingBom),
            .EncodingDisplayName = GetEncodingDisplayName(kind, hasMatchingBom),
            .HasBom = hasMatchingBom,
            .UsedReplacementCharacter = False
        }
    End Function

    Private Shared Function ReadFileText(filePath As String,
                                         encoding As Encoding,
                                         bomLength As Integer) As String
        Using stream As New FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite)
            stream.Position = Math.Min(CLng(bomLength), stream.Length)
            Using reader As New StreamReader(
                stream,
                encoding,
                False,
                65536,
                False)
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Shared Function GetEncodingForDecode(kind As CsvTextEncoding,
                                                 throwOnInvalidBytes As Boolean) As Encoding
        Select Case kind
            Case CsvTextEncoding.ShiftJis
                Return Encoding.GetEncoding(
                    932,
                    If(throwOnInvalidBytes,
                       EncoderFallback.ExceptionFallback,
                       EncoderFallback.ReplacementFallback),
                    If(throwOnInvalidBytes,
                       DecoderFallback.ExceptionFallback,
                       DecoderFallback.ReplacementFallback))
            Case CsvTextEncoding.Utf16LittleEndian
                Return New UnicodeEncoding(False, False, throwOnInvalidBytes)
            Case CsvTextEncoding.Utf16BigEndian
                Return New UnicodeEncoding(True, False, throwOnInvalidBytes)
            Case Else
                Return New UTF8Encoding(False, throwOnInvalidBytes)
        End Select
    End Function

    Private Shared Function GetBomLength(kind As CsvTextEncoding,
                                         hasMatchingBom As Boolean) As Integer
        If Not hasMatchingBom Then Return 0
        If kind = CsvTextEncoding.Utf8Bom OrElse
           kind = CsvTextEncoding.Utf8NoBom Then
            Return 3
        End If
        If kind = CsvTextEncoding.Utf16LittleEndian OrElse
           kind = CsvTextEncoding.Utf16BigEndian Then
            Return 2
        End If
        Return 0
    End Function

    Private Shared Function GetEncodingDisplayName(kind As CsvTextEncoding,
                                                   hasMatchingBom As Boolean) As String
        Select Case kind
            Case CsvTextEncoding.Utf8Bom, CsvTextEncoding.Utf8NoBom
                Return If(hasMatchingBom,
                          "UTF-8（BOMあり）",
                          "UTF-8（BOMなし）")
            Case CsvTextEncoding.ShiftJis
                Return "Shift_JIS"
            Case CsvTextEncoding.Utf16LittleEndian
                Return "UTF-16 LE"
            Case CsvTextEncoding.Utf16BigEndian
                Return "UTF-16 BE"
            Case Else
                Return "UTF-8（BOMなし）"
        End Select
    End Function

    Private Shared Function DetectBomlessUtf16(bytes As Byte()) As CsvTextEncoding
        If bytes Is Nothing OrElse bytes.Length < 4 Then
            Return CsvTextEncoding.AutoDetect
        End If

        Dim sampleLength As Integer = Math.Min(bytes.Length, 4096)
        If sampleLength Mod 2 <> 0 Then sampleLength -= 1
        If sampleLength < 4 Then Return CsvTextEncoding.AutoDetect

        Dim sample(sampleLength - 1) As Byte
        Buffer.BlockCopy(bytes, 0, sample, 0, sampleLength)

        Dim littleText As String = Nothing
        Dim bigText As String = Nothing
        Dim littleScore As Integer = Integer.MinValue
        Dim bigScore As Integer = Integer.MinValue

        If TryDecodeBytes(sample, CsvTextEncoding.Utf16LittleEndian, littleText) Then
            littleScore = ScoreUtf16Candidate(sample, littleText, True)
        End If
        If TryDecodeBytes(sample, CsvTextEncoding.Utf16BigEndian, bigText) Then
            bigScore = ScoreUtf16Candidate(sample, bigText, False)
        End If

        Const minimumScore As Integer = 10
        Const minimumMargin As Integer = 4
        If littleScore >= minimumScore AndAlso littleScore >= bigScore + minimumMargin Then
            Return CsvTextEncoding.Utf16LittleEndian
        End If
        If bigScore >= minimumScore AndAlso bigScore >= littleScore + minimumMargin Then
            Return CsvTextEncoding.Utf16BigEndian
        End If
        Return CsvTextEncoding.AutoDetect
    End Function

    Private Shared Function ScoreUtf16Candidate(bytes As Byte(),
                                                text As String,
                                                littleEndian As Boolean) As Integer
        Dim expectedNulls As Integer = 0
        Dim unexpectedNulls As Integer = 0
        Dim structuralPairs As Integer = 0

        For index As Integer = 0 To bytes.Length - 2 Step 2
            Dim lowIndex As Integer = If(littleEndian, index, index + 1)
            Dim highIndex As Integer = If(littleEndian, index + 1, index)
            Dim low As Byte = bytes(lowIndex)
            Dim high As Byte = bytes(highIndex)

            If high = 0 Then expectedNulls += 1
            If low = 0 Then unexpectedNulls += 1

            If high = 0 AndAlso IsCsvStructuralAscii(low) Then
                structuralPairs += 1
            End If
        Next

        If expectedNulls = 0 AndAlso structuralPairs = 0 Then
            Return Integer.MinValue \\ 4
        End If

        Dim score As Integer =
            (structuralPairs * 12) +
            (expectedNulls * 2) -
            (unexpectedNulls * 6)

        For Each character As Char In text
            If character = ChrW(0) Then
                score -= 20
            ElseIf Char.IsControl(character) AndAlso
                   character <> ControlChars.Cr AndAlso
                   character <> ControlChars.Lf AndAlso
                   character <> ControlChars.Tab Then
                score -= 8
            End If
        Next
        Return score
    End Function

    Private Shared Function IsCsvStructuralAscii(value As Byte) As Boolean
        Return value = CByte(AscW(","c)) OrElse
               value = CByte(AscW(";"c)) OrElse
               value = CByte(AscW("|"c)) OrElse
               value = CByte(AscW(ControlChars.Tab)) OrElse
               value = CByte(AscW(ControlChars.Cr)) OrElse
               value = CByte(AscW(ControlChars.Lf)) OrElse
               value = CByte(AscW(ControlChars.Quote))
    End Function

    Private Shared Function TryDecodeBytes(bytes As Byte(),
                                           kind As CsvTextEncoding,
                                           ByRef text As String) As Boolean
        Try
            text = GetEncodingForDecode(kind, True).GetString(bytes)
            Return True
        Catch ex As DecoderFallbackException
            text = Nothing
            Return False
        End Try
    End Function

    Private Shared Function ContainsNonAscii(bytes As Byte()) As Boolean
        For Each value As Byte In bytes
            If value >= &H80 Then Return True
        Next
        Return False
    End Function

    Private Shared Function DetectBom(bytes As Byte()) As CsvTextEncoding
        If bytes.Length >= 3 AndAlso
           bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
            Return CsvTextEncoding.Utf8Bom
        End If

        If bytes.Length >= 2 AndAlso bytes(0) = &HFF AndAlso bytes(1) = &HFE Then
            Return CsvTextEncoding.Utf16LittleEndian
        End If

        If bytes.Length >= 2 AndAlso bytes(0) = &HFE AndAlso bytes(1) = &HFF Then
            Return CsvTextEncoding.Utf16BigEndian
        End If

        Return CsvTextEncoding.AutoDetect
    End Function

    Private Shared Function IsMatchingBom(detectedBom As CsvTextEncoding,
                                          requestedEncoding As CsvTextEncoding) As Boolean
        If detectedBom = CsvTextEncoding.Utf8Bom AndAlso
           (requestedEncoding = CsvTextEncoding.Utf8Bom OrElse
            requestedEncoding = CsvTextEncoding.Utf8NoBom) Then
            Return True
        End If

        Return detectedBom = requestedEncoding AndAlso
               detectedBom <> CsvTextEncoding.AutoDetect
    End Function

    Private Shared Function DecodeUsing(bytes As Byte(),
                                        kind As CsvTextEncoding,
                                        hasMatchingBom As Boolean) As DecodedCsvText
        Dim encoding As Encoding = GetEncodingForDecode(kind, True)
        Dim displayName As String = GetEncodingDisplayName(kind, hasMatchingBom)
        Dim bomLength As Integer = GetBomLength(kind, hasMatchingBom)

        Try
            Return New DecodedCsvText() With {
                .Text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength),
                .EncodingKind = NormalizeEncodingKind(kind, hasMatchingBom),
                .EncodingDisplayName = displayName,
                .HasBom = hasMatchingBom,
                .UsedReplacementCharacter = False
            }
        Catch ex As DecoderFallbackException
            Dim fallbackEncoding As Encoding = GetEncodingForDecode(kind, False)
            Return New DecodedCsvText() With {
                .Text = fallbackEncoding.GetString(bytes, bomLength, bytes.Length - bomLength),
                .EncodingKind = NormalizeEncodingKind(kind, hasMatchingBom),
                .EncodingDisplayName = displayName,
                .HasBom = hasMatchingBom,
                .UsedReplacementCharacter = True
            }
        End Try
    End Function

    Private Shared Function NormalizeEncodingKind(kind As CsvTextEncoding,
                                                  hasBom As Boolean) As CsvTextEncoding
        If kind = CsvTextEncoding.Utf8Bom OrElse kind = CsvTextEncoding.Utf8NoBom Then
            Return If(hasBom, CsvTextEncoding.Utf8Bom, CsvTextEncoding.Utf8NoBom)
        End If
        Return kind
    End Function
End Class
