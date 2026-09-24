[CmdletBinding()]
param([switch] $SkipTests, [switch] $PackageOnly)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkDirectory 'csc.exe'))) {
    $frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkDirectory 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '需要 Windows 自带的 .NET Framework 4.6 或更新版本的 C# 编译器。'
}
$outputRoot = Join-Path $projectRoot 'dist'
$portableDirectory = Join-Path $outputRoot 'FreeIsland'
$buildDirectory = Join-Path $projectRoot 'artifacts\build'
[System.IO.Directory]::CreateDirectory($portableDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($buildDirectory) | Out-Null

function Invoke-Compiler {
    param([string[]] $CompilerArguments)
    & $compiler @CompilerArguments
    if ($LASTEXITCODE -ne 0) { throw "C# 编译失败，退出代码：$LASTEXITCODE" }
}

$iconPath = Join-Path $projectRoot 'assets\FreeIsland.ico'
& (Join-Path $projectRoot 'assets\Generate-Icon.ps1') -OutputPath $iconPath
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $portableDirectory 'FreeIsland.ico') -Force

$referenceDirectory = & (Join-Path $projectRoot 'tools\Get-Net46References.ps1') -ProjectRoot $projectRoot
$standardReferences = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Xml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'System.Xaml.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll')
$referenceArguments = @('/noconfig', '/nostdlib+') + @($standardReferences | ForEach-Object { '/reference:' + (Join-Path $referenceDirectory $_) })
$appExecutable = Join-Path $portableDirectory 'FreeIsland.exe'
$manifest = Join-Path $projectRoot 'src\app.manifest'
$appSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
$appArguments = @('/nologo', '/codepage:65001', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', "/out:$appExecutable", "/win32icon:$iconPath", "/win32manifest:$manifest")
$appArguments += $referenceArguments
$appArguments += $appSources
if (-not $PackageOnly) {
    Write-Host '正在编译浮岛…'
    Invoke-Compiler -CompilerArguments $appArguments
} elseif (-not (Test-Path -LiteralPath $appExecutable)) {
    throw '-PackageOnly 需要已有 dist\FreeIsland\FreeIsland.exe。'
}

$configuration = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.6" />
  </startup>
</configuration>
'@
[System.IO.File]::WriteAllText($appExecutable + '.config', $configuration, (New-Object System.Text.UTF8Encoding($false)))

$coreTestSource = Join-Path $projectRoot 'tests\CoreTests.cs'
if (-not $SkipTests -and (Test-Path -LiteralPath $coreTestSource)) {
    Write-Host '正在运行核心行为测试（不执行关机或修改自启动）…'
    $testExecutable = Join-Path $projectRoot 'artifacts\CoreBehaviorTests.exe'
    $serializationReference = Join-Path $frameworkDirectory 'System.Runtime.Serialization.dll'
    Invoke-Compiler -CompilerArguments @('/nologo', '/codepage:65001', '/define:FI_CORE_TESTING', '/target:exe', '/optimize+', "/out:$testExecutable", "/reference:$serializationReference", (Join-Path $projectRoot 'src\Core.cs'), $coreTestSource)
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw "核心行为测试失败，退出代码：$LASTEXITCODE" }
    $audioTestExecutable = Join-Path $buildDirectory 'AlertAudioTests.exe'
    Invoke-Compiler -CompilerArguments (@('/nologo', '/codepage:65001', '/target:exe', '/optimize+', "/out:$audioTestExecutable") + $referenceArguments + @((Join-Path $projectRoot 'src\AlertAudio.cs'), (Join-Path $projectRoot 'tests\AlertAudioTests.cs')))
    & $audioTestExecutable (Join-Path $buildDirectory ('audio-fixture-' + [Guid]::NewGuid().ToString('N')))
    if ($LASTEXITCODE -ne 0) { throw "Alert audio fixture tests failed: $LASTEXITCODE" }
    $aiTestExecutable = Join-Path $buildDirectory 'LocalAiTests.exe'
    $aiSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter 'LocalAi*.cs' -File | ForEach-Object FullName)
    Invoke-Compiler -CompilerArguments (@('/nologo', '/codepage:65001', '/target:exe', '/optimize+', "/out:$aiTestExecutable") + $referenceArguments + $aiSources + @((Join-Path $projectRoot 'tests\LocalAiTests.cs')))
    & $aiTestExecutable (Join-Path $buildDirectory ('ai-fixture-' + [Guid]::NewGuid().ToString('N')))
    if ($LASTEXITCODE -ne 0) { throw "Local AI fixture tests failed: $LASTEXITCODE" }
    $updateTestExecutable = Join-Path $buildDirectory 'UpdateTests.exe'
    Invoke-Compiler -CompilerArguments (@('/nologo', '/codepage:65001', '/target:exe', '/optimize+', "/out:$updateTestExecutable") + $referenceArguments + @((Join-Path $projectRoot 'src\Core.cs'), (Join-Path $projectRoot 'src\UpdateService.cs'), (Join-Path $projectRoot 'tests\UpdateTests.cs')))
    & $updateTestExecutable (Join-Path $projectRoot 'artifacts\update-tests-build-1.0.9')
    if ($LASTEXITCODE -ne 0) { throw "Automatic update fixture tests failed: $LASTEXITCODE" }
    $recurringTestExecutable = Join-Path $buildDirectory 'RecurringUpdateIntegrationTests.exe'
    Invoke-Compiler -CompilerArguments (@('/nologo', '/codepage:65001', '/target:exe', '/define:FI_CORE_TESTING', '/optimize+', "/out:$recurringTestExecutable") + $referenceArguments + @((Join-Path $projectRoot 'src\Core.cs'), (Join-Path $projectRoot 'src\UpdateService.cs'), (Join-Path $projectRoot 'tests\RecurringUpdateIntegrationTests.cs')))
    & $recurringTestExecutable (Join-Path $projectRoot 'artifacts\recurring-update-build-1.0.9')
    if ($LASTEXITCODE -ne 0) { throw "Recurring shutdown/update integration tests failed: $LASTEXITCODE" }
}

