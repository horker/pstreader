[CmdletBinding()]
param()

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\PstReader\PstReader.csproj'
$publishDirectory = Join-Path $projectRoot 'artifacts\publish\win-x64'
$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot)
$resolvedPublishDirectory = [IO.Path]::GetFullPath($publishDirectory)

if (!$resolvedPublishDirectory.StartsWith(
        $resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "発行先がプロジェクト外です: $resolvedPublishDirectory"
}

if (Test-Path -LiteralPath $resolvedPublishDirectory) {
    Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
}

dotnet publish $projectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$publishedFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File)
$executable = $publishedFiles | Where-Object Name -EQ 'pstreader.exe'
$unexpectedFiles = $publishedFiles | Where-Object Name -NE 'pstreader.exe'

if ($null -eq $executable -or $unexpectedFiles.Count -ne 0) {
    throw "単一 EXE の発行結果が不正です: $publishDirectory"
}

Write-Output $executable.FullName
