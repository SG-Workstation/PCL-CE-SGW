<#
.SYNOPSIS
    PCL-CE（Plain Craft Launcher Community Edition）编译 / 发布脚本。

.DESCRIPTION
    封装与 GitHub Actions（.github/workflows/reusable-build.yml）一致的 dotnet publish 流程，
    并补充本地开发需要的能力：

      * 环境自检：.NET SDK 10、项目文件、git 仓库
      * 配置 / 架构组合构建，支持 x64 与 ARM64 一次出齐
      * 产物校验：单文件 exe 是否存在、体积、SHA256
      * 可选单元测试（PCL.Core.Test）、可选 zip 打包
      * 全程日志落盘到 artifacts\logs

    默认参数等价于 CI 的构建命令：
        dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
            -p:Configuration=CI -p:Platform=x64 -p:DeleteExistingFiles=true -o ./artifact --no-self-contained

.PARAMETER Configuration
    构建配置：CI（默认，与 CI 一致）、Debug、Beta、Release。
    CI/Debug 保留完整符号且不优化，适合排错；Beta/Release 优化且无符号。
    只有设置了 PCL_WRITE_SECRET（以及对应 PCL_* 环境变量）时，密钥/提交号才会被
    源生成器写进程序，否则产物内相关配置为空 —— 本地开发构建无需任何密钥。

.PARAMETER Platform
    目标架构：x64（默认）。ARM64 仍可显式指定（-Platform ARM64），但日常构建/发布用 x64 即可。
    AnyCPU 在单文件发布下会沿用宿主 RID（本机为 win-x64），仅建议调试期使用。

.PARAMETER Output
    显式指定输出目录，仅在只构建单个 Platform 时可用。
    省略时输出到 <OutputRoot>\PCL2_CE_<Configuration>_<Platform>。

.PARAMETER OutputRoot
    输出根目录，默认 artifacts（已被 .gitignore 忽略，不会污染 git status）。

.PARAMETER ExeName
    产物 exe 的文件名（不含 .exe），默认 PCL CE SGW。
    只重命名文件，不改程序集名与 WPF 资源 URI，因此不影响运行；
    传 'Plain Craft Launcher 2' 可保持上游原始文件名。

.PARAMETER SelfContained
    生成自包含发布（内含 .NET 运行时，目标机无需预装）。默认框架依赖（与 CI 一致）。

.PARAMETER NoSingleFile
    关闭单文件打包，输出散装目录（排查程序集加载 / 缺失 DLL 时用）。

.PARAMETER SkipRestore
    跳过 NuGet 还原，直接发布（要求之前成功还原过，否则会因缺 assets 失败）。

.PARAMETER Clean
    构建前清理各项目的 bin / obj（源生成器与 WPF 的中间产物一并清掉）。

.PARAMETER Tests
    额外编译 PCL.Core.Test 测试工程。

.PARAMETER RunTests
    编译并运行 PCL.Core.Test（隐含 Tests）。

.PARAMETER Zip
    每个产物额外打成 zip（包含完整产物目录），放在输出根目录下。

.PARAMETER Open
    构建完成后打开输出目录。

.EXAMPLE
    .\build.ps1
    默认 CI + x64，产物：artifacts\PCL2_CE_CI_x64\PCL CE SGW.exe

.EXAMPLE
    .\build.ps1 -Configuration Beta -Zip
    出 x64 的 Beta 单文件包（Release 同理）并打 zip。

.EXAMPLE
    .\build.ps1 -Configuration Debug -Tests -RunTests -Open
    调试构建 + 跑单元测试 + 打开产物目录。

.EXAMPLE
    .\build.ps1 -Clean -SelfContained -NoSingleFile
    清理后发布自包含散装目录，便于排查目标机运行时依赖问题。

.NOTES
    最低要求：Windows + .NET SDK 10。脚本兼容 Windows PowerShell 5.1 与 PowerShell 7+。
    查看全部参数：Get-Help .\build.ps1 -Detailed
#>

[CmdletBinding()]
param(
    [ValidateSet('CI', 'Debug', 'Beta', 'Release')]
    [string]$Configuration = 'CI',

    [ValidateSet('x64', 'ARM64', 'AnyCPU')]
    [string[]]$Platform = @('x64'),

    [string]$Output,

    [string]$OutputRoot = 'artifacts',

    [string]$ExeName = 'PCL CE SGW',

    [switch]$SelfContained,

    [switch]$NoSingleFile,

    [switch]$SkipRestore,

    [switch]$Clean,

    [switch]$Tests,

    [switch]$RunTests,

    [switch]$Zip,

    [switch]$Open
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$global:LASTEXITCODE = 0

try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

# ---------------------------------------------------------------- 常量与路径

$Root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Get-Location).ProviderPath }
$Root = [IO.Path]::GetFullPath($Root)

