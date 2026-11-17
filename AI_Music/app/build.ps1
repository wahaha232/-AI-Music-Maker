param(
    [switch]$Preflight
)

$ErrorActionPreference = 'Stop'
$appDir = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found: $csc" }

$sources = @(Get-ChildItem -LiteralPath (Join-Path $appDir 'src') -Filter '*.cs' |
    Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "no .cs files under $appDir\src" }

$exe = Join-Path $appDir 'AIMusicGenerator.exe'
$manifest = Join-Path $appDir 'app.manifest'
$icon = Join-Path $appDir 'app.ico'
$lib = Join-Path $appDir 'lib'
& $csc /nologo /codepage:65001 /target:winexe /optimize+ "/out:$exe" `
    /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll `
    /r:System.Drawing.dll /r:System.Web.Extensions.dll "/win32manifest:$manifest" "/win32icon:$icon" `
    "/r:$lib\NAudio.dll" "/r:$lib\TagLibSharp.dll" $sources
if ($LASTEXITCODE -ne 0) { throw "compile failed: $LASTEXITCODE" }

Copy-Item (Join-Path $lib '*.dll') $appDir -Force
& $exe --selftest
if ($LASTEXITCODE -ne 0) { throw "selftest failed: $LASTEXITCODE" }

$python = Join-Path (Split-Path -Parent $appDir) '..\AI_Music.venv\Scripts\python.exe'
$tests = Join-Path (Split-Path -Parent $appDir) 'tests'
& $python -m unittest discover -s $tests
if ($LASTEXITCODE -ne 0) { throw "unittest failed: $LASTEXITCODE" }

if ($Preflight) {
    $backend = Join-Path $appDir 'generate_backend.py'
    & $python $backend --preflight
    if ($LASTEXITCODE -ne 0) { throw "preflight failed: $LASTEXITCODE" }
    $lockCheck = Join-Path (Split-Path -Parent $appDir) 'scripts\check_lock.py'
    & $python $lockCheck
    if ($LASTEXITCODE -ne 0) { throw "requirements lock check failed: $LASTEXITCODE" }
}

Write-Host "OK: $exe"
