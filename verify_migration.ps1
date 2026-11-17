# Verify that the ACE-Step environment really works from its new location.
#
#   1. no binary/text file inside either venv still references the old paths
#   2. every console-script wrapper starts (trampolines are path bound on Windows)
#   3. python / torch / CUDA / the three CUDA wheels
#   4. model files are all present under the new path
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File "G:\AIMUSIC\verify_migration.ps1"

param(
    [string]$Root = 'G:\AIMUSIC',
    [string[]]$OldPaths = @('D:\VS CODE\PROJECT\AIMUSIC\AI_Music.venv',
                            'D:\VS CODE\PROJECT\AIMUSIC\AI_Music',
                            'D:\AI_Music.venv', 'D:\AI_Music')
)

$ErrorActionPreference = 'Continue'
$proj = Join-Path $Root 'AI_Music'
$main = Join-Path $Root 'AI_Music.venv'
$hf   = Join-Path $proj 'tools\hfvenv'
$log  = Join-Path $proj 'logs\migration_verify.txt'
New-Item -ItemType Directory -Force -Path (Join-Path $proj 'logs') | Out-Null

$r = New-Object System.Collections.Generic.List[string]
function W($s) { $r.Add([string]$s); Write-Host $s }
function Find-OldPath([string]$text, [string]$needle) {
    if ([string]::IsNullOrEmpty($needle)) { return $false }
    if ($text.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    $slash = $needle.Replace('\', '/')
    return ($text.IndexOf($slash, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
}

W "ACE-Step migration verification - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
W ("=" * 72)

# ---- 1. stale old paths ----
W "[1] files still referencing the old paths"
$hits = 0
$failures = New-Object System.Collections.Generic.List[string]
function Add-Failure([string]$message) {
    $failures.Add($message)
    W ("    FAIL " + $message)
}
$scanExt = @('.exe', '.json', '.cfg', '.txt', '.pth', '.bat', '.cmd', '.ps1', '.sh', '.dat')
foreach ($venv in @($main, $hf)) {
    $cand = @()
    $cand += Get-ChildItem (Join-Path $venv 'Scripts') -Force -File -EA SilentlyContinue
    $cand += Get-ChildItem $venv -Recurse -Force -File -EA SilentlyContinue |
        Where-Object { $scanExt -contains $_.Extension.ToLower() -and $_.Length -lt 5MB }
    foreach ($f in ($cand | Sort-Object FullName -Unique)) {
        $bytes = [IO.File]::ReadAllBytes($f.FullName)
        $ascii = [Text.Encoding]::ASCII.GetString($bytes)
        foreach ($old in $OldPaths) {
            if ((Find-OldPath $ascii $old)) {
                W ("    STALE  " + $f.FullName)
                Add-Failure ("stale path: " + $f.FullName)
                $hits++
            }
        }
    }
}
if ($hits -eq 0) { W "    none - launchers, configs and metadata are clean" }
W ""

# ---- 1b. junctions (they store absolute targets, so a move breaks them) ----
W "[1b] junctions (absolute targets - these are what broke on the first move)"
$links = @(
    @{ Path = (Join-Path $proj '.venv');                                         Probe = 'Scripts\python.exe' },
    @{ Path = (Join-Path $proj 'tools\python\cpython-3.12-windows-x86_64-none'); Probe = 'python.exe' }
)
foreach ($l in $links) {
    $item = Get-Item $l.Path -Force -EA SilentlyContinue
    if (-not $item) { W ("    MISSING   " + $l.Path); Add-Failure ("missing junction: " + $l.Path); continue }
    if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        W ("    NOT-LINK  " + $l.Path); Add-Failure ("not a junction: " + $l.Path); continue
    }
    $target = @($item.Target)[0]
    $resolves = Test-Path (Join-Path $l.Path $l.Probe)
    W ("    {0,-9} {1,-56} -> {2}" -f $(if ($resolves) { 'ok' } else { 'BROKEN' }),
        $l.Path.Replace($proj, '<proj>'), $target)
    if (-not $resolves) { Add-Failure ("broken junction: " + $l.Path) }
}
W ""

# ---- 2. console-script wrappers ----
W "[2] console-script wrappers (each one must load its package)"
$probeArgs = @{
    'hf.exe'              = @('version')
    'huggingface-cli.exe' = @('version')
    'torchrun.exe'        = @('--help')
    'torchfrtrace.exe'    = @('--help')
    'accelerate.exe'      = @('--help')
    'transformers.exe'    = @('--help')
    'diffusers-cli.exe'   = @('--help')
    'numpy-config.exe'    = @('--help')
    'fonttools.exe'       = @('--help')
    'isympy.exe'          = @('--version')
    'tqdm.exe'            = @('--help')
    'numba'               = @('--help')
    'idna.exe'            = @('decode', '--help')
    'httpx.exe'           = @('--help')
    'cffi-gen-src.exe'    = @('--help')
    'tiny-agents.exe'     = @('--help')
    'ttx.exe'             = @('--help')
    'pyftsubset.exe'      = @('--help')
    'pyftmerge.exe'       = @('--help')
    'normalizer.exe'      = @('--help')
    'httpx2.exe'          = @('--help')
    'f2py.exe'            = @('--help')
}
$okCount = 0; $badCount = 0
foreach ($venv in @($main, $hf)) {
    foreach ($name in ($probeArgs.Keys | Sort-Object)) {
        $exe = Join-Path $venv (Join-Path 'Scripts' $name)
        if (-not (Test-Path $exe)) {
            if ($venv -eq $main) { Add-Failure ("missing wrapper: " + $name) }
            continue
        }
        $tmpO = [IO.Path]::GetTempFileName(); $tmpE = [IO.Path]::GetTempFileName()
        $saved = $ErrorActionPreference; $ErrorActionPreference = 'SilentlyContinue'
        & $exe @($probeArgs[$name]) 1> $tmpO 2> $tmpE
        $code = $LASTEXITCODE
        $ErrorActionPreference = $saved
        $txt = ((Get-Content $tmpO -Raw -EA SilentlyContinue) + (Get-Content $tmpE -Raw -EA SilentlyContinue))
        Remove-Item $tmpO, $tmpE -Force -EA SilentlyContinue
        $tramp = $txt -match 'uv trampoline failed'
        if ($tramp) {
            $badCount++
            W ("    BROKEN " + (Split-Path -Leaf $venv) + "\$name  (trampoline error)")
            Add-Failure ("trampoline failure: " + (Split-Path -Leaf $venv) + "\$name")
        }
        elseif ($code -ne 0) {
            $badCount++
            W ("    FAILED " + (Split-Path -Leaf $venv) + "\$name  exit=$code")
            Add-Failure ("wrapper exit $code : " + (Split-Path -Leaf $venv) + "\$name")
        }
        else { $okCount++; W ("    ok     " + (Split-Path -Leaf $venv) + "\$name  exit=$code") }
    }
}
W "    -> $okCount working, $badCount broken"
W ""

# ---- 3. interpreter + GPU ----
W "[3] interpreter / torch / CUDA"
$py = Join-Path $main 'Scripts\python.exe'
$tmpO = [IO.Path]::GetTempFileName(); $tmpE = [IO.Path]::GetTempFileName()
$saved = $ErrorActionPreference; $ErrorActionPreference = 'SilentlyContinue'
& $py -c "import sys, torch, transformers, diffusers, soundfile, einops; print('exe      :', sys.executable); print('prefix   :', sys.prefix); print('python   :', sys.version.split()[0]); print('torch    :', torch.__version__, '(cuda', str(torch.version.cuda) + ')'); print('cuda ok  :', torch.cuda.is_available()); print('gpu      :', torch.cuda.get_device_name(0)); print('transformers:', transformers.__version__); print('diffusers:', diffusers.__version__); print('soundfile:', soundfile.__version__); print('einops   :', einops.__version__)" 1> $tmpO 2> $tmpE
$ErrorActionPreference = $saved
Get-Content $tmpO -EA SilentlyContinue | ForEach-Object { W ("    " + $_) }
(Get-Content $tmpE -EA SilentlyContinue) | Where-Object { $_ } | ForEach-Object { W ("    ERR " + $_) }
Remove-Item $tmpO, $tmpE -Force -EA SilentlyContinue
$tmpO = [IO.Path]::GetTempFileName(); $tmpE = [IO.Path]::GetTempFileName()
& $py -c "import torch,sys;sys.exit(0 if torch.cuda.is_available() else 3)" 1> $tmpO 2> $tmpE
$cudaExit = $LASTEXITCODE
if ($cudaExit -ne 0) { Add-Failure ("torch CUDA unavailable (exit=$cudaExit)") }
Remove-Item $tmpO, $tmpE -Force -EA SilentlyContinue
W ""

# ---- 4. CUDA wheel origin + model files ----
W "[4] installed CUDA wheels (must point at the new wheels dir)"
$durl = Get-Content (Join-Path $main 'Lib\site-packages\torch-2.7.1+cu128.dist-info\direct_url.json') -Raw -EA SilentlyContinue
W ("    torch direct_url: " + ($durl -replace '\s+', ' '))
W ""
W "[5] model files under the new path"
$models = Join-Path $proj 'models\Ace-Step1.5'
foreach ($rel in @('acestep-v15-turbo\model.safetensors', 'acestep-5Hz-lm-1.7B\model.safetensors',
                   'Qwen3-Embedding-0.6B\model.safetensors', 'vae\diffusion_pytorch_model.safetensors')) {
    $p = Join-Path $models $rel
    $state = if (Test-Path $p) { '{0:N1} MB' -f ((Get-Item $p).Length / 1MB) } else { 'MISSING' }
    W ("    {0,12}  {1}" -f $state, $rel)
    if (-not (Test-Path $p)) { Add-Failure ("missing model file: " + $rel) }
}
$all = Get-ChildItem $models -Recurse -Force -File -EA SilentlyContinue |
    Where-Object { $_.FullName -notlike '*\.cache\*' }
W ("    total: {0} files, {1:N1} MB" -f $all.Count, (($all | Measure-Object Length -Sum).Sum / 1MB))
W ""

# ---- 6. project sources: no stale paths + valid syntax ----
W "[6] project scripts"
$skip = '\\.venv\\|\\.uv-cache\\|\\.hf-cache\\|\\models\\|\\wheels\\|\\__pycache__\\|\\logs\\|\\AI_Music_code_bundle_[^\\]*\\.txt'
$srcFiles = Get-ChildItem $proj -Recurse -File -Force -EA SilentlyContinue |
    Where-Object { $_.FullName -notmatch $skip -and $_.Length -lt 3MB -and
                   $_.Extension -in '.py', '.ps1', '.md', '.txt', '.json', '.cfg', '.toml', '.bat', '.cmd' }
$stale = $srcFiles | Select-String -Pattern ([regex]::Escape($OldPaths[1])) -EA SilentlyContinue
if (($stale | Measure-Object).Count -eq 0) {
    W ("    {0} source files scanned - no stale absolute paths" -f $srcFiles.Count)
} else {
    Add-Failure ("stale old paths in " + (($stale | Group-Object Path).Count) + " source file(s)")
    foreach ($g in ($stale | Group-Object Path)) {
        W ("    STALE " + $g.Name.Replace($proj, '<proj>'))
        foreach ($m in $g.Group) { W ("        L{0}: {1}" -f $m.LineNumber, $m.Line.Trim()) }
    }
}
$pyFiles = Get-ChildItem (Join-Path $proj 'scripts') -Filter '*.py' -File -EA SilentlyContinue
$saved = $ErrorActionPreference; $ErrorActionPreference = 'SilentlyContinue'
& (Join-Path $main 'Scripts\python.exe') -m py_compile @($pyFiles.FullName) 2>&1 |
    ForEach-Object { W ("    PYERR " + $_) }
$pyExit = $LASTEXITCODE
$ErrorActionPreference = $saved
W ("    python syntax check: {0} file(s), exit={1}" -f $pyFiles.Count, $pyExit)
if ($pyExit -ne 0) { Add-Failure ("python syntax check failed (exit=$pyExit)") }
foreach ($ps in @((Join-Path $proj 'scripts\install_env.ps1'), (Join-Path $proj 'scripts\fetch_pytorch_wheels.ps1'),
                  (Join-Path $Root 'migrate_venv_paths.ps1'), (Join-Path $Root 'verify_migration.ps1'))) {
    if (-not (Test-Path $ps)) { continue }
    $err = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($ps, [ref]$null, [ref]$err)
    $parseErrors = ($err | Measure-Object).Count
    W ("    {0,-28} powershell parse errors = {1}" -f (Split-Path -Leaf $ps), $parseErrors)
    if ($parseErrors -gt 0) { Add-Failure ("PowerShell parse errors in " + (Split-Path -Leaf $ps)) }
}

if ($failures.Count -gt 0) {
    W ""
    W ("RESULT: FAILED (" + $failures.Count + " failure(s))")
    $r | Set-Content -LiteralPath $log -Encoding UTF8
    Write-Host ""
    Write-Host "log: $log"
    exit 1
}
W ""
W "RESULT: PASS"
$r | Set-Content -LiteralPath $log -Encoding UTF8
Write-Host ""
Write-Host "log: $log"
exit 0
