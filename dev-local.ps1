# Local validation and optional release ZIP build. No GitHub runner needed.
param([switch]$Build)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
function Run-Python([string[]]$Arguments) {
    & python @Arguments
    if ($LASTEXITCODE -ne 0) { throw "python $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}
Push-Location $Root
try {
    Run-Python @('tools/check_versions.py')
    Run-Python @('tools/audit_docs.py')
    Run-Python @('tools/build_zips.py', '--check')
    Run-Python @('-m', 'unittest', 'discover', '-s', 'tests', '-v')
    if ($Build) { Run-Python @('tools/build_zips.py', '--release', 'dist') }
    Write-Host "Local Meshy checks passed. GitHub Actions was not used."
} finally { Pop-Location }
