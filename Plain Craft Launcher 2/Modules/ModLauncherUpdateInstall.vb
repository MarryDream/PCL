Imports System.Diagnostics
Imports System.IO
Imports System.Security.Cryptography
Imports Newtonsoft.Json.Linq

Friend Module ModLauncherUpdateInstall
    Friend Function LauncherUpdateHash(FileName As String) As String
        Using Stream = File.OpenRead(FileName), Sha = SHA256.Create()
            Return BitConverter.ToString(Sha.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

    Friend Function CurrentLauncherPath() As String
        Return Process.GetCurrentProcess().MainModule.FileName
    End Function

    Friend Function GetLauncherUpdateFolder(Source As String, Info As LauncherUpdateInfo, Optional TargetPath As String = Nothing) As String
        If TargetPath Is Nothing Then TargetPath = CurrentLauncherPath()
        Dim Key As String
        Using Sha = SHA256.Create()
            Key = BitConverter.ToString(Sha.ComputeHash(Text.Encoding.UTF8.GetBytes(Source & vbLf & Info.Version & vbLf & Info.Sha256.ToLowerInvariant()))).Replace("-", "").ToLowerInvariant()
        End Using
        Return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(TargetPath)), "PCL", "CustomUpdates", Key)
    End Function

    Friend Sub PrepareLauncherUpdate(Folder As String, Info As LauncherUpdateInfo, Source As String, Optional TargetPath As String = Nothing)
        If TargetPath Is Nothing Then TargetPath = CurrentLauncherPath()
        TargetPath = Path.GetFullPath(TargetPath)
        If Not String.Equals(Path.GetFullPath(Folder), GetLauncherUpdateFolder(Source, Info, TargetPath), StringComparison.OrdinalIgnoreCase) Then Throw New IOException("更新缓存目录不匹配。")
        Dim OldVersion = FileVersionInfo.GetVersionInfo(TargetPath).ProductVersion
        If CompareLauncherVersions(Info.Version, OldVersion) <= 0 Then Throw New InvalidDataException("更新包版本必须高于当前启动器版本。")
        Dim ZipPath = Path.Combine(Folder, "update.zip")
        If Not String.Equals(LauncherUpdateHash(ZipPath), Info.Sha256, StringComparison.OrdinalIgnoreCase) Then
            File.Delete(ZipPath)
            Throw New InvalidDataException("更新包 SHA-256 校验失败。")
        End If
        Dim Candidate = Path.Combine(Folder, "candidate.exe")
        Dim Temporary = Path.Combine(Folder, "candidate.tmp")
        Try
            Using Archive = Compression.ZipFile.OpenRead(ZipPath)
                If Archive.Entries.Count <> 1 OrElse Archive.Entries(0).FullName <> "Plain Craft Launcher 2.exe" Then Throw New InvalidDataException("更新 ZIP 必须仅包含 Plain Craft Launcher 2.exe。")
                Using Input = Archive.Entries(0).Open(), Output = File.Create(Temporary)
                    Input.CopyTo(Output)
                End Using
            End Using
            If FileVersionInfo.GetVersionInfo(Temporary).ProductVersion <> Info.Version Then Throw New InvalidDataException("更新 EXE 版本与版本信息文件不一致。")
            Reflection.AssemblyName.GetAssemblyName(Temporary) '拒绝无效或非托管 EXE。
            File.Copy(Temporary, Candidate, True)
            Dim Receipt As New JObject From {
                {"version", Info.Version}, {"url", Info.Url}, {"sha256", Info.Sha256}, {"notes", Info.Notes},
                {"source", Source}, {"target", TargetPath}, {"oldVersion", OldVersion},
                {"oldHash", LauncherUpdateHash(TargetPath)}, {"exeHash", LauncherUpdateHash(Candidate)}}
            File.WriteAllText(Path.Combine(Folder, "install.json"), Receipt.ToString(), New Text.UTF8Encoding(False))
        Finally
            If File.Exists(Temporary) Then File.Delete(Temporary)
        End Try
    End Sub

    Private Function ReadLauncherInstall(Folder As String, Target As String, Candidate As String) As JObject
        Dim Receipt = JObject.Parse(File.ReadAllText(Path.Combine(Folder, "install.json"), Text.Encoding.UTF8))
        Dim Info = ParseLauncherUpdate(Receipt.ToString())
        If Not String.Equals(Path.GetFullPath(Receipt.Value(Of String)("target")), Path.GetFullPath(Target), StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(Path.GetFullPath(Folder), GetLauncherUpdateFolder(Receipt.Value(Of String)("source"), Info, Target), StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(Path.GetFullPath(Candidate), Path.Combine(Path.GetFullPath(Folder), "candidate.exe"), StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("更新安装路径校验失败。")
        If Not String.Equals(LauncherUpdateHash(Path.Combine(Folder, "update.zip")), Info.Sha256, StringComparison.OrdinalIgnoreCase) OrElse
           LauncherUpdateHash(Candidate) <> Receipt.Value(Of String)("exeHash") OrElse
           FileVersionInfo.GetVersionInfo(Candidate).ProductVersion <> Info.Version Then Throw New InvalidDataException("待安装文件已改变，请重新下载更新。")
        Reflection.AssemblyName.GetAssemblyName(Candidate)
        If CompareLauncherVersions(Info.Version, Receipt.Value(Of String)("oldVersion")) <= 0 Then Throw New InvalidDataException("不允许安装相同或更旧的版本。")
        Return Receipt
    End Function

    Friend Sub StartLauncherUpdateHelper(Folder As String)
        Dim Target = CurrentLauncherPath()
        Dim Candidate = Path.Combine(Folder, "candidate.exe")
        Dim Receipt = ReadLauncherInstall(Folder, Target, Candidate)
        If LauncherUpdateHash(Target) <> Receipt.Value(Of String)("oldHash") Then Throw New IOException("当前启动器文件已改变，请重新检查更新。")
        Receipt("processId") = Process.GetCurrentProcess().Id
        Receipt("processStart") = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks
        File.WriteAllText(Path.Combine(Folder, "install.json"), Receipt.ToString(), New Text.UTF8Encoding(False))
        Dim Ready = Path.Combine(Folder, "helper-ready")
        Dim Failure = Path.Combine(Folder, "install-error.txt")
        If File.Exists(Ready) Then File.Delete(Ready)
        If File.Exists(Failure) Then File.Delete(Failure)
        Dim Arguments = "--update " & Process.GetCurrentProcess().Id & " """ & Target & """ """ & Candidate & """ True"
        Using Helper = Process.Start(New ProcessStartInfo(Candidate, Arguments) With {.UseShellExecute = False, .WorkingDirectory = Folder})
            Dim Timer = Stopwatch.StartNew()
            While Not File.Exists(Ready)
                If Helper.HasExited OrElse Timer.ElapsedMilliseconds > 10000 Then
                    Throw New IOException("更新程序未能就绪。" & If(File.Exists(Failure), File.ReadAllText(Failure), "请检查杀毒软件或文件权限。"))
                End If
                Threading.Thread.Sleep(50)
            End While
        End Using
    End Sub

    '在新版 EXE 的独立 --update 进程运行；任何失败都不能强杀旧进程。
    Friend Sub ReplaceLauncherUpdate(ProcessId As Integer, Target As String, Candidate As String, Restart As Boolean)
        Dim Folder = Path.GetDirectoryName(Path.GetFullPath(Candidate))
        Try
            If Not String.Equals(CurrentLauncherPath(), Path.GetFullPath(Candidate), StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("更新辅助进程路径不匹配。")
            Using InstallLock = File.Open(Path.Combine(Folder, "helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
                Dim Receipt = ReadLauncherInstall(Folder, Target, Candidate)
                If ProcessId <> Receipt.Value(Of Integer)("processId") Then Throw New InvalidDataException("更新进程编号不匹配。")
                Dim OldProcess As Process = Nothing
                Try
                    OldProcess = Process.GetProcessById(ProcessId)
                Catch ex As ArgumentException
                    '父进程已经退出。
                End Try
                If OldProcess IsNot Nothing Then
                    Using OldProcess
                        If OldProcess.StartTime.ToUniversalTime().Ticks <> Receipt.Value(Of Long)("processStart") OrElse
                           Not String.Equals(OldProcess.MainModule.FileName, Path.GetFullPath(Target), StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("原启动器进程已改变。")
                        File.WriteAllText(Path.Combine(Folder, "helper-ready"), "ready")
                        If Not OldProcess.WaitForExit(30000) Then Throw New TimeoutException("等待原启动器退出超时，未安装更新。")
                    End Using
                Else
                    File.WriteAllText(Path.Combine(Folder, "helper-ready"), "ready")
                End If
                InstallLauncherUpdateFiles(Folder, Target, Candidate, Restart)
            End Using
        Catch ex As Exception
            Try
                File.WriteAllText(Path.Combine(Folder, "install-error.txt"), ex.ToString(), New Text.UTF8Encoding(False))
            Catch
            End Try
            Throw
        End Try
    End Sub

    Friend Sub InstallLauncherUpdateFiles(Folder As String, Target As String, Candidate As String, Restart As Boolean)
        Dim Receipt = ReadLauncherInstall(Folder, Target, Candidate)
        Dim Backup = Path.Combine(Folder, "previous.exe")
        Dim Replacement = Path.Combine(Folder, "replacement.tmp")
        If LauncherUpdateHash(Target) <> Receipt.Value(Of String)("oldHash") Then Throw New IOException("原启动器已改变，取消覆盖。")
        File.Copy(Candidate, Replacement, True)
        Dim Timer = Stopwatch.StartNew()
        Do
            Try
                File.Replace(Replacement, Target, Backup)
                Exit Do
            Catch ex As IOException
                If Timer.ElapsedMilliseconds >= 10000 Then Throw
                Threading.Thread.Sleep(200)
            End Try
        Loop
        Try
            File.WriteAllText(Path.Combine(Folder, "installed.json"), Receipt.ToString(), New Text.UTF8Encoding(False))
            If Restart Then Process.Start(New ProcessStartInfo(Target) With {.UseShellExecute = False, .WorkingDirectory = Path.GetDirectoryName(Target)})
        Catch
            File.Replace(Backup, Target, Path.Combine(Folder, "failed-new.exe"))
            File.Delete(Path.Combine(Folder, "installed.json"))
            Throw
        End Try
    End Sub

    Friend Function ShowLauncherUpdateResult() As String
        Dim OldVersion As String = Nothing
        Dim Root = Path.Combine(Path.GetDirectoryName(CurrentLauncherPath()), "PCL", "CustomUpdates")
        If Not Directory.Exists(Root) Then Return Nothing
        For Each Folder In Directory.GetDirectories(Root)
            Try
                Dim Failure = Path.Combine(Folder, "install-error.txt")
                If File.Exists(Failure) Then
                    Logger.Warn("上次启动器更新未完成：" & File.ReadAllText(Failure))
                    Hint("上次启动器更新安装失败，原版本与恢复备份已保留。", HintType.Red)
                    File.Move(Failure, Path.Combine(Folder, "install-error-shown-" & Guid.NewGuid().ToString("N") & ".txt"))
                End If
                Dim Installed = Path.Combine(Folder, "installed.json")
                If Not File.Exists(Installed) Then Continue For
                Dim Receipt = JObject.Parse(File.ReadAllText(Installed))
                If Receipt.Value(Of String)("version") <> VersionName OrElse
                   Not String.Equals(Receipt.Value(Of String)("target"), CurrentLauncherPath(), StringComparison.OrdinalIgnoreCase) Then Continue For
                OldVersion = Receipt.Value(Of String)("oldVersion")
                File.Move(Installed, Path.Combine(Folder, "installed-shown.json"))
                '清理成功安装的已知文件
            Catch ex As Exception
                Logger.Warn(ex, "读取启动器更新结果失败")
            End Try
            If File.Exists(Path.Combine(Folder, "installed-shown.json")) Then
                For Each Name In {"candidate.exe", "update.zip", "replacement.tmp", "helper-ready"}
                    Try
                        File.Delete(Path.Combine(Folder, Name))
                    Catch
                    End Try
                Next
            End If
        Next
        Return OldVersion
    End Function
End Module
