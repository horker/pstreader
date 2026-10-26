#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$moduleDirectory = Join-Path $projectRoot 'artifacts/PowerShell/PstReader'
$libraryDirectory = Join-Path $moduleDirectory 'lib'

# Clean only this project's module output so previous target frameworks cannot leak into a package.
$resolvedRoot = [IO.Path]::GetFullPath($projectRoot)
$resolvedModule = [IO.Path]::GetFullPath($moduleDirectory)
if (!$resolvedModule.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The module destination is outside the project: $resolvedModule"
}
if (Test-Path -LiteralPath $resolvedModule) {
    Remove-Item -LiteralPath $resolvedModule -Recurse -Force
}

foreach ($framework in @('net472', 'netstandard2.0')) {
    dotnet publish (Join-Path $projectRoot 'src/PstReader.Core/PstReader.Core.csproj') `
        -c $Configuration -f $framework --self-contained false -o (Join-Path $libraryDirectory $framework)
    if ($LASTEXITCODE -ne 0) { throw "Module library publish failed with exit code $LASTEXITCODE." }
}

dotnet build (Join-Path $projectRoot 'src/PstReader.PowerShell.Loader/PstReader.PowerShell.Loader.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Module loader build failed with exit code $LASTEXITCODE." }
Copy-Item -LiteralPath (Join-Path $projectRoot "src/PstReader.PowerShell.Loader/bin/$Configuration/netstandard2.0/PstReader.PowerShell.Loader.dll") `
    -Destination $libraryDirectory -Force

New-Item -ItemType Directory -Path $moduleDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'src/PstReader.PowerShell/PstReader/*') -Destination $moduleDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $moduleDirectory -Force
New-Item -ItemType Directory -Path (Join-Path $moduleDirectory 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses/XstReader-MS-PL.md') `
    -Destination (Join-Path $moduleDirectory 'licenses') -Force
Write-Output (Join-Path $moduleDirectory 'PstReader.psd1')
