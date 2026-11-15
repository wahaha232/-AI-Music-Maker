$ErrorActionPreference = 'Stop'
$app = $PSScriptRoot
$exe = Join-Path $app 'AIMusicGenerator.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "missing exe: $exe" }

$workspace = Split-Path -Parent (Split-Path -Parent $app)
if (-not (Test-Path -LiteralPath (Join-Path $workspace 'AI_Music.venv\Scripts\python.exe'))) {
    $workspace = 'G:\AIMUSIC'
}
$python  = Join-Path $workspace 'AI_Music.venv\Scripts\python.exe'
$backend = Join-Path $workspace 'AI_Music\app\generate_backend.py'
$models  = Join-Path $workspace 'AI_Music\models\Ace-Step1.5'

if (-not (Test-Path -LiteralPath $python))  { throw "missing python: $python" }
if (-not (Test-Path -LiteralPath $backend)) { throw "missing backend: $backend" }
if (-not (Test-Path -LiteralPath $models))  { throw "missing models: $models" }

$shell = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath('Desktop')
$desktopLink = Join-Path $desktop 'AI Music Maker.lnk'
$link = $shell.CreateShortcut($desktopLink)
$link.TargetPath = $exe
$link.WorkingDirectory = $app
$link.IconLocation = "$exe,0"
$link.Description = 'AI Music Maker - ACE-Step 1.5'
$link.Save()

$startDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Music Maker'
New-Item -ItemType Directory -Force -Path $startDir | Out-Null
$startLink = Join-Path $startDir 'AI Music Maker.lnk'
$link = $shell.CreateShortcut($startLink)
$link.TargetPath = $exe
$link.WorkingDirectory = $app
$link.IconLocation = "$exe,0"
$link.Description = 'AI Music Maker - ACE-Step 1.5'
$link.Save()

Write-Host "[install] desktop shortcut : $desktopLink"
Write-Host "[install] start menu        : $startLink"
Write-Host "[install] workspace         : $workspace"
Write-Host '[install] DONE'
