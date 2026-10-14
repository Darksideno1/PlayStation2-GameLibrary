$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ProjectRoot
try {
    python -m unittest discover -s tests -v
    if ($LASTEXITCODE -ne 0) { throw 'Python unit tests failed.' }
    python tests\validate_static.py
    if ($LASTEXITCODE -ne 0) { throw 'Static validation failed.' }
    dotnet build .\src\PlayStation2GameLibrary\PlayStation2GameLibrary.csproj --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw 'WPF build failed.' }
} finally { Pop-Location }
