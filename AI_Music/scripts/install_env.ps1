# Install the local (offline, no-WebUI) ACE-Step 1.5 Python environment.
#
# 1. waits for the PyTorch wheel fetcher to finish
# 2. installs the three official CUDA 12.8 wheels from <project>\wheels
# 3. installs the remaining requirements (PyPI) with the wheels as a fallback source
# 4. prints the CUDA / GPU verification block
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File "<project>\scripts\install_env.ps1"
#      (all paths are derived from this script's location, so the project folder
#       can be moved without editing anything)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')
$proj = Split-Path -Parent $PSScriptRoot        # <workspace>\AI_Music
$base = Split-Path -Parent $proj                # <workspace> (holds AI_Music.venv)
$uv = Join-Path $proj 'tools\uv\uv.exe'
$venvPy = Join-Path $base 'AI_Music.venv\Scripts\python.exe'
$env:UV_CACHE_DIR = Join-Path $proj '.uv-cache'

Write-Host '[install] waiting for the PyTorch wheel fetch to finish (max 120 min)...'
$deadline = (Get-Date).AddMinutes(120)
while ((Get-Date) -lt $deadline) {
    $running = Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
        Where-Object { $_.CommandLine -like '*fetch_file.py*' }
    if (-not $running) { break }
    Start-Sleep -Seconds 20
}

$wheelsDir = Join-Path $proj 'wheels'
$wheels = @(Get-ChildItem (Join-Path $wheelsDir '*.whl') -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
Write-Host "[install] found $($wheels.Count) wheel(s) in $wheelsDir"
if ($wheels.Count -lt 3) { throw "expected 3 PyTorch wheels, found $($wheels.Count)" }

Write-Host '[install] installing PyTorch wheels...'
$wheelArgs = @('pip', 'install', '--python', $venvPy) + $wheels
$code = Run-Native $uv $wheelArgs
if ($code -ne 0) { throw "PyTorch wheel install failed (exit=$code)" }

Write-Host '[install] installing remaining requirements...'
$reqArgs = @('pip', 'install', '--python', $venvPy, '-r', (Join-Path $proj 'requirements_local.txt'),
    '--index-url', 'https://download.pytorch.org/whl/cu128',
    '--extra-index-url', 'https://pypi.org/simple',
    '--index-strategy', 'unsafe-best-match',
    '--find-links', $wheelsDir)
$code = Run-Native $uv $reqArgs
if ($code -ne 0) { throw "requirements install failed (exit=$code)" }

Write-Host '[install] environment verification:'
& $venvPy -c "import sys, torch, transformers, diffusers, soundfile, einops, vector_quantize_pytorch; print('python           :', sys.version.split()[0]); print('torch            :', torch.__version__); print('torch cuda build :', torch.version.cuda); print('cuda available   :', torch.cuda.is_available()); print('gpu              :', torch.cuda.get_device_name(0)); free, total = torch.cuda.mem_get_info(); print('vram free/total  : %.2f / %.2f GB' % (free/1024**3, total/1024**3)); print('bf16 supported   :', torch.cuda.is_bf16_supported()); print('transformers     :', transformers.__version__); print('diffusers        :', diffusers.__version__)"

Write-Host '[install] DONE'
