[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$artifactsDirectory = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$packageDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsDirectory 'package'))
$expectedPrefix = $projectRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

if (-not $artifactsDirectory.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Artifacts directory escaped the plugin project.'
}

if (Test-Path -LiteralPath $artifactsDirectory) {
    Remove-Item -LiteralPath $artifactsDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDirectory | Out-Null
dotnet build (Join-Path $projectRoot 'NyaLauncher.Clock.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

$buildDirectory = Join-Path $projectRoot 'bin\Release\net10.0'
Copy-Item -LiteralPath (Join-Path $projectRoot 'plugin.json') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $buildDirectory 'NyaLauncher.Clock.dll') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory

$unexpectedSdk = Join-Path $packageDirectory 'NyaLauncher.Plugin.Abstractions.dll'
if (Test-Path -LiteralPath $unexpectedSdk) {
    throw 'The host-provided SDK assembly must not be packaged.'
}

$archive = Join-Path $artifactsDirectory 'io.github.touristh.clock-1.0.0.zip'
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archive
$item = Get-Item -LiteralPath $archive
$sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()

[pscustomobject]@{
    Path = $item.FullName
    Size = $item.Length
    Sha256 = $sha256
}