$ProjectPath = Join-Path $Root 'Plain Craft Launcher 2\Plain Craft Launcher 2.csproj'
$TestProjectPath = Join-Path $Root 'PCL.Core.Test\PCL.Core.Test.csproj'
$MetadataPath = Join-Path $Root 'Plain Craft Launcher 2\metadata.json'
$PublishExeName = 'Plain Craft Launcher 2.exe'

$script:Dotnet = $null
$script:StartTime = Get-Date
$script:Results = @()

# ---------------------------------------------------------------- 辅助函数

function Write-Step {
    param([string]$Text)
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Text)
    Write-Host "    $Text" -ForegroundColor DarkGray
}

function Get-SafeFullPath {
    # 构造路径并断言其位于仓库根目录内，避免误删仓库外内容
    param([string]$Relative)
    $full = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $full.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) `
        -and -not $full.Equals($Root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝操作仓库目录之外的路径：$full"
    }
    return $full
}

function Remove-Tree {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝删除仓库目录之外的路径：$full"
    }
    Write-Info "删除 $full"
    Remove-Item -LiteralPath $full -Recurse -Force
}

function Clear-Directory {
    # 只清空目录内容、保留目录本身：
    # 资源管理器打开着输出目录时会持有目录句柄，删除目录本身必然失败，但清空内容不受影响
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清空仓库目录之外的路径：$full"
    }
    Write-Info "清空输出目录 : $full"
    foreach ($item in (Get-ChildItem -LiteralPath $full -Force)) {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                if ($attempt -eq 3) {
                    $running = @(Get-Process | Where-Object {
                        try { $_.Path -and $_.Path.StartsWith($full, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
                    })
                    $hint = if ($running.Count -gt 0) {
                        '检测到仍在运行的进程：' + (($running | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ', ') + '，请先关闭启动器。'
                    } else {
                        '若没有程序在运行，通常是资源管理器窗口打开了该目录，关掉该窗口即可。'
                    }
                    throw "无法删除 $($item.FullName)：$($_.Exception.Message)`n$hint"
                }
                Start-Sleep -Milliseconds 500
            }
        }
    }
}

function Get-LauncherVersion {
    # 从 metadata.json 读取 2.15.1-beta.1 这类版本串，仅用于命名，读不到就返回 unknown
    try {
        $meta = Get-Content -LiteralPath $MetadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $base = [string]$meta.version.base
        $suffix = [string]$meta.version.suffix
        if ([string]::IsNullOrWhiteSpace($base)) { return 'unknown' }
        if ([string]::IsNullOrWhiteSpace($suffix)) { return $base }
        return "$base-$suffix"
    }
    catch {
        return 'unknown'
    }
}

function Invoke-DotnetStep {
    # 执行一次 dotnet 调用：回显命令、落盘日志、非零退出码立刻失败
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogPath
    )
    Write-Step $Title
    Write-Info ("dotnet " + ($Arguments -join ' '))
    & $script:Dotnet @Arguments 2>&1 | Tee-Object -FilePath $LogPath -Append
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "步骤失败：$Title（dotnet 退出码 $code，日志：$LogPath）"
    }
}

# ---------------------------------------------------------------- 环境自检

Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
Write-Host ' PCL-CE 构建脚本' -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Green
Write-Info "仓库根目录 : $Root"
Write-Info "配置       : $Configuration"
Write-Info "架构       : $($Platform -join ', ')"
Write-Info "自包含     : $(if ($SelfContained) { '是（内含运行时）' } else { '否（框架依赖，与 CI 一致）' })"
Write-Info "单文件     : $(if ($NoSingleFile) { '否' } else { '是' })"
Write-Info "产物名称   : $ExeName.exe"

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    Write-Host "找不到 dotnet 命令。请安装 .NET SDK 10：https://dotnet.microsoft.com/download" -ForegroundColor Red
    exit 2
}
$script:Dotnet = $dotnetCommand.Source

