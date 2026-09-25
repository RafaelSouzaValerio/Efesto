param(
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [switch]$NoVersionIncrement,
    [string]$Version
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $versionFile = Join-Path $PSScriptRoot 'Version.props'
    $savedText = Get-Content -LiteralPath $versionFile -Raw
    $savedMatch = [regex]::Match($savedText, '<VersionPrefix>([^<]+)</VersionPrefix>')
    if (-not $savedMatch.Success) { throw "VersionPrefix não encontrado em $versionFile." }
    $releaseVersion = if ([string]::IsNullOrWhiteSpace($Version)) { $savedMatch.Groups[1].Value } else { $Version.Trim() }
    if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Versão inválida: $releaseVersion. Use major.minor.patch, por exemplo 1.0.0." }
    $versionArgument = "-p:Version=$releaseVersion"
    if (-not $SkipTests) {
        dotnet test tests/Efesto.Tests/Efesto.Tests.csproj -c Release $versionArgument
        if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
    }
    dotnet publish src/Efesto/Efesto.csproj -c Release -o artifacts/Efesto $versionArgument
    if ($LASTEXITCODE -ne 0) { throw 'A publicação WinUI falhou.' }
    dotnet publish src/Efesto.Runner/Efesto.Runner.csproj -c Release -r win-x64 --self-contained true -o artifacts/Efesto/runner $versionArgument
    if ($LASTEXITCODE -ne 0) { throw 'A publicação do executor falhou.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tools\New-MinimalRelease.ps1') -PublishedPath (Join-Path $PSScriptRoot 'artifacts\Efesto') -OutputPath (Join-Path $PSScriptRoot 'artifacts\Efesto-minimal')
    if ($LASTEXITCODE -ne 0) { throw 'A publicação mínima falhou.' }

    $isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
    $iscc = if ($isccCommand) { $isccCommand.Source } else { $null }
    if (-not $iscc) {
        foreach ($candidate in @(
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        )) { if (Test-Path -LiteralPath $candidate) { $iscc = $candidate; break } }
    }
    if ($SkipInstaller) {
        Write-Host 'Instalador ignorado por -SkipInstaller.'
    } elseif ($iscc) {
        New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'artifacts\installer') -Force | Out-Null
        & $iscc "/DMyAppVersion=$releaseVersion" (Join-Path $PSScriptRoot 'installer\Efesto.iss')
        if ($LASTEXITCODE -ne 0) { throw 'A compilação do instalador falhou.' }
        Write-Host "Instalador disponível em $PSScriptRoot\artifacts\installer\Efesto-Setup-$releaseVersion.exe"
    } else {
        Write-Warning 'Inno Setup não foi encontrado. Instale o Inno Setup 6 para gerar o instalador ou use -SkipInstaller.'
    }

    if (-not $NoVersionIncrement) {
        $parts = $releaseVersion.Split('.') | ForEach-Object { [int]$_ }
        $nextVersion = "$($parts[0]).$($parts[1]).$($parts[2] + 1)"
        $updatedText = [regex]::Replace($savedText, '<VersionPrefix>[^<]+</VersionPrefix>', "<VersionPrefix>$nextVersion</VersionPrefix>")
        Set-Content -LiteralPath $versionFile -Value $updatedText -Encoding UTF8
        Write-Host "Próxima versão automática: $nextVersion. Para configurar manualmente, edite Version.props ou use -Version major.minor.patch."
    }
    Write-Host "Aplicativo disponível em $PSScriptRoot\artifacts\Efesto\Efesto.exe"
    Write-Host "Distribuição mínima disponível em $PSScriptRoot\artifacts\Efesto-minimal\Efesto.exe"
} finally { Pop-Location }