$installerDirectory = Join-Path $projectRoot 'installer'
$commonSource = Join-Path $installerDirectory 'Common.cs'
$installerAssembly = Join-Path $installerDirectory 'AssemblyInfo.cs'
$installerReferences = @('/noconfig', '/nostdlib+') + @(@('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll') | ForEach-Object { '/reference:' + (Join-Path $referenceDirectory $_) })
$uninstallExecutable = Join-Path $portableDirectory 'FreeIsland.Uninstall.exe'
Write-Host '正在编译每用户卸载器…'
Invoke-Compiler -CompilerArguments (@('/nologo', '/codepage:65001', '/target:winexe', '/platform:anycpu', '/optimize+', "/out:$uninstallExecutable", "/win32icon:$iconPath", "/win32manifest:$manifest") + $installerReferences + @($commonSource, $installerAssembly, (Join-Path $installerDirectory 'Uninstall.cs')))
$recoveryUninstaller = Join-Path $outputRoot 'FreeIsland-Uninstall-1.0.9.exe'
Copy-Item -LiteralPath $uninstallExecutable -Destination $recoveryUninstaller -Force

$payloadNames = @('FreeIsland.exe', 'FreeIsland.exe.config', 'FreeIsland.ico', 'FreeIsland.Uninstall.exe')
$payloadManifestPath = Join-Path $buildDirectory 'payload.sha256'
$payloadHashes = @($payloadNames | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $portableDirectory $_) -Algorithm SHA256).Hash + '  ' + $_ })
[System.IO.File]::WriteAllLines($payloadManifestPath, $payloadHashes, (New-Object System.Text.UTF8Encoding($false)))
$setupExecutable = Join-Path $outputRoot 'FreeIsland-Setup-1.0.9.exe'
$setupArguments = @('/nologo', '/codepage:65001', '/target:winexe', '/platform:anycpu', '/optimize+', "/out:$setupExecutable", "/win32icon:$iconPath", "/win32manifest:$manifest")
$setupArguments += $installerReferences
$setupArguments += @($commonSource, $installerAssembly, (Join-Path $installerDirectory 'Setup.cs'))
$setupArguments += '/resource:' + $payloadManifestPath + ',FreeIsland.Payload.sha256'
foreach ($name in $payloadNames) { $setupArguments += '/resource:' + (Join-Path $portableDirectory $name) + ',FreeIsland.Payload.' + $name }
Write-Host '正在打包安装程序…'
Invoke-Compiler -CompilerArguments $setupArguments

$verifyDirectory = Join-Path $buildDirectory ('payload-verification-' + [Guid]::NewGuid().ToString('N'))
# This mode only extracts and checks the embedded payload. It never installs or starts the app.
$verifyProcess = Start-Process -FilePath $setupExecutable -ArgumentList @('--verify-payload', ('"' + $verifyDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
if ($verifyProcess.ExitCode -ne 0) { throw "安装包内嵌文件验证失败。请查看 $verifyDirectory" }
foreach ($name in $payloadNames) {
    $originalHash = (Get-FileHash -LiteralPath (Join-Path $portableDirectory $name) -Algorithm SHA256).Hash
    $extractedHash = (Get-FileHash -LiteralPath (Join-Path $verifyDirectory $name) -Algorithm SHA256).Hash
    if ($originalHash -ne $extractedHash) { throw "安装包解包校验不一致：$name" }
}
Write-Host '安装包内嵌文件 SHA-256 校验通过。'

$readme = Join-Path $projectRoot 'README.md'
if (Test-Path -LiteralPath $readme) { Copy-Item -LiteralPath $readme -Destination (Join-Path $portableDirectory 'README.md') -Force }
$zipPath = Join-Path $outputRoot 'FreeIsland-Portable-1.0.9.zip'
$archiveFiles = @('FreeIsland.exe', 'FreeIsland.exe.config', 'FreeIsland.ico', 'README.md') | ForEach-Object { Join-Path $portableDirectory $_ } | Where-Object { Test-Path -LiteralPath $_ }
Compress-Archive -LiteralPath $archiveFiles -DestinationPath $zipPath -CompressionLevel Optimal -Force
$releaseHashes = @($setupExecutable, $zipPath, $recoveryUninstaller) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash + '  ' + [System.IO.Path]::GetFileName($_) }
[System.IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS.txt'), $releaseHashes, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "构建完成：$setupExecutable"
Write-Host "免安装版：$zipPath"
