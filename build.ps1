param([string]$MSBuildPath, [string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$originalConsoleOutputEncoding = [Console]::OutputEncoding
$originalOutputEncoding = $OutputEncoding
Push-Location $PSScriptRoot
try {
    # dotnet emits UTF-8 when redirected; PowerShell must decode it as UTF-8 too.
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    if (-not (Test-Path 'MeloongCore/Shared/MeloongCore.csproj')) {
        throw 'Missing MeloongCore. Run: git submodule update --init --recursive'
    }
    if ($PSBoundParameters.ContainsKey('MSBuildPath')) {
        if ([string]::IsNullOrWhiteSpace($MSBuildPath) -or -not (Test-Path -LiteralPath $MSBuildPath -PathType Leaf)) {
            throw 'The explicit -MSBuildPath does not point to an existing MSBuild executable.'
        }
    } else {
        $msbuildCommand = Get-Command MSBuild.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($msbuildCommand) { $MSBuildPath = $msbuildCommand.Source }
        if (-not $MSBuildPath) {
            $vswhereCandidates = @(
                foreach ($programFilesRoot in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
                    if ($programFilesRoot) { Join-Path $programFilesRoot 'Microsoft Visual Studio/Installer/vswhere.exe' }
                }
                Get-Command vswhere.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
            ) | Select-Object -Unique
            :findMSBuild foreach ($vswhere in $vswhereCandidates) {
                if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) { continue }
                # Some installations have MSBuild files but incomplete component metadata.
                foreach ($requireComponent in @($true, $false)) {
                    $queryArgs = @('-all', '-prerelease', '-products', '*', '-format', 'json')
                    if ($requireComponent) { $queryArgs += @('-requires', 'Microsoft.Component.MSBuild') }
                    $instanceJson = & $vswhere @queryArgs | Out-String
                    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($instanceJson)) { continue }
                    try { $instances = @($instanceJson | ConvertFrom-Json) }
                    catch { continue }
                    $instances = $instances | Sort-Object { [version]$_.installationVersion } -Descending
                    foreach ($instance in $instances) {
                        if (-not $instance.installationPath) { continue }
                        foreach ($relativePath in @('MSBuild/Current/Bin/MSBuild.exe', 'MSBuild/15.0/Bin/MSBuild.exe')) {
                            $candidate = Join-Path $instance.installationPath $relativePath
                            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                                $MSBuildPath = $candidate
                                break findMSBuild
                            }
                        }
                    }
                }
            }
        }
    }
    if (-not $MSBuildPath) {
        throw 'MSBuild not found after checking PATH and Visual Studio Installer instances (including Build Tools). Install Visual Studio .NET desktop build tools with MSBuild, or pass -MSBuildPath for a custom installation.'
    }
    $MSBuildPath = (Resolve-Path -LiteralPath $MSBuildPath).ProviderPath
    Write-Output "Using MSBuild: $MSBuildPath"

    # Reuse optional portable tools without modifying the system installation.
    $tooling = Join-Path $PSScriptRoot 'artifacts/tooling'
    $portableDotnet = Join-Path $tooling 'dotnet/dotnet.exe'
    $dotnetCommand = 'dotnet'
    $buildArgs = @('-t:Rebuild', '-p:Configuration=Release', '-p:Platform=AnyCPU', '-nologo', '-verbosity:minimal')
    if (Test-Path $portableDotnet) {
        $dotnetCommand = $portableDotnet
        $env:DOTNET_ROOT = Join-Path $tooling 'dotnet'
        $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
        $sdkVersion = (& $dotnetCommand --version).Trim()
        $env:MSBuildSDKsPath = Join-Path $env:DOTNET_ROOT "sdk/$sdkVersion/Sdks"
        $env:MSBuildEnableWorkloadResolver = 'false'
        $env:DOTNET_CLI_HOME = Join-Path $tooling 'cli-home'
        $env:NUGET_PACKAGES = Join-Path $tooling 'packages'
    }
    $compilerPath = Join-Path $tooling 'compilers/tasks/net472'
    if (Test-Path "$compilerPath/Microsoft.VisualBasic.Core.targets") {
        $buildArgs += "-p:RoslynTargetsPath=$compilerPath"
    }
    $referencePath = Join-Path $tooling 'net48/build/'
    if (Test-Path "$referencePath/.NETFramework/v4.8/mscorlib.dll") {
        $buildArgs += "-p:TargetFrameworkRootPath=$referencePath"
    }

    & $dotnetCommand restore PCLCS/PCLCS.csproj --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
    & $MSBuildPath 'Plain Craft Launcher 2/Plain Craft Launcher 2.vbproj' @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    $assemblyInfo = Get-Content -Encoding UTF8 'Plain Craft Launcher 2/My Project/AssemblyInfo.vb' -Raw
    $fileVersion = [regex]::Match($assemblyInfo, 'AssemblyFileVersion\("([0-9.]+)"\)').Groups[1].Value
    $versionInfo = Get-Content -Encoding UTF8 'Plain Craft Launcher 2/Modules/Base/ModBase.vb' -Raw
    $baseVersion = [regex]::Match($versionInfo, 'VersionBaseName As String = "([0-9.]+)"').Groups[1].Value
    $suffixMatch = [regex]::Match($versionInfo, 'VersionLocalSuffix As String = "([-a-zA-Z0-9.]*)"')
    if (-not $fileVersion -or -not $baseVersion -or -not $suffixMatch.Success) { throw 'Version information not found.' }
    if ($fileVersion -ne $baseVersion) { throw 'File version must match the upstream base version.' }
    $version = $baseVersion + $suffixMatch.Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $PSScriptRoot "artifacts/public/$version"
    }
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Force $output | Out-Null
    $exe = Join-Path $output 'Plain Craft Launcher 2.exe'
    Copy-Item -LiteralPath 'Plain Craft Launcher 2/obj/Release/Plain Craft Launcher 2.exe' -Destination $exe -Force
    $exeVersion = (Get-Item $exe).VersionInfo
    if ($exeVersion.FileVersion -ne $fileVersion) { throw 'EXE file version mismatch.' }
    if ($exeVersion.ProductVersion -ne $version) { throw 'EXE product version mismatch.' }
    $zip = Join-Path $output "PCL-$version.zip"
    Compress-Archive -LiteralPath $exe -DestinationPath $zip -Force
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksums = @($exe, $zip) | ForEach-Object {
        $hash = if ($_ -eq $zip) { $zipHash } else { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
        "$hash  $([IO.Path]::GetFileName($_))"
    }
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), [string[]]$checksums, [Text.UTF8Encoding]::new($false))
    $updateInfo = [ordered]@{
        version = $version
        url = ''
        sha256 = $zipHash
        notes = ''
    } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $output 'update.json'), $updateInfo, [Text.UTF8Encoding]::new($false))
    Write-Output "Artifacts: $output"
} finally {
    try {
        [Console]::OutputEncoding = $originalConsoleOutputEncoding
        $OutputEncoding = $originalOutputEncoding
    } finally {
        Pop-Location
    }
}
