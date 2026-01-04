$outDir = Join-Path $PSScriptRoot "out"
if (Test-Path $outDir) {
    Remove-Item -Recurse -Force $outDir
}
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Building solution in Debug mode..." -ForegroundColor Cyan
dotnet build "UnchainedLauncher.sln" --configuration Debug

if ($LASTEXITCODE -ne 0) {
    Write-Error "Solution build failed."
    exit $LASTEXITCODE
}

Write-Host "Publishing GUI project to $outDir..." -ForegroundColor Cyan
dotnet publish "UnchainedLauncher.GUI/UnchainedLauncher.GUI.csproj" `
    --configuration Debug `
    --output $outDir `
    --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit $LASTEXITCODE
}

Write-Host "Done! Binaries are in: $outDir" -ForegroundColor Green