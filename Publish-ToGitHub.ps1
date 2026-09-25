param(
    [string]$RemoteUrl = 'https://github.com/RafaelSouzaValerio/Efesto.git',
    [string]$Branch = 'main',
    [string]$CommitMessage = 'Initial commit',
    [switch]$SkipPush
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    throw 'Git não foi encontrado no PATH.'
}
if ([string]::IsNullOrWhiteSpace($RemoteUrl)) { throw 'Informe uma URL de repositório válida.' }
if ([string]::IsNullOrWhiteSpace($Branch)) { throw 'Informe um nome de branch válido.' }
if ([string]::IsNullOrWhiteSpace($CommitMessage)) { throw 'Informe uma mensagem de commit.' }

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)] [string[]]$Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "O comando git falhou: git $($Arguments -join ' ')"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.git'))) {
    Invoke-Git init
}

# `git remote get-url origin` returns no value when a new repository has no remotes.
# Check the remote name first so PowerShell never calls `.Trim()` on `$null`.
$remoteNames = @(& git remote)
if ($remoteNames -contains 'origin') {
    $origin = [string](& git remote get-url origin 2>$null | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($origin) -or $origin.Trim() -ne $RemoteUrl) {
        Invoke-Git remote set-url origin $RemoteUrl
    }
} else {
    Invoke-Git remote add origin $RemoteUrl
}

Invoke-Git branch -M $Branch
Invoke-Git add --all
$staged = @(git diff --cached --name-only)
if ($staged.Count -gt 0) {
    Invoke-Git commit -m $CommitMessage
} else {
    Write-Host 'Nenhuma alteração nova para commit.'
}

if ($SkipPush) {
    Write-Host "Push ignorado por -SkipPush. Repositório configurado em $RemoteUrl, branch $Branch."
} else {
    Invoke-Git push -u origin $Branch
    Write-Host "Projeto enviado para $RemoteUrl ($Branch)."
}
