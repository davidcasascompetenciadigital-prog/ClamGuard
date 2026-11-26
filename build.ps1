param(
    [switch]$Test
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path $csc)) {
    throw "No se encontró el compilador de .NET Framework 4.x: $csc"
}

$src = Get-ChildItem (Join-Path $root 'src\*.cs') | ForEach-Object { $_.FullName }
if ($src.Count -eq 0) { throw "No hay archivos .cs en src." }

$dist = Join-Path $root 'dist'
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$out = if ($Test) { Join-Path $dist 'ClamGuard.Test.exe' } else { Join-Path $dist 'ClamGuard.exe' }
$icon = Join-Path $root 'app.ico'
$manifest = Join-Path $root 'app.manifest'

$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.ServiceProcess.dll',
    'System.Runtime.Serialization.dll',
    'System.Security.dll',
    'System.Web.Extensions.dll',
    'System.Configuration.Install.dll'
)

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/out:$out",
    "/win32icon:$icon"
)

if (-not $Test) {
    $arguments += "/win32manifest:$manifest"
}

foreach ($reference in $references) {
    $path = Join-Path $fw $reference
    if (Test-Path $path) { $arguments += "/r:$path" }
}
$arguments += $src
# Incrustar archivos de idioma
$langFiles = Get-ChildItem (Join-Path $root 'lang\*.json') -ErrorAction SilentlyContinue
foreach ($lf in $langFiles) {
    $name = 'ClamGuard.lang.' + $lf.BaseName + '.json'
    $arguments += "/resource:$($lf.FullName),$name"
}

Write-Host "Compilando ClamGuard..."
& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "La compilación falló con código $LASTEXITCODE."
}

if ($Test) {
    Write-Host "Compilación de prueba: sin manifiesto, no solicita UAC."
} else {
    $bytes = [System.IO.File]::ReadAllBytes($out)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($text -notmatch 'requireAdministrator') {
        throw "El ejecutable no incluye el manifiesto requireAdministrator (UAC)."
    }
    Write-Host "UAC verificado: el ejecutable solicitará permisos de administrador."
}

if (-not $Test) {
    $langDest = Join-Path $dist 'lang'
    if (-not (Test-Path $langDest)) { New-Item -ItemType Directory -Path $langDest | Out-Null }
    Copy-Item (Join-Path $root 'lang\*.json') $langDest -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root 'README.md') $dist -Force -ErrorAction SilentlyContinue
    $zip = Join-Path $dist 'ClamGuard-1.2.0-win.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $items = @($out)
    $readme = Join-Path $dist 'README.md'
    if (Test-Path $readme) { $items += $readme }
    Compress-Archive -Path $items -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Paquete creado: $zip"
}

$size = [Math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "Listo: $out ($size KB)"
