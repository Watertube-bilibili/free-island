[CmdletBinding()]
param([string] $ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$programFilesRoot = if (${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} } else { $env:ProgramFiles }
$installed = Join-Path $programFilesRoot 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.6'
if (Test-Path -LiteralPath (Join-Path $installed 'mscorlib.dll')) { return $installed }
$cache = Join-Path $ProjectRoot 'artifacts\net46-reference-1.0.3'
$references = Join-Path $cache 'build\.NETFramework\v4.6'
if (-not (Test-Path -LiteralPath (Join-Path $references 'mscorlib.dll'))) {
    [IO.Directory]::CreateDirectory((Join-Path $ProjectRoot 'artifacts')) | Out-Null
    $package = Join-Path $ProjectRoot 'artifacts\net46-reference-1.0.3.zip'
    if (-not (Test-Path -LiteralPath $package)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net46/1.0.3/microsoft.netframework.referenceassemblies.net46.1.0.3.nupkg' -OutFile $package -TimeoutSec 90
    }
    $expected = 'DCBB79BB3868DBFBB64C643116FAA63D888EE9ECCBA0C4E965E9992ED7C4E35D'
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) { throw '微软 .NET 4.6 参考程序集校验失败，停止构建。' }
    Expand-Archive -LiteralPath $package -DestinationPath $cache -Force
}
if (-not (Test-Path -LiteralPath (Join-Path $references 'mscorlib.dll'))) { throw '缺少 .NET 4.6 参考程序集。' }
return $references
