Imports System.IO
Imports System.Text

''' <summary>
''' Launcher for Path Studio.
'''
''' The application itself is the Python in tools\ - the planner, the radar
''' navigator, the smoother and the .campath writer, all of it tuned against
''' measured results. This project exists to start it, to carry the scripts into
''' an install, and to say something useful when the machine cannot run them.
'''
''' It is a separate project so it can be built and run without building
''' nuTerra, which shares exactly two things with it: the .campath files Path
''' Studio writes, and a keyboard shortcut that starts this exe.
''' </summary>
Module Program

    Private Const SCRIPT As String = "path_studio.py"

    ' Everything the scripts import that is not in the standard library, plus
    ' tkinter - which IS standard but is left out of some installs and produces a
    ' baffling failure when it is missing.
    Private ReadOnly REQUIRED() As String = {"numpy", "scipy", "PIL", "tkinter"}

    Sub Main(args As String())
        Try
            Dim script = FindScript()
            If script Is Nothing Then
                Fail("Could not find " & SCRIPT & "." & vbCrLf & vbCrLf &
                     "Looked in tools\ beside " & AppContext.BaseDirectory &
                     " and in every folder above it.")
                Return
            End If

            Dim py = FindPython()
            If py Is Nothing Then
                Fail("No Python interpreter found." & vbCrLf & vbCrLf &
                     "Tried the PATHSTUDIO_PYTHON variable, the py launcher, " &
                     "and python.exe on PATH." & vbCrLf & vbCrLf &
                     "Install Python 3 with numpy, scipy and pillow, or set " &
                     "PATHSTUDIO_PYTHON to an interpreter that has them.")
                Return
            End If

            ' Preflight BEFORE launching the real thing.
            '
            ' The app is started windowless so it does not drag a console around
            ' with it, which means an import error would leave nothing on screen
            ' at all - the process would simply vanish. This runs a one-line
            ' import first, where the output is short, bounded and safe to read
            ' to completion, and reports what is missing.
            Dim missing = CheckImports(py)
            If missing <> "" Then
                Fail("Python is present but the tools cannot run." & vbCrLf & vbCrLf &
                     missing & vbCrLf & vbCrLf &
                     "Interpreter: " & py & vbCrLf & vbCrLf &
                     "Install them with:" & vbCrLf &
                     "    """ & py & """ -m pip install numpy scipy pillow")
                Return
            End If

            Launch(py, script, args)

        Catch ex As Exception
            Fail("Path Studio could not start." & vbCrLf & vbCrLf & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' tools\path_studio.py, beside the exe first and then upward.
    '''
    ''' Beside the exe is the installed layout, where the project drops the
    ''' scripts. Walking up is what makes a plain F5 out of the build folder work
    ''' - bin\Debug\net6.0-windows is four levels under the repo, and hard coding
    ''' that count breaks the moment the output path changes.
    ''' </summary>
    Private Function FindScript() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim p = Path.Combine(dir.FullName, "tools", SCRIPT)
            If File.Exists(p) Then Return p
            dir = dir.Parent
        End While
        Return Nothing
    End Function

    ''' <summary>
    ''' An interpreter that can actually be started from a process.
    '''
    ''' python.exe on PATH is often the WindowsApps alias - a Store app execution
    ''' stub that works from a shell and behaves unpredictably when launched with
    ''' a different environment. It is tried LAST and skipped when it resolves
    ''' into WindowsApps, because py.exe is a real executable and the correct
    ''' answer on Windows.
    '''
    ''' The windowless variants come first at each step: this starts a Tk
    ''' application, and pythonw keeps a console from appearing behind it.
    ''' </summary>
    Private Function FindPython() As String
        Dim env = Environment.GetEnvironmentVariable("PATHSTUDIO_PYTHON")
        If Not String.IsNullOrWhiteSpace(env) AndAlso File.Exists(env) Then Return env

        For Each name In {"pyw.exe", "py.exe"}
            Dim p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name)
            If File.Exists(p) Then Return p
        Next

        For Each name In {"pythonw.exe", "python.exe"}
            Dim p = OnPath(name)
            If p IsNot Nothing AndAlso
               p.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0 Then
                Return p
            End If
        Next

        ' The usual per-user install, newest first.
        Dim root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python")
        If Directory.Exists(root) Then
            Dim found = Directory.GetDirectories(root).
                                  OrderByDescending(Function(d) d).
                                  Select(Function(d) Path.Combine(d, "pythonw.exe")).
                                  FirstOrDefault(AddressOf File.Exists)
            If found IsNot Nothing Then Return found
        End If

        Return Nothing
    End Function

    Private Function OnPath(exe As String) As String
        Dim paths = Environment.GetEnvironmentVariable("PATH")
        If paths Is Nothing Then Return Nothing
        For Each d In paths.Split(";"c)
            If d = "" Then Continue For
            Try
                Dim p = Path.Combine(d.Trim(), exe)
                If File.Exists(p) Then Return p
            Catch
                ' A malformed PATH entry is not worth failing the launch over.
            End Try
        Next
        Return Nothing
    End Function

    ''' <summary>Returns "" when every import works, else what failed.</summary>
    Private Function CheckImports(py As String) As String
        Dim code = "import " & String.Join(", ", REQUIRED)
        Dim psi As New Diagnostics.ProcessStartInfo(py) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardError = True,
            .RedirectStandardOutput = True
        }
        ' -c goes through the py launcher unchanged, so one form covers both.
        psi.ArgumentList.Add("-c")
        psi.ArgumentList.Add(code)

        Using pr = Diagnostics.Process.Start(psi)
            Dim err = pr.StandardError.ReadToEnd()
            pr.StandardOutput.ReadToEnd()
            If Not pr.WaitForExit(30000) Then
                Try : pr.Kill() : Catch : End Try
                Return "The interpreter did not respond within 30 seconds."
            End If
            If pr.ExitCode = 0 Then Return ""
            Return If(err.Trim() = "", "Exit code " & pr.ExitCode, err.Trim())
        End Using
    End Function

    ''' <summary>
    ''' Start it and get out of the way.
    '''
    ''' Nothing is redirected here on purpose. Path Studio prints progress for
    ''' its whole run, and a redirected pipe that nobody drains fills up and
    ''' blocks the writer - the app would hang part way through a route. The
    ''' preflight above is where errors get caught, precisely because its output
    ''' is one line long.
    ''' </summary>
    Private Sub Launch(py As String, script As String, args As String())
        Dim psi As New Diagnostics.ProcessStartInfo(py) With {
            .UseShellExecute = False,
            .WorkingDirectory = Path.GetDirectoryName(script)
        }
        psi.ArgumentList.Add(script)
        For Each a In args
            psi.ArgumentList.Add(a)
        Next
        Diagnostics.Process.Start(psi)
    End Sub

    Private Sub Fail(message As String)
        Windows.Forms.MessageBox.Show(message, "Path Studio",
                                      Windows.Forms.MessageBoxButtons.OK,
                                      Windows.Forms.MessageBoxIcon.Error)
    End Sub

End Module
