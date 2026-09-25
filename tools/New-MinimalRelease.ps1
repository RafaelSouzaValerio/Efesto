param(
    [Parameter(Mandatory)] [string]$PublishedPath,
    [Parameter(Mandatory)] [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($PublishedPath)
$target = [IO.Path]::GetFullPath($OutputPath)

if (-not (Test-Path -LiteralPath (Join-Path $source 'Efesto.exe'))) {
    throw "Publicação inválida: Efesto.exe não foi encontrado em $source."
}
if (-not $target.StartsWith([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts')) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "A pasta mínima precisa ficar dentro de artifacts."
}
if ($target -eq $source) { throw 'A pasta mínima não pode ser a própria publicação.' }

if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
New-Item -ItemType Directory -Path $target | Out-Null

# The WinUI runtime is self-contained. Keep every root runtime/resource file,
# but omit development-only symbols and XML documentation.
Get-ChildItem -LiteralPath $source -File | Where-Object {
    $_.Extension -notin @('.pdb', '.xml')
} | Copy-Item -Destination $target -Force

# These folders are part of the application payload, unlike satellite language
# folders emitted by the Windows App SDK publish. Keep Portuguese resources.
foreach ($folderName in @('Assets', 'runner', 'pt-BR', 'Microsoft.UI.Xaml')) {
    $folder = Join-Path $source $folderName
    if (Test-Path -LiteralPath $folder) {
        Copy-Item -LiteralPath $folder -Destination $target -Recurse -Force
    }
}

$exe = Join-Path $target 'Efesto.exe'
$runner = Join-Path $target 'runner\Efesto.Runner.exe'
if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $runner)) {
    throw 'A publicação mínima ficou incompleta: Efesto.exe ou o executor não foi copiado.'
}

$files = @(Get-ChildItem -LiteralPath $target -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host ("Publicação mínima criada em {0} ({1} arquivos, {2:N1} MB)." -f $target, $files.Count, ($bytes / 1MB))
