[CmdletBinding()]
param([string]$Configuration = 'Release', [string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $ProjectRoot 'src/PlayStation2GameLibrary/PlayStation2GameLibrary.csproj'
$Dist = Join-Path $ProjectRoot 'dist'
$Stage = Join-Path $Dist 'PLAYSTATION_2_GAME_LIBRARY_v0.1_BETA_PORTABLE'
$Zip = Join-Path $Dist 'PLAYSTATION_2_GAME_LIBRARY_v0.1_BETA_PORTABLE.zip'
$FullSource = Join-Path $ProjectRoot 'data/PS2.data.full.json'
$FullSourceUrl = 'https://github.com/niemasd/GameDB-PS2/releases/download/2026-07-16_20-23-50/PS2.data.json'
$FullSourceSha256 = '471613adebaf40080ab9e96029fb10fea92bbe6a1af33724690e274e7235d9b8'
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
    Write-Host "Downloading pinned official GameDB-PS2 source..."
    Invoke-WebRequest -Uri $FullSourceUrl -OutFile $FullSource -UseBasicParsing
    $actualHash = (Get-FileHash -Path $FullSource -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $FullSourceSha256) { throw "Official GameDB source SHA256 mismatch. Expected $FullSourceSha256, got $actualHash" }

    & $pythonCommand @pythonPrefixArgs -m unittest discover -s tests -v
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $pythonCommand @pythonPrefixArgs tests/validate_static.py
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $pythonCommand @pythonPrefixArgs tests/validate_official_full_catalog.py --source $FullSource --report FULL_CATALOG_VALIDATION.txt
    if ($LASTEXITCODE -ne 0) { throw 'Official full GameDB catalogue validation failed.' }
    & $dotnetCommand.Source run --project tests/WindowsSmoke/WindowsSmoke.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Catalogue → Add to Collection → reopen persistence smoke test failed.' }
    & $dotnetCommand.Source run --project tests/WindowsGuiSmoke/WindowsGuiSmoke.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Windows WPF Catalog -> Add to Collection UI smoke test failed.' }

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
        $seedTarget = Join-Path $output 'data'
        New-Item -ItemType Directory -Path $seedTarget -Force | Out-Null
        Copy-Item $FullSource (Join-Path $seedTarget 'PS2.data.full.json') -Force
        # MainWindow and ADMIN both call StarterCatalogue with data/starter-catalog.json.
        # In release builds that name intentionally points at the pinned full multi-release source.
        Copy-Item $FullSource (Join-Path $seedTarget 'starter-catalog.json') -Force
        Copy-Item (Join-Path $ProjectRoot 'data/GameDB-PS2.seed.json') (Join-Path $seedTarget 'GameDB-PS2.seed.json') -Force
    }

    foreach ($folder in @('Data','Config','Media','Media/Covers')) { New-Item -ItemType Directory -Path (Join-Path $Stage $folder) -Force | Out-Null }
    'The active portable SQLite database is created here as catalogue.db.' | Set-Content (Join-Path $Stage 'Data/README.txt') -Encoding UTF8
    'Application settings are stored here as settings.json.' | Set-Content (Join-Path $Stage 'Config/README.txt') -Encoding UTF8
    'Copy front cover images into Covers. ONE GAME = ONE FRONT COVER.' | Set-Content (Join-Path $Stage 'Media/Covers/README.txt') -Encoding UTF8

    foreach ($file in @('Version.txt','CHANGELOG.txt','FUNKCIONALNOSTI.txt','TEST_CHECKLIST.txt','README.md','FULL_CATALOG_VALIDATION.txt')) {
        if (Test-Path $file) { Copy-Item $file $Stage -Force }
    }
    Copy-Item 'docs/PORTABLE_USER_GUIDE.md' $Stage -Force
    Copy-Item 'docs/WINDOWS_FIRST_BUILD_AND_TEST.md' $Stage -Force
    @'
PLAYSTATION 2 GAME LIBRARY v0.1 BETA
Extract the entire archive before running it.
Run USER\PlayStation2GameLibrary.exe for normal use or ADMIN\PlayStation2GameLibrary.exe for catalogue maintenance.
USER and ADMIN share the package-level Data, Config and Media folders.
The program no longer reuses the old %LOCALAPPDATA% test database by default.
The portable build contains a pinned, SHA256-verified official GameDB-PS2 full-source snapshot.
A fresh portable Data/catalogue.db is seeded from that full source on first launch.
'@ | Set-Content (Join-Path $Stage 'START_HERE.txt') -Encoding UTF8

    Compress-Archive -Path (Join-Path $Stage '*') -DestinationPath $Zip -CompressionLevel Optimal -Force
    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
        foreach ($required in @('USER/PlayStation2GameLibrary.exe','ADMIN/PlayStation2GameLibrary.exe','USER/database/schema.sql','ADMIN/database/schema.sql','USER/data/PS2.data.full.json','ADMIN/data/PS2.data.full.json','USER/data/starter-catalog.json','ADMIN/data/starter-catalog.json','Data/README.txt','Config/README.txt','Media/Covers/README.txt','FUNKCIONALNOSTI.txt','TEST_CHECKLIST.txt','FULL_CATALOG_VALIDATION.txt')) {
            if ($names -notcontains $required) { throw "ZIP missing $required" }
        }
    } finally { $archive.Dispose() }
    Write-Host "Portable package created: $Zip"
} finally {
    if (Test-Path $FullSource) { Remove-Item $FullSource -Force -ErrorAction SilentlyContinue }
    Pop-Location
}