$sdkLines = @(& $script:Dotnet --list-sdks 2>$null)
$sdkVersions = @()
foreach ($line in $sdkLines) {
    if ($line -match '^\s*(\d+)\.(\d+)\.') { $sdkVersions += [version]("{0}.{1}.{2}" -f $Matches[1], $Matches[2], 0) }
}
$hasSdk10 = @($sdkVersions | Where-Object { $_.Major -ge 10 }).Count -gt 0
if (-not $hasSdk10) {
    Write-Host "本项目目标框架为 net10.0-windows，需要 .NET SDK 10（当前已安装：$($sdkVersions -join ', ')）" -ForegroundColor Red
    Write-Host '下载地址：https://dotnet.microsoft.com/download/dotnet/10.0' -ForegroundColor Red
    exit 2
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    Write-Host "找不到主工程：$ProjectPath" -ForegroundColor Red
    exit 2
}

if ($Output -and $Platform.Count -gt 1) {
    Write-Host '-Output 只能与单个 -Platform 搭配使用；多架构构建请改用 -OutputRoot。' -ForegroundColor Red
    exit 2
}

# 密钥注入状态（只打印变量名，绝不打印值）
if ($env:PCL_WRITE_SECRET) {
    $secretNames = @(Get-ChildItem env: | Where-Object {
        $_.Name -like 'PCL_*' -and $_.Name -ne 'PCL_WRITE_SECRET' -and -not [string]::IsNullOrWhiteSpace($_.Value)
    } | ForEach-Object { $_.Name })
    if (-not $env:PCL_GITHUB_SHA) {
        try {
            $sha = (& git -C $Root rev-parse HEAD 2>$null)
            if ($LASTEXITCODE -eq 0 -and $sha) { $env:PCL_GITHUB_SHA = ([string]$sha).Trim() }
        }
        catch { }
        if ($env:PCL_GITHUB_SHA) { $secretNames += 'PCL_GITHUB_SHA' }
    }
    Write-Info "密钥注入   : 已开启（$($secretNames -join ', ')）"
}
else {
    Write-Info '密钥注入   : 未开启（PCL_WRITE_SECRET 未设置，产物内密钥为空，仅影响需要密钥联网的功能）'
}

$launcherVersion = Get-LauncherVersion
$logDir = Get-SafeFullPath (Join-Path $OutputRoot 'logs')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

if ($Clean) {
    Write-Step '清理 bin / obj'
    $cleanTargets = @(
        'Plain Craft Launcher 2\bin', 'Plain Craft Launcher 2\obj',
        'PCL.Core\bin', 'PCL.Core\obj',
        'PCL.Core.SourceGenerators\bin', 'PCL.Core.SourceGenerators\obj',
        'PCL.Core.Test\bin', 'PCL.Core.Test\obj'
    )
    foreach ($target in $cleanTargets) { Remove-Tree (Get-SafeFullPath $target) }
}

New-Item -ItemType Directory -Path $logDir -Force | Out-Null

# ---------------------------------------------------------------- 主构建循环

