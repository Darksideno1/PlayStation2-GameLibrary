[CmdletBinding()]
param([string]$Configuration = 'Release', [string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $ProjectRoot 'src/PlayStation2GameLibrary/PlayStation2GameLibrary.csproj'
$Dist = Join-Path $ProjectRoot 'dist'
$Stage = Join-Path $Dist 'PLAYSTATION_2_GAME_LIBRARY_v0.1_BETA_PORTABLE'
$Zip = Join-Path $Dist 'PLAYSTATION_2_GAME_LIBRARY_v0.1_BETA_PORTABLE.zip'
if (-not $IsWindows -and $env:OS -ne 'Windows_NT') { throw 'Run this release script on Windows 10/11.' }
if (-not (Test-Path $Project)) { throw "Project file not found: $Project" }
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCommand) { throw 'The .NET CLI was not found.' }
$sdks = & $dotnetCommand.Source --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdks | Select-String '^8\.')) { throw '.NET 8 SDK is required.' }
$pythonCommand = $null
$pythonPrefixArgs = @()
foreach ($candidate in @('python','python3')) {
    $found = Get-Command $candidate -ErrorAction SilentlyContinue
    if ($found) { $pythonCommand = $found.Source; break }
}
if (-not $pythonCommand) {
    $launcher = Get-Command 'py' -ErrorAction SilentlyContinue
    if ($launcher) { $pythonCommand = $launcher.Source; $pythonPrefixArgs = @('-3') }
}
if (-not $pythonCommand) { throw 'Python was not found. Install Python 3 to run release tests.' }
& $pythonCommand @pythonPrefixArgs --version
if ($LASTEXITCODE -ne 0) { throw 'Python interpreter could not run.' }
Push-Location $ProjectRoot
try {
    & $pythonCommand @pythonPrefixArgs -m unittest discover -s tests -v
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -ne 0) { exit $testExitCode }
    & $pythonCommand @pythonPrefixArgs tests/validate_static.py
    $staticExitCode = $LASTEXITCODE
    if ($staticExitCode -ne 0) { exit $staticExitCode }
    if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
    foreach ($role in @('USER','ADMIN')) {
        $output = Join-Path $Stage $role
        $admin = if ($role -eq 'ADMIN') { 'true' } else { 'false' }
        & $dotnetCommand.Source publish $Project --configuration $Configuration --runtime $Runtime --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' '-p:DebugType=None' '-p:DebugSymbols=false' "-p:AdminBuild=$admin" --output $output
        if ($LASTEXITCODE -ne 0) { throw "$role publish failed" }
        if (-not (Test-Path (Join-Path $output 'PlayStation2GameLibrary.exe'))) { throw "$role executable missing" }
        $schemaTarget = Join-Path $output 'database'
        New-Item -ItemType Directory -Path $schemaTarget -Force | Out-Null
        Copy-Item (Join-Path $ProjectRoot 'database/schema.sql') (Join-Path $schemaTarget 'schema.sql') -Force
    }
    foreach ($file in @('Version.txt','CHANGELOG.txt','FUNKCIONALNOSTI.txt','README.md')) {
        if (Test-Path $file) { Copy-Item $file $Stage -Force }
    }
    Copy-Item 'docs/PORTABLE_USER_GUIDE.md' $Stage -Force
    Copy-Item 'docs/WINDOWS_FIRST_BUILD_AND_TEST.md' $Stage -Force
    @'
PLAYSTATION 2 GAME LIBRARY v0.1 BETA
Extract this archive and run USER\PlayStation2GameLibrary.exe or ADMIN\PlayStation2GameLibrary.exe.
This is a beta package; runtime functionality must still be verified on Windows.
'@ | Set-Content (Join-Path $Stage 'START_HERE.txt') -Encoding UTF8
    Compress-Archive -Path (Join-Path $Stage '*') -DestinationPath $Zip -CompressionLevel Optimal -Force
    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
        foreach ($required in @('USER/PlayStation2GameLibrary.exe','ADMIN/PlayStation2GameLibrary.exe','USER/database/schema.sql','ADMIN/database/schema.sql')) {
            if ($names -notcontains $required) { throw "ZIP missing $required" }
        }
    } finally { $archive.Dispose() }
    Write-Host "Portable package created: $Zip"
} finally { Pop-Location }
