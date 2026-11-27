# Run from the repository root with PowerShell 7, Docker and the repository SDK.
$ErrorActionPreference = 'Stop'
$baseline = 'e589d8624c80f499a8f9587623288652cc23372f'
$temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$baselineRoot = Join-Path $temporary ('trips-base-matrix-' + [Guid]::NewGuid().ToString('N'))
$source = 'tools/trips/Travel.Trips.SchemaMatrix'
New-Item -ItemType Directory -Path $baselineRoot | Out-Null
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Matrix command failed with exit code $LASTEXITCODE." } }
try {
    $archivePath = Join-Path $baselineRoot 'source.tar'
    git archive --format=tar --output=$archivePath $baseline
    Check-Exit
    tar -xf $archivePath -C $baselineRoot
    Check-Exit
    $legacyTool = Join-Path $baselineRoot $source
    New-Item -ItemType Directory -Path $legacyTool | Out-Null
    Get-ChildItem -LiteralPath $source -File | Where-Object Extension -in '.cs', '.csproj' | Copy-Item -Destination $legacyTool
    dotnet build (Join-Path $legacyTool 'Travel.Trips.SchemaMatrix.csproj') -m:1 --verbosity quiet
    Check-Exit
    dotnet build (Join-Path $source 'Travel.Trips.SchemaMatrix.csproj') -m:1 --verbosity quiet
    Check-Exit
    dotnet (Join-Path $source 'bin/Debug/net10.0/Travel.Trips.SchemaMatrix.dll') verify $baselineRoot $PSScriptRoot
    Check-Exit
} finally {
    $resolved = [IO.Path]::GetFullPath($baselineRoot)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporary.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^trips-base-matrix-[a-f0-9]{32}$') { throw 'Unexpected fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
