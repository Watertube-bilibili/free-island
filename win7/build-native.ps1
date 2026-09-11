[CmdletBinding()]
param(
    [switch] $SkipTests,
    [string] $CompilerDirectory = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($CompilerDirectory)) {
    $CompilerDirectory = Join-Path $projectRoot 'tools\w64devkit-i686-1.23.0\w64devkit\bin'
}
$CompilerDirectory = [System.IO.Path]::GetFullPath($CompilerDirectory)
$compiler = Join-Path $CompilerDirectory 'g++.exe'
$resourceCompiler = Join-Path $CompilerDirectory 'windres.exe'
$objectDump = Join-Path $CompilerDirectory 'objdump.exe'
foreach ($tool in @($compiler, $resourceCompiler, $objectDump)) {
    if (-not (Test-Path -LiteralPath $tool)) {
        throw "找不到原生构建工具：$tool。请按 tools\TOOLCHAIN.md 解压官方 w64devkit i686 1.23.0。"
    }
}

$buildDirectory = Join-Path $projectRoot 'artifacts\win7-native'
$outputDirectory = Join-Path $projectRoot 'dist'
$portableDirectory = Join-Path $outputDirectory 'FreeIsland-Win7'
foreach ($directory in @($buildDirectory, $outputDirectory, $portableDirectory)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
$appExecutable = Join-Path $portableDirectory 'FreeIslandWin7.exe'
$setupExecutable = Join-Path $outputDirectory 'FreeIsland-Win7-Setup-1.0.1.exe'
$zipPath = Join-Path $outputDirectory 'FreeIsland-Win7-Portable-1.0.1.zip'
$runtimeLicense = Join-Path (Split-Path -Parent $CompilerDirectory) 'COPYING.MinGW-w64-runtime.txt'
if (-not (Test-Path -LiteralPath $runtimeLicense)) { throw '编译器缺少 COPYING.MinGW-w64-runtime.txt。' }

$commonArguments = @(
    ('-B' + $CompilerDirectory + '\'), '-std=c++17', '-O2', '-s',
    '-static', '-static-libgcc', '-static-libstdc++',
    '-DUNICODE', '-D_UNICODE', '-DNOMINMAX', '-DWINVER=0x0601',
    '-D_WIN32_WINNT=0x0601', '-DNTDDI_VERSION=0x06010000',
    '-finput-charset=UTF-8', '-fexec-charset=UTF-8',
    '-Wall', '-Wextra', '-Wno-missing-field-initializers',
    '-Wl,--major-os-version,6,--minor-os-version,1',
    '-Wl,--major-subsystem-version,6,--minor-subsystem-version,1',
    '-Wl,--dynamicbase,--nxcompat,--no-insert-timestamp'
)
$guiArguments = @('-mwindows', '-municode')
$libraries = @('-lgdiplus', '-lcomctl32', '-lshell32', '-lshlwapi', '-lole32', '-loleaut32', '-luuid', '-ladvapi32', '-lwinmm', '-lgdi32', '-luser32')

function Invoke-NativeCompiler {
    param([string[]] $CompilerArguments)
    & $compiler @CompilerArguments
    if ($LASTEXITCODE -ne 0) { throw "原生 C++ 编译失败，退出代码：$LASTEXITCODE" }
}

function Invoke-ResourceCompiler {
    param([string] $InputFile, [string] $OutputFile)
    # Relative include paths avoid windres' preprocessor quoting bug in paths with spaces.
    & $resourceCompiler '-I' 'win7\resources' '-I' 'assets' '-I' 'artifacts\win7-native' '-i' $InputFile '-o' $OutputFile '-O' 'coff'
    if ($LASTEXITCODE -ne 0) { throw "Windows 资源编译失败：$InputFile" }
}

function Assert-NativeWindows7 {
    param([string] $Executable, [int] $ExpectedSubsystem = 2)
    $bytes = [System.IO.File]::ReadAllBytes($Executable)
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    if ([BitConverter]::ToUInt32($bytes, $pe) -ne 0x00004550) { throw "不是 PE 文件：$Executable" }
    $machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
    $optional = $pe + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optional)
    if ($machine -ne 0x014c -or $magic -ne 0x010b) { throw "必须生成 x86 PE32：$Executable" }
    $major = [BitConverter]::ToUInt16($bytes, $optional + 48)
    $minor = [BitConverter]::ToUInt16($bytes, $optional + 50)
    if ($major -ne 6 -or $minor -ne 1) { throw "子系统版本必须为 6.1：$Executable" }
    if ([BitConverter]::ToUInt16($bytes, $optional + 68) -ne $ExpectedSubsystem) { throw "子系统类型不匹配：$Executable" }
    $clrAddress = [BitConverter]::ToUInt32($bytes, $optional + 96 + 14 * 8)
    $clrSize = [BitConverter]::ToUInt32($bytes, $optional + 100 + 14 * 8)
    if ($clrAddress -ne 0 -or $clrSize -ne 0) { throw "发现不允许的 .NET CLR 运行时目录：$Executable" }
    $dump = @(& $objectDump '-p' $Executable)
    if ($LASTEXITCODE -ne 0) { throw "读取 PE 导入表失败：$Executable" }
    $report = Join-Path $buildDirectory ([System.IO.Path]::GetFileName($Executable) + '.imports.txt')
    [System.IO.File]::WriteAllLines($report, [string[]] $dump, $utf8)
    $imports = @($dump | ForEach-Object { if ($_ -match 'DLL Name:\s*(\S+)') { $Matches[1].ToLowerInvariant() } } | Sort-Object -Unique)
    $allowed = @('advapi32.dll', 'comctl32.dll', 'comdlg32.dll', 'gdi32.dll', 'gdiplus.dll', 'imm32.dll', 'kernel32.dll', 'msvcrt.dll', 'ole32.dll', 'oleaut32.dll', 'shell32.dll', 'shlwapi.dll', 'user32.dll', 'uxtheme.dll', 'version.dll', 'winmm.dll')
    foreach ($dll in $imports) {
        if ($allowed -notcontains $dll) { throw "导入了非允许的 Windows 7 系统 DLL：$dll ($Executable)" }
    }
    $modernOnly = '\b(GetDpiForWindow|GetDpiForMonitor|SetProcessDpiAwareness|SetProcessDpiAwarenessContext|GetSystemTimePreciseAsFileTime|SetThreadDescription|WaitOnAddress|WakeByAddressSingle|WakeByAddressAll|GetCurrentThreadStackLimits|SetThreadDpiAwarenessContext)\b'
    if (@($dump | Select-String -Pattern $modernOnly).Count -gt 0) { throw "发现 Windows 7 不支持的静态导入：$Executable" }
    Write-Host (([System.IO.Path]::GetFileName($Executable)) + '：x86 原生 / 子系统 6.1 / 无 CLR / ' + ($imports -join ', '))
}

Push-Location $projectRoot
try {
    Write-Host '正在编译 Windows 7 原生应用…'
    Invoke-ResourceCompiler -InputFile 'win7\resources\app.rc' -OutputFile 'artifacts\win7-native\app-res.o'
    Invoke-NativeCompiler -CompilerArguments ($commonArguments + $guiArguments + @('win7\src\main.cpp', 'win7\src\core.cpp', 'artifacts\win7-native\app-res.o', '-o', $appExecutable) + $libraries)
    Assert-NativeWindows7 -Executable $appExecutable

    $coreTestSource = Join-Path $projectRoot 'win7\tests\core_tests.cpp'
    if (-not $SkipTests -and (Test-Path -LiteralPath $coreTestSource)) {
        Write-Host '正在运行原生核心行为测试（不会执行关机或修改自启动）…'
        $testExecutable = Join-Path $buildDirectory 'CoreBehaviorTests.exe'
        Invoke-NativeCompiler -CompilerArguments ($commonArguments + @('-DFI_CORE_TESTING', 'win7\src\core.cpp', $coreTestSource, '-o', $testExecutable) + $libraries)
        Assert-NativeWindows7 -Executable $testExecutable -ExpectedSubsystem 3
        & $testExecutable
        if ($LASTEXITCODE -ne 0) { throw "原生核心行为测试失败：$LASTEXITCODE" }
    }

    $payloadHash = (Get-FileHash -LiteralPath $appExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText((Join-Path $buildDirectory 'payload.sha256'), $payloadHash, $utf8)
    Copy-Item -LiteralPath $runtimeLicense -Destination (Join-Path $buildDirectory 'COPYING.MinGW-w64-runtime.txt') -Force
    $setupResource = @'
#include <windows.h>
#pragma code_page(65001)
101 ICON "FreeIsland.ico"
1 RT_MANIFEST "app.manifest"
201 RCDATA "FreeIslandWin7.exe"
202 RCDATA "payload.sha256"
203 RCDATA "COPYING.MinGW-w64-runtime.txt"
1 VERSIONINFO
 FILEVERSION 1,0,1,0
 PRODUCTVERSION 1,0,1,0
 FILEFLAGSMASK VS_FFI_FILEFLAGSMASK
 FILEFLAGS 0
 FILEOS VOS_NT_WINDOWS32
 FILETYPE VFT_APP
BEGIN
 BLOCK "StringFileInfo"
 BEGIN
  BLOCK "080404b0"
  BEGIN
   VALUE "CompanyName", "Free Island\0"
   VALUE "FileDescription", "浮岛 · Windows 7 原生安装程序\0"
   VALUE "FileVersion", "1.0.1\0"
   VALUE "OriginalFilename", "FreeIsland-Win7-Setup-1.0.1.exe\0"
   VALUE "ProductName", "浮岛\0"
   VALUE "ProductVersion", "1.0.1\0"
  END
 END
 BLOCK "VarFileInfo"
 BEGIN
  VALUE "Translation", 0x0804, 1200
 END
END
'@
    [System.IO.File]::WriteAllText((Join-Path $buildDirectory 'installer.rc'), $setupResource, $utf8)
    Write-Host '正在编译原生安装程序…'
    & $resourceCompiler '-I' 'win7\resources' '-I' 'assets' '-I' 'artifacts\win7-native' '-I' 'dist\FreeIsland-Win7' '-i' 'artifacts\win7-native\installer.rc' '-o' 'artifacts\win7-native\installer-res.o' '-O' 'coff'
    if ($LASTEXITCODE -ne 0) { throw '安装程序资源编译失败。' }
    Invoke-NativeCompiler -CompilerArguments ($commonArguments + $guiArguments + @('win7\src\installer.cpp', 'artifacts\win7-native\installer-res.o', '-o', $setupExecutable) + $libraries)
    Assert-NativeWindows7 -Executable $setupExecutable

    $verifyDirectory = Join-Path $buildDirectory ('payload-verification-' + [Guid]::NewGuid().ToString('N'))
    $verifyProcess = Start-Process -FilePath $setupExecutable -ArgumentList @('--verify-payload', ('"' + $verifyDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($verifyProcess.ExitCode -ne 0) { throw "安装程序载荷校验失败：$verifyDirectory (exit $($verifyProcess.ExitCode))" }
    $extractedApp = Join-Path $verifyDirectory 'FreeIslandWin7.exe'
    if (-not (Test-Path -LiteralPath $extractedApp)) { throw '安装程序未提取预期应用文件。' }
    if ((Get-FileHash -LiteralPath $extractedApp -Algorithm SHA256).Hash.ToLowerInvariant() -ne $payloadHash) { throw '安装程序提取的应用 SHA-256 不一致。' }
    Write-Host '安装程序内嵌应用 SHA-256 校验通过。'

    Copy-Item -LiteralPath $runtimeLicense -Destination (Join-Path $portableDirectory 'COPYING.MinGW-w64-runtime.txt') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'win7\README.md') -Destination (Join-Path $portableDirectory 'README.md') -Force
    $archiveFiles = @('FreeIslandWin7.exe', 'COPYING.MinGW-w64-runtime.txt', 'README.md') | ForEach-Object { Join-Path $portableDirectory $_ }
    Compress-Archive -LiteralPath $archiveFiles -DestinationPath $zipPath -CompressionLevel Optimal -Force
    $releaseHashes = @($appExecutable, $setupExecutable, $zipPath) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($_) }
    [System.IO.File]::WriteAllLines((Join-Path $outputDirectory 'SHA256SUMS-Win7.txt'), $releaseHashes, $utf8)
    Write-Host "构建完成：$setupExecutable"
    Write-Host "免安装版：$zipPath"
}
finally {
    Pop-Location
}
