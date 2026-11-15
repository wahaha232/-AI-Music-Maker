# Fetch the official PyTorch CUDA 12.8 wheels (Windows / CPython 3.12) that the
# ACE-Step 1.5 Windows pin requires.  Uses fetch_file.py (parallel range
# downloader) because single-stream downloads stall on this network.
# Run:  powershell -NoProfile -ExecutionPolicy Bypass -File "<project>\scripts\fetch_pytorch_wheels.ps1"
#       (all paths are derived from this script's location, so the project folder
#        can be moved without editing anything)

$ErrorActionPreference = 'Stop'
$proj   = Split-Path -Parent $PSScriptRoot    # <workspace>\AI_Music
$py     = Join-Path $proj 'tools\hfvenv\Scripts\python.exe'
$fetch  = Join-Path $proj 'scripts\fetch_file.py'
$outDir = Join-Path $proj 'wheels'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$wheels = @(
    @{ name = 'torch-2.7.1+cu128-cp312-cp312-win_amd64.whl';
       sha  = '2bb8c05d48ba815b316879a18195d53a6472a03e297d971e916753f8e1053d30' },
    @{ name = 'torchaudio-2.7.1+cu128-cp312-cp312-win_amd64.whl';
       sha  = '4586e3106701b06a4f9377f5c1da9e1d8555e16bd58fd7d810aa3f6cf50bd713' },
    @{ name = 'torchvision-0.22.1+cu128-cp312-cp312-win_amd64.whl';
       sha  = '650561ba326d21021243f5e064133dc62dc64d52f79623db5cd76637a9665f96' }
)

foreach ($wheel in $wheels) {
    $url = 'https://download.pytorch.org/whl/cu128/' + [uri]::EscapeDataString($wheel.name)
    Write-Host "[wheels] fetching $($wheel.name)"
    & $py $fetch --url $url --out (Join-Path $outDir $wheel.name) --sha256 $wheel.sha --workers 24
    if ($LASTEXITCODE -ne 0) { throw "fetch failed: $($wheel.name)" }
}

Write-Host '[wheels] all official PyTorch CUDA 12.8 wheels fetched and SHA256-verified.'