$failed = $false
try {
    foreach ($arch in $Platform) {
        $rid = $null
        switch ($arch) {
            'x64' { $rid = 'win-x64' }
            'ARM64' { $rid = 'win-arm64' }
            'AnyCPU' { $rid = 'win-x64（宿主 RID）' }
        }

        if ($Output) {
            $outDir = [IO.Path]::GetFullPath((Join-Path $Root $Output))
        }
        else {
            $outDir = Get-SafeFullPath (Join-Path $OutputRoot ("PCL2_CE_{0}_{1}" -f $Configuration, $arch))
        }

        $logPath = Join-Path $logDir ("build_{0}_{1}_{2}.log" -f $Configuration.ToLowerInvariant(), $arch.ToLowerInvariant(), $stamp)

        Write-Host ''
        Write-Host "---- 构建 $Configuration | $arch（RID: $rid）----" -ForegroundColor Yellow
        Write-Info "输出目录   : $outDir"
        Write-Info "构建日志   : $logPath"

        $sw = [Diagnostics.Stopwatch]::StartNew()

        # 每次构建前清空目标目录内容，避免上一次的残留文件混进产物
        if (Test-Path -LiteralPath $outDir) { Clear-Directory $outDir }
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null

        if (-not $SkipRestore) {
            $restoreArgs = @(
                'restore', $ProjectPath,
                "-p:Configuration=$Configuration",
                "-p:Platform=$arch",
                '--nologo',
                '-v:m'
            )
            Invoke-DotnetStep -Title "NuGet 还原（$arch）" -Arguments $restoreArgs -LogPath $logPath
        }

        $publishArgs = @(
            'publish', $ProjectPath,
            "-p:Configuration=$Configuration",
            "-p:Platform=$arch",
            '-p:DeleteExistingFiles=true',
            '-o', $outDir,
            '--nologo',
            '-v:m'
        )
        if ($NoSingleFile) { $publishArgs += '-p:PublishSingleFile=false' }
        if ($SelfContained) { $publishArgs += '-p:SelfContained=true' } else { $publishArgs += '--no-self-contained' }
        if ($SkipRestore) { $publishArgs += '--no-restore' }

        Invoke-DotnetStep -Title "发布（$arch）" -Arguments $publishArgs -LogPath $logPath

        $sw.Stop()

        # 产物校验：publish 产出的 exe 必须真实存在
        $publishedExe = Join-Path $outDir $PublishExeName
        if (-not (Test-Path -LiteralPath $publishedExe)) {
            $listing = (Get-ChildItem -LiteralPath $outDir -File | ForEach-Object { $_.Name }) -join ', '
            throw "发布结束但未找到 $PublishExeName。目录内容：$listing"
        }

        # 按 -ExeName 重命名产物（只改文件名，程序集名与 WPF 资源 URI 不受影响）
        $targetExeName = "$ExeName.exe"
        $exePath = Join-Path $outDir $targetExeName
        if ($targetExeName -ne $PublishExeName) {
            if (Test-Path -LiteralPath $exePath) { Remove-Item -LiteralPath $exePath -Force }
            Move-Item -LiteralPath $publishedExe -Destination $exePath
            Write-Info "重命名产物 : $targetExeName"
        }

        $exeItem = Get-Item -LiteralPath $exePath
        $hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
        $sizeMb = [math]::Round($exeItem.Length / 1MB, 2)
        $fileCount = (Get-ChildItem -LiteralPath $outDir -Recurse -File | Measure-Object).Count

        $zipPath = $null
        if ($Zip) {
            $zipPath = Get-SafeFullPath (Join-Path $OutputRoot ("PCL2_CE_{0}_{1}_{2}.zip" -f $Configuration, $arch, $launcherVersion))
            if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
            Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
            Write-Info ("打包 zip   : {0}（{1} MB）" -f $zipPath, [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2))
        }

        Write-Host ("    构建成功：{0}（{1} MB，目录内 {2} 个文件，耗时 {3:mm\:ss}）" -f $targetExeName, $sizeMb, $fileCount, $sw.Elapsed) -ForegroundColor Green
        Write-Info "SHA256     : $hash"

        $script:Results += [pscustomobject]@{
            Configuration = $Configuration
            Platform      = $arch
            File          = $exePath
            SizeMB        = $sizeMb
            SHA256        = $hash
            Elapsed       = ('{0:mm\:ss}' -f $sw.Elapsed)
            Zip           = $zipPath
            Log           = $logPath
        }

        if ($Open) { Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$outDir`"" | Out-Null }
    }

    # ------------------------------------------------------------ 单元测试
    if ($Tests -or $RunTests) {
        if (-not (Test-Path -LiteralPath $TestProjectPath)) {
            throw "找不到测试工程：$TestProjectPath"
        }
        $testLog = Join-Path $logDir ("test_{0}_{1}.log" -f $Configuration.ToLowerInvariant(), $stamp)
        if ($RunTests) {
            $testArgs = @(
                'test', $TestProjectPath,
                "-p:Configuration=$Configuration",
                "-p:Platform=$($Platform[0])",
                '--nologo',
                '-v:m'
            )
            Invoke-DotnetStep -Title '运行单元测试（PCL.Core.Test）' -Arguments $testArgs -LogPath $testLog
        }
        else {
            $testArgs = @(
                'build', $TestProjectPath,
                "-p:Configuration=$Configuration",
                "-p:Platform=$($Platform[0])",
                '--nologo',
                '-v:m'
            )
            Invoke-DotnetStep -Title '编译单元测试（PCL.Core.Test）' -Arguments $testArgs -LogPath $testLog
        }
    }
}
catch {
    $failed = $true
    Write-Host ''
    Write-Host "构建失败：$($_.Exception.Message)" -ForegroundColor Red
}

# ---------------------------------------------------------------- 汇总

$total = (Get-Date) - $script:StartTime
Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
if ($script:Results.Count -gt 0) {
    $script:Results | Format-Table -AutoSize Configuration, Platform, SizeMB, Elapsed, SHA256 | Out-String -Width 200 | Write-Host
}
if ($failed) {
    Write-Host ("失败，总耗时 {0:mm\:ss}。请检查上方错误与日志。" -f $total) -ForegroundColor Red
    exit 1
}
Write-Host ("全部完成，总耗时 {0:mm\:ss}。日志目录：{1}" -f $total, $logDir) -ForegroundColor Green
exit 0
