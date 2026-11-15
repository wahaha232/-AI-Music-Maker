# Shared PowerShell helpers for the AI_Music scripts.
# Dot-source from a script: . (Join-Path $PSScriptRoot '_common.ps1')

# Run a native executable with stdout/stderr captured to temp files, so
# PowerShell 5.1 never turns uv/pip progress output into NativeCommandError
# records. Returns the process exit code.
function Run-Native($exe, [string[]]$argv) {
    $tmpO = [IO.Path]::GetTempFileName()
    $tmpE = [IO.Path]::GetTempFileName()
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    & $exe @argv 1> $tmpO 2> $tmpE
    $code = $LASTEXITCODE
    $ErrorActionPreference = $saved
    foreach ($f in @($tmpO, $tmpE)) {
        Get-Content -LiteralPath $f -EA SilentlyContinue |
            Where-Object { $_ -ne '' } | ForEach-Object { Write-Host ("    " + $_) }
        Remove-Item -LiteralPath $f -Force -EA SilentlyContinue
    }
    return $code
}
