Imports System.IO
Imports System.Runtime.CompilerServices
Imports OpenTK.Graphics.OpenGL4

Module modUtilities
    Public Sub LogThis(entry As String)
#If DEBUG Then
        Debug.Print(entry)
#End If

        'Writes to the log and immediately saves it.
        nuTerra_LOG.AppendLine(entry)
        File.WriteAllText(Path.Combine(TEMP_STORAGE, "nuTerra_Log.txt"), nuTerra_LOG.ToString)
    End Sub

    ' Allows us to split by strings. Not just characters.
    <Extension()>
    Public Function Split(ByVal input As String,
                          ParamArray delimiter As String()) As String()
        Return input.Split(delimiter, StringSplitOptions.None)
        Dim a(0) As String
        a(0) = input
        Return a
    End Function
End Module
