# Repair the two ACE-Step virtual environments after moving the project to a new
# absolute path.  uv builds Windows launchers that embed ABSOLUTE paths, so a
# moved venv cannot start until the launchers are rebuilt.
#
# What it does
#   1. re-creates both venvs in place with `uv venv --relocatable --allow-existing`
#      (keeps Lib\site-packages, replaces the path-bound python.exe launchers)
#   2. rewrites any remaining text file that still contains the old paths
#      (`Scripts\activate*`, `pyvenv.cfg`, ...)
#   3. re-installs the packages that own console scripts so their .exe wrappers
#      (hf.exe, torchrun.exe, ...) are regenerated with the new paths
#   4. verifies python + torch + CUDA and runs the .exe wrappers
#
# Usage (run right after the workspace has been moved to a new absolute path):
#   powershell -NoProfile -ExecutionPolicy Bypass -File "G:\AIMUSIC\migrate_venv_paths.ps1"
#   ... -OldRoot 'D:\VS CODE\PROJECT\AIMUSIC\AI_Music'   # previous location, used for the text rewrite
#   ... -SkipReinstall              # only fix the interpreters, keep wrappers as-is
#
# Before this script, run retarget_workspace_paths.ps1: it rewrites the absolute
# paths in the freeze files, so the wrapper re-install below regenerates the
# console scripts straight from the new wheel location.

param(
    [string]$Root = 'G:\AIMUSIC',
    [string]$OldRoot = 'D:\VS CODE\PROJECT\AIMUSIC\AI_Music',
    [string]$OldVenv = 'D:\VS CODE\PROJECT\AIMUSIC\AI_Music.venv',
    [switch]$SkipReinstall
)

# NOTE: keep this on Continue.  uv writes its progress lines to stderr, and with
# $ErrorActionPreference='Stop' PowerShell turns those into NativeCommandError
# exceptions that abort the script even though uv exited 0.  Exit codes are
# checked explicitly below instead.
$ErrorActionPreference = 'Continue'

$proj   = Join-Path $Root 'AI_Music'
$main   = Join-Path $Root 'AI_Music.venv'
$hf     = Join-Path $proj 'tools\hfvenv'
$base   = Join-Path $proj 'tools\python\cpython-3.12-windows-x86_64-none\python.exe'
$uv     = Join-Path $proj 'tools\uv\uv.exe'
$logDir = Join-Path $proj 'logs'
$stamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
$log    = Join-Path $logDir "venv_repair_$stamp.log"
$newVenv = $main
$newRoot = $proj

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$env:UV_CACHE_DIR = Join-Path $proj '.uv-cache'

function Say($msg) {
    $line = "$msg"
    Write-Host $line
    Add-Content -LiteralPath $log -Value $line -Encoding UTF8
}

# Run a native exe with its output captured to temp files.  uv prints progress
# to stderr; PowerShell 5.1 would otherwise turn that into a mangled
# NativeCommandError block.  Returns the exit code.
function Run-Native($exe, [string[]]$argv) {
    $tmpO = [IO.Path]::GetTempFileName()
    $tmpE = [IO.Path]::GetTempFileName()
    $saved = $ErrorActionPreference
    # PowerShell 5.1 turns native stderr writes into NativeCommandError records;
    # silence that rendering, the text itself is captured in the temp files.
    $ErrorActionPreference = 'SilentlyContinue'
    & $exe @argv 1> $tmpO 2> $tmpE
    $code = $LASTEXITCODE
    $ErrorActionPreference = $saved
    foreach ($f in @($tmpO, $tmpE)) {
        Get-Content -LiteralPath $f -EA SilentlyContinue |
            Where-Object { $_ -ne '' } | ForEach-Object { Say ("    " + $_) }
        Remove-Item -LiteralPath $f -Force -EA SilentlyContinue
    }
    return $code
}

