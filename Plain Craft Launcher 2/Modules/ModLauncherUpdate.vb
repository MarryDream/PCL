Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

'自定义更新源检测与下载。安装必须由用户确认。
Friend Module ModLauncherUpdate
    Private ReadOnly UpdateStateLock As New Object
    Private UpdateCheckRunning As Integer
    Private UpdateGeneration As Integer
    Private CheckedSource As String
    Private CheckedNewest As Boolean?
    Private AutoUpdateStarted As Integer
    Private ActiveUpdateTask As LoaderCombo(Of Integer)
    Private ReadyUpdateFolder As String

    Friend Sub InvalidateLauncherUpdate()
        SyncLock UpdateStateLock
            UpdateGeneration += 1
            CheckedSource = Nothing
            CheckedNewest = Nothing
            ReadyUpdateFolder = Nothing
        End SyncLock
        ActiveUpdateTask?.Cancel()
    End Sub

    Friend Function GetLauncherNewest() As Boolean?
        Dim Source = Settings.Get(Of String)("SystemSystemUpdateSource").Trim()
        SyncLock UpdateStateLock
            If Source <> CheckedSource Then Return Nothing
            Return CheckedNewest
        End SyncLock
    End Function

    Friend Sub StartLauncherAutoUpdate()
        If Threading.Interlocked.Exchange(AutoUpdateStarted, 1) <> 0 Then Return
        If Settings.Get(Of Integer)("SystemSystemUpdate") = 3 OrElse
           String.IsNullOrWhiteSpace(Settings.Get(Of String)("SystemSystemUpdateSource")) Then Return
        CheckLauncherUpdate(Automatic:=True)
    End Sub

    Friend Sub CheckLauncherUpdate(Optional Automatic As Boolean = False)
        Dim Source = Settings.Get(Of String)("SystemSystemUpdateSource").Trim()
        If ActiveUpdateTask IsNot Nothing AndAlso ActiveUpdateTask.State = LoadState.Loading Then
            If Not Automatic Then Hint("正在下载启动器更新，可在任务列表查看进度或取消。")
            Return
        End If
        If Threading.Interlocked.CompareExchange(UpdateCheckRunning, 1, 0) <> 0 Then Return
        Dim Generation As Integer
        SyncLock UpdateStateLock
            Generation = UpdateGeneration
            CheckedSource = Nothing
            CheckedNewest = Nothing
        End SyncLock
        RunInNewThread(
        Sub()
            Try
                Dim Info = FetchLauncherUpdate(Source)
                Dim IsNewest = CompareLauncherVersions(Info.Version, VersionName) <= 0
                RunInUi(
                Sub()
                    If Not IsCurrentUpdateSource(Source, Generation) Then Return
                    SyncLock UpdateStateLock
                        CheckedSource = Source
                        CheckedNewest = IsNewest
                    End SyncLock
                    If IsNewest Then
                        If Not Automatic Then Hint("当前已是最新版本", HintType.Green)
                    Else
                        Dim Mode = Settings.Get(Of Integer)("SystemSystemUpdate")
                        Dim Action = LauncherUpdateAction(Automatic, Mode, Info.Critical)
                        If Action = 0 Then Return
                        If Action = 2 Then
                            DownloadLauncherUpdate(Source, Generation, Info)
                        ElseIf ReadyUpdateFolder = GetLauncherUpdateFolder(Source, Info) Then
                            DownloadLauncherUpdate(Source, Generation, Info)
                        ElseIf MyMsgBox("发现新版本：" & Info.Version &
                                 If(String.IsNullOrWhiteSpace(Info.Notes), "", vbCrLf & vbCrLf & Info.Notes),
                                 "启动器更新", "下载更新", "取消") = 1 Then
                            DownloadLauncherUpdate(Source, Generation, Info)
                        End If
                    End If
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "检查自定义更新源失败")
                RunInUi(
                Sub()
                    If IsCurrentUpdateSource(Source, Generation) Then Hint("当前更新源不可用", HintType.Red)
                End Sub)
            Finally
                Threading.Interlocked.Exchange(UpdateCheckRunning, 0)
            End Try
        End Sub, "LauncherUpdateCheck")
    End Sub

    Friend Function IsCurrentUpdateSource(Source As String, Generation As Integer) As Boolean
        If Source <> Settings.Get(Of String)("SystemSystemUpdateSource").Trim() Then Return False
        SyncLock UpdateStateLock
            Return Generation = UpdateGeneration
        End SyncLock
    End Function

    '0: 无提示，1: 下载前询问，2: 自动下载。
    Friend Function LauncherUpdateAction(Automatic As Boolean, Mode As Integer, Critical As Boolean) As Integer
        If Not Automatic Then Return 1
        If Mode = 0 Then Return 2
        If Mode = 1 OrElse (Mode = 2 AndAlso Critical) Then Return 1
        Return 0
    End Function

    Private Sub DownloadLauncherUpdate(Source As String, Generation As Integer, Info As LauncherUpdateInfo, Optional TargetPath As String = Nothing)
        If Not IsCurrentUpdateSource(Source, Generation) Then Return
        If ActiveUpdateTask IsNot Nothing AndAlso ActiveUpdateTask.State = LoadState.Loading Then Return
        Try
            Dim Folder = GetLauncherUpdateFolder(Source, Info, TargetPath)
            Dim ZipPath = IO.Path.Combine(Folder, "update.zip")
            IO.Directory.CreateDirectory(Folder)
            'Only reuse a complete, verified archive; a failed check forces a fresh download.
            If IO.File.Exists(ZipPath) AndAlso Not String.Equals(LauncherUpdateHash(ZipPath), Info.Sha256, StringComparison.OrdinalIgnoreCase) Then IO.File.Delete(ZipPath)
            Dim Steps As New List(Of LoaderBase)
            If Not IO.File.Exists(ZipPath) Then
                Steps.Add(New LoaderDownload("下载启动器更新包", New List(Of NetFile) From {New NetFile({Info.Url}, ZipPath)}) With {.ProgressWeight = 10})
            End If
            Steps.Add(New LoaderTask(Of Integer, Integer)("校验启动器更新", Sub(Task)
                If Task.IsCanceled OrElse Not IsCurrentUpdateSource(Source, Generation) Then Throw New OperationCanceledException
                PrepareLauncherUpdate(Folder, Info, Source, TargetPath)
                If Task.IsCanceled OrElse Not IsCurrentUpdateSource(Source, Generation) Then Throw New OperationCanceledException
            End Sub))
            Dim Loader As New LoaderCombo(Of Integer)("更新启动器 " & Info.Version, Steps)
            ActiveUpdateTask = Loader
            Loader.OnStateChanged = Sub()
                If Not IsCurrentUpdateSource(Source, Generation) Then Return
                Select Case Loader.State
                    Case LoadState.Finished
                        ReadyUpdateFolder = Folder
                        ShowLauncherInstallPrompt(Source, Generation, Info)
                    Case LoadState.Failed
                        Logger.Warn(Loader.Error, "启动器更新下载或校验失败")
                        Hint("启动器更新下载或校验失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                    Case LoadState.Canceled
                        Hint("已取消启动器更新下载。")
                End Select
            End Sub
            Loader.Start(1)
            LoaderTaskbarAdd(Loader)
        Catch ex As Exception
            Logger.Warn(ex, "准备启动器更新下载失败")
            Hint("无法准备启动器更新下载：" & ex.GetDisplay(False), HintType.Red)
        End Try
    End Sub

    Private Sub ShowLauncherInstallPrompt(Source As String, Generation As Integer, Info As LauncherUpdateInfo)
        If Not IsCurrentUpdateSource(Source, Generation) Then Return
        If MyMsgBox("更新已下载并校验完成：" & Info.Version & vbCrLf & "是否立即重启并安装？选择稍后不会自动安装。" &
                    If(String.IsNullOrWhiteSpace(Info.Notes), "", vbCrLf & vbCrLf & Info.Notes), "启动器更新", "立即重启更新", "稍后") = 1 AndAlso
           IsCurrentUpdateSource(Source, Generation) Then RestartWithLauncherUpdate()
    End Sub

    Friend Sub RestartWithLauncherUpdate()
        If String.IsNullOrEmpty(ReadyUpdateFolder) Then Return
        If HasDownloadingTask() OrElse McLaunchLoader.State = LoadState.Loading Then
            Hint("请等待下载或游戏启动任务完成后，再点击检查更新安装。", HintType.Red)
            Return
        End If
        Try
            Dim Folder = ReadyUpdateFolder
            FrmMain.EndProgram(True, Sub() StartLauncherUpdateHelper(Folder))
        Catch ex As Exception
            Logger.Warn(ex, "启动更新安装程序失败")
            Hint("无法安装启动器更新：" & ex.GetDisplay(False), HintType.Red)
        End Try
    End Sub

    Friend Class LauncherUpdateInfo
        Public Property Version As String
        Public Property Url As String
        Public Property Sha256 As String
        Public Property Notes As String
        Public Property Critical As Boolean
    End Class

    Friend Function FetchLauncherUpdate(Source As String) As LauncherUpdateInfo
        ValidateUpdateUrl(Source)
        Return ParseLauncherUpdate(NetRequestByClient(Source, Timeout:=10000, RequireJson:=True, Encoding:=Text.Encoding.UTF8,
                                                     Headers:={{"Cache-Control", "no-cache"}}))
    End Function

    Private Sub ValidateUpdateUrl(Value As String)
        Dim Address As Uri = Nothing
        If String.IsNullOrWhiteSpace(Value) OrElse Not Uri.TryCreate(Value, UriKind.Absolute, Address) OrElse
           (Address.Scheme <> Uri.UriSchemeHttp AndAlso Address.Scheme <> Uri.UriSchemeHttps) OrElse
           String.IsNullOrEmpty(Address.Host) Then Throw New FormatException("更新地址必须是完整的 HTTP 或 HTTPS 地址。")
    End Sub

    Friend Function ParseLauncherUpdate(Content As String) As LauncherUpdateInfo
        Dim Json = JObject.Parse(Content)
        Dim Info As New LauncherUpdateInfo With {
            .Version = ReadUpdateString(Json, "version"),
            .Url = ReadUpdateString(Json, "url"),
            .Sha256 = ReadUpdateString(Json, "sha256"),
            .Notes = ReadUpdateString(Json, "notes", OptionalField:=True)}
        ParseLauncherVersion(Info.Version)
        If Json("critical") IsNot Nothing Then
            If Json("critical").Type <> JTokenType.Boolean Then Throw New FormatException("critical 必须为布尔值。")
            Info.Critical = Json("critical").Value(Of Boolean)()
        End If
        ValidateUpdateUrl(Info.Url)
        If Not Regex.IsMatch(Info.Sha256, "\A[0-9a-fA-F]{64}\z") Then Throw New FormatException("更新文件的 SHA-256 格式无效。")
        Return Info
    End Function

    Private Function ReadUpdateString(Json As JObject, Name As String, Optional OptionalField As Boolean = False) As String
        Dim Value = Json(Name)
        If Value Is Nothing AndAlso OptionalField Then Return ""
        If Value Is Nothing OrElse Value.Type <> JTokenType.String Then Throw New FormatException("更新信息缺少字符串字段：" & Name)
        Dim Result = Value.Value(Of String)().Trim()
        If Not OptionalField AndAlso Result.Length = 0 Then Throw New FormatException("更新信息字段为空：" & Name)
        Return Result
    End Function

    Private Function ParseLauncherVersion(Value As String) As Integer()
        Dim Match = Regex.Match(Value, "\A([0-9]+)\.([0-9]+)\.([0-9]+)(?:\.([0-9]+))?-fix-download\.([1-9][0-9]*)\z")
        If Not Match.Success Then Throw New FormatException("更新版本格式无效。")
        Dim Parts(4) As Integer
        For Index = 0 To 4
            Dim Part = Match.Groups(Index + 1).Value
            If Part = "" Then Part = "0"
            If Not Integer.TryParse(Part, Parts(Index)) Then Throw New FormatException("更新版本数字超出范围。")
        Next
        Return Parts
    End Function

    Friend Function CompareLauncherVersions(Left As String, Right As String) As Integer
        Dim LeftParts = ParseLauncherVersion(Left)
        Dim RightParts = ParseLauncherVersion(Right)
        For Index = 0 To 4
            Dim Result = LeftParts(Index).CompareTo(RightParts(Index))
            If Result <> 0 Then Return Result
        Next
        Return 0
    End Function
End Module
