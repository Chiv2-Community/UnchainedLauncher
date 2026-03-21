param (
    [string]$Version = $null
)

$outDir = Join-Path $PSScriptRoot "out"
if (Test-Path $outDir) {
    Remove-Item -Recurse -Force $outDir
}
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$buildArgs = @("build", "UnchainedLauncher.sln", "--configuration", "Debug")
if ($Version) {
    $buildArgs += "/p:Version=$Version"
}

Write-Host "Building solution in Debug mode..." -ForegroundColor Cyan
dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Solution build failed."
    exit $LASTEXITCODE
}

Write-Host "Publishing GUI project to $outDir..." -ForegroundColor Cyan
$publishArgs = @("publish", "UnchainedLauncher.GUI/UnchainedLauncher.GUI.csproj", "--configuration", "Debug", "--output", $outDir, "--no-build")
if ($Version) {
    $publishArgs += "/p:Version=$Version"
}
dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit $LASTEXITCODE
}

Write-Host "Done! Binaries are in: $outDir" -ForegroundColor Green