function Find-OldPath([string]$text, [string]$needle) {
    if ([string]::IsNullOrEmpty($needle)) { return $false }
    if ($text.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    $slash = $needle.Replace('\', '/')
    return ($text.IndexOf($slash, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
}

function Replace-OldPath([string]$text, [string]$old, [string]$new) {
    if ([string]::IsNullOrEmpty($old)) { return $text }
    $sb = New-Object System.Text.StringBuilder
    $pos = 0
    while ($true) {
        $i = $text.IndexOf($old, $pos, [System.StringComparison]::OrdinalIgnoreCase)
        if ($i -lt 0) { [void]$sb.Append($text.Substring($pos)); break }
        [void]$sb.Append($text.Substring($pos, $i - $pos)).Append($new)
        $pos = $i + $old.Length
    }
    return $sb.ToString()
}

Say "=== venv path repair - $stamp ==="
Say "root            : $Root"
Say "new venv path   : $newVenv"
Say "new project path: $newRoot"
Say "old venv path   : $OldVenv"
Say "old project path: $OldRoot"
Say "uv              : $uv ($(if (Test-Path $uv) { 'ok' } else { 'MISSING' }))"
Say "base interpreter: $base ($(if (Test-Path $base) { 'ok' } else { 'MISSING' }))"
if (-not (Test-Path $uv))   { throw "uv.exe not found at $uv" }
if (-not (Test-Path $base)) { throw "base python not found at $base" }
Say ""

# ---- 1. rebuild both venvs as relocatable (site-packages is preserved) ----
foreach ($venv in @($main, $hf)) {
    Say "--- uv venv --relocatable --allow-existing $(Split-Path -Leaf $venv) ---"
    $uvExit = Run-Native $uv @('venv', '--relocatable', '--allow-existing', '--python', $base, $venv)
    if ($uvExit -ne 0) { throw "uv venv failed for $venv (exit=$uvExit)" }
    $py = Join-Path $venv 'Scripts\python.exe'
    $probe = & $py -c "import sys;print(sys.executable);print(sys.prefix)" 2>&1 | Out-String
    Say ("    python.exe works: exit=$LASTEXITCODE " + ($probe.Trim() -replace "`r?`n", ' | '))
    Say ""
}

# ---- 2. text rewrite for anything still holding the old absolute paths ----
Say "--- text rewrite (old -> new) ---"
$old = @($OldVenv, $OldRoot)     # venv path first: it is the more specific one
$new = @($newVenv, $newRoot)
foreach ($venv in @($main, $hf)) {
    $files = @()
    $cfg = Join-Path $venv 'pyvenv.cfg'
    if (Test-Path $cfg) { $files += Get-Item $cfg }
    $files += Get-ChildItem (Join-Path $venv 'Scripts') -Force -File -EA SilentlyContinue |
        Where-Object { $_.Length -lt 262144 -and $_.Extension -notin '.exe', '.dll', '.pyd', '.lib' }
    foreach ($f in ($files | Sort-Object FullName -Unique)) {
        $t = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
        $stale = $false
        for ($i = 0; $i -lt $old.Count; $i++) {
            if (Find-OldPath $t $old[$i]) { $stale = $true; break }
        }
        if ($stale) {
            for ($i = 0; $i -lt $old.Count; $i++) { $t = Replace-OldPath $t $old[$i] $new[$i] }
            [IO.File]::WriteAllText($f.FullName, $t, (New-Object Text.UTF8Encoding($false)))
            Say ("    fixed " + $f.FullName)
        }
    }
}
Say ""

# ---- 3. regenerate console-script wrappers ----
if (-not $SkipReinstall) {
    # exact pins come from the freeze files, so versions cannot drift
    $freezeMain = Get-Content (Join-Path $Root 'env_freeze_main_venv.txt') -EA SilentlyContinue
    $freezeHf   = Get-Content (Join-Path $Root 'env_freeze_hfvenv.txt') -EA SilentlyContinue

    function Get-Pins($freeze, $names) {
        $out = @()
        foreach ($n in $names) {
            # match both `name==version` and `name @ file:///...`
            $hit = $freeze | Where-Object { $_ -match ('^' + [regex]::Escape($n) + '(\s*==|\s*@)') } | Select-Object -First 1
            if ($hit) { $out += $hit } else { Say "    ! no pin for $n (skipped)" }
        }
        return $out
    }

    $mainScriptPkgs = @(
        'accelerate', 'cffi', 'diffusers', 'numpy', 'fonttools', 'huggingface-hub',
        'httpx', 'httpx2', 'idna', 'sympy', 'charset-normalizer', 'tqdm',
        'transformers', 'numba', 'torch', 'torchaudio', 'torchvision'
    )
    $hfScriptPkgs = @('huggingface-hub', 'idna', 'charset-normalizer', 'tqdm')

    Say "--- regenerating console scripts (main venv) ---"
    $pins = Get-Pins $freezeMain $mainScriptPkgs
    Say ("    packages: " + ($pins -join ', '))
    $pipExit = Run-Native $uv (@('pip', 'install', '--python', (Join-Path $main 'Scripts\python.exe'),
        '--no-deps', '--reinstall', '--find-links', (Join-Path $proj 'wheels')) + $pins)
    if ($pipExit -ne 0) { Say "    ! uv pip install (main) exit=$pipExit" }
    Say ""

    Say "--- regenerating console scripts (hfvenv) ---"
    $pinsHf = Get-Pins $freezeHf $hfScriptPkgs
    Say ("    packages: " + ($pinsHf -join ', '))
    $pipExitHf = Run-Native $uv (@('pip', 'install', '--python', (Join-Path $hf 'Scripts\python.exe'),
        '--no-deps', '--reinstall') + $pinsHf)
    if ($pipExitHf -ne 0) { Say "    ! uv pip install (hfvenv) exit=$pipExitHf" }
    Say ""
}

# ---- 4. verification ----
Say "--- verification ---"
$mainPy = Join-Path $main 'Scripts\python.exe'
$check = & $mainPy -c "import sys, torch; print('exe     :', sys.executable); print('prefix  :', sys.prefix); print('python  :', sys.version.split()[0]); print('torch   :', torch.__version__); print('cuda    :', torch.cuda.is_available())" 2>&1
$check | ForEach-Object { Say ("    " + $_) }
Say ("    exit=$LASTEXITCODE")

$hfExe = Join-Path $main 'Scripts\hf.exe'
if (Test-Path $hfExe) {
    $hfOut = & $hfExe version 2>&1
    Say ("    hf.exe  : exit=$LASTEXITCODE " + (($hfOut | Select-Object -First 1) -join ''))
}
$torchrun = Join-Path $main 'Scripts\torchrun.exe'
if (Test-Path $torchrun) {
    $trOut = & $torchrun --version 2>&1
    Say ("    torchrun: exit=$LASTEXITCODE " + (($trOut | Select-Object -First 1) -join ''))
}
$hfPy = & (Join-Path $hf 'Scripts\python.exe') -c "import huggingface_hub;print('hfvenv hf_hub ' + huggingface_hub.__version__)" 2>&1
Say ("    hfvenv  : exit=$LASTEXITCODE " + (($hfPy -join ' | ')))

Say ""
Say "log: $log"
