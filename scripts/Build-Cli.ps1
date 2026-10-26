$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\PstReader\PstReader.csproj'
$publishDirectory = Join-Path $projectRoot "artifacts"

dotnet publish $projectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}