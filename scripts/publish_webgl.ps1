# Builds PoSumo's WebGL player on THIS machine and publishes it as a GitHub Release. Publishing the
# release triggers .github/workflows/webgl.yml, which deploys the release's webgl.zip to the game's
# Azure Static Web App on a GitHub-hosted runner. No Unity licence is needed in CI.
#
#   pwsh -File scripts\publish_webgl.ps1               # EditMode tests, build, zip, publish release
#   pwsh -File scripts\publish_webgl.ps1 -NoPublish    # tests + build + zip only (local check)
#   pwsh -File scripts\publish_webgl.ps1 -SkipBuild    # publish the existing Builds/WebGL (its own stamp)
#   pwsh -File scripts\publish_webgl.ps1 -SkipTests    # build without the EditMode gate (local only)
#
# Close the Unity editor on this project first (a batch-mode Unity cannot open a project that is
# already open). The release is tagged at HEAD, so HEAD must already be pushed to origin.
# The build method comes from scripts/webgl.json; it writes Builds/WebGL.
#
# ONE VERSION STAMP. The UTC yyMMdd stamp is computed once here and handed to Unity as
# PO_BUILD_VERSION; Assets/_Project/Core/Editor/WriteBuildStamp.cs sets PlayerSettings.bundleVersion from it, so
# Application.version, the v{PRODUCT_VERSION} in index.html and the footer of diag.html (written by
# scripts/webgl_diag.py with the same value) all carry the same stamp. The script checks that.

param(
    [switch] $SkipBuild,
    [switch] $SkipTests,
    [switch] $NoPublish,
    [int] $Keep = 0          # >0: prune to the newest N webgl-* releases (opt-in; 0 never deletes a release)
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$cfg = Get-Content (Join-Path $PSScriptRoot 'webgl.json') -Raw | ConvertFrom-Json
$buildDir = Join-Path $root 'Builds\WebGL'
$zip = Join-Path $root 'Builds\webgl.zip'
New-Item -ItemType Directory -Force (Join-Path $root 'Builds') | Out-Null

$sha = (git rev-parse HEAD).Trim()
if (-not $NoPublish) {
    git fetch --quiet origin
    if (-not (git branch -r --contains $sha)) {
        throw "HEAD $($sha.Substring(0,7)) is not on origin. Push it first: the release is tagged at HEAD."
    }
    if (git status --porcelain --untracked-files=no) {
        Write-Warning 'Uncommitted changes: the build will include them, but the release is tagged at HEAD.'
    }
}

# The exact editor ProjectVersion.txt pins; no "nearest patch" fallback (a different editor rewrites
# ProjectVersion.txt and package versions, and the build would not be the one the tests ran on).
$want = ((Get-Content 'ProjectSettings\ProjectVersion.txt' | Select-String 'm_EditorVersion:') -split ':\s*')[1].Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$want\Editor\Unity.exe"

function Invoke-Unity([string[]] $arguments, [string] $log) {
    if (-not (Test-Path $unity)) { throw "Unity $want is not installed at $unity (install it with the WebGL module)." }
    # Wait on the Unity process itself, not with Start-Process -Wait: -Wait also waits for every child,
    # and Unity's licensing client (Unity.Licensing.Client.exe) outlives Unity, so -Wait can hang forever.
    $p = Start-Process -FilePath $unity -PassThru -NoNewWindow -ArgumentList (@(
        '-batchmode', '-projectPath', "`"$root`"", '-logFile', "`"$log`"") + $arguments)
    $null = $p.Handle   # cache the handle now, or ExitCode reads back empty after the exit
    $p.WaitForExit()
    return $p.ExitCode
}

if ($SkipBuild) {
    $index = Join-Path $buildDir 'index.html'
    if (-not (Test-Path $index)) { throw "No WebGL build at $buildDir." }
    $m = Select-String -Path $index -Pattern 'v(\d{6})<' | Select-Object -First 1
    if (-not $m) { throw "$index carries no v<yyMMdd> stamp; rebuild without -SkipBuild." }
    $stamp = $m.Matches[0].Groups[1].Value
    "Reusing the existing build, stamp v$stamp"
}
else {
    $stamp = (Get-Date).ToUniversalTime().ToString('yyMMdd')

    if (-not $SkipTests) {
        $results = Join-Path $root 'Builds\editmode-results.xml'
        $tlog = Join-Path $root 'Builds\editmode-tests.log'
        if (Test-Path $results) { Remove-Item $results -Force }
        "EditMode tests (Unity $want); log: $tlog"
        $code = Invoke-Unity @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testResults', "`"$results`"") $tlog
        if (-not (Test-Path $results)) { Get-Content $tlog -Tail 40; throw "EditMode tests produced no results (exit $code)." }
        $run = ([xml](Get-Content $results -Raw)).'test-run'
        "EditMode: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped (of $($run.total))"
        if ($code -ne 0 -or [int]$run.failed -gt 0) { throw "EditMode tests failed (exit $code). Results: $results" }
    }

    $log = Join-Path $root 'Builds\webgl-build.log'
    "Building WebGL v$stamp with Unity $want ($($cfg.buildMethod)); log: $log"
    $env:PO_BUILD_VERSION = $stamp
    try {
        $code = Invoke-Unity @('-quit', '-buildTarget', 'WebGL', '-executeMethod', $cfg.buildMethod) $log
    }
    finally { Remove-Item Env:PO_BUILD_VERSION -ErrorAction SilentlyContinue }
    if ($code -ne 0) { Get-Content $log -Tail 40; throw "Unity build failed (exit $code). Full log: $log" }
    if (-not (Test-Path (Join-Path $buildDir 'index.html'))) { throw "No WebGL build at $buildDir." }
    if (-not (Select-String -Path (Join-Path $buildDir 'index.html') -Pattern "v$stamp<" -Quiet)) {
        throw "Builds/WebGL/index.html does not carry v${stamp}: PlayerSettings.bundleVersion was not stamped."
    }
}

# Static Web Apps 404s .unityweb without this MIME config; it also routes /diag to diag.html.
Copy-Item (Join-Path $PSScriptRoot 'webgl\staticwebapp.config.json') $buildDir -Force
$python = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $python) { throw 'python is not on PATH (needed for scripts/webgl_diag.py).' }
& $python (Join-Path $PSScriptRoot 'webgl_diag.py') $buildDir $stamp
if ($LASTEXITCODE -ne 0) { throw "webgl_diag.py failed ($LASTEXITCODE)." }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $buildDir '*') -DestinationPath $zip -CompressionLevel Optimal
'{0}: {1:N1} MB (v{2})' -f $zip, ((Get-Item $zip).Length / 1MB), $stamp

if ($NoPublish) { 'Built and zipped; not published (-NoPublish).'; return }

$repo = (gh repo view --json nameWithOwner --jq .nameWithOwner).Trim()
$when = (Get-Date).ToUniversalTime().ToString('yyMMdd-HHmm')
$tag = "webgl-$when-$($sha.Substring(0,7))"
gh release create $tag $zip -R $repo --target $sha --title "WebGL v$stamp ($when)" --latest=false `
    --notes "Local WebGL build v$stamp of $($sha.Substring(0,7)). Publishing this release deploys it to Azure (.github/workflows/webgl.yml)."
if ($LASTEXITCODE -ne 0) { throw "gh release create failed ($LASTEXITCODE)." }
"PUBLISHED $tag  ->  deploy: https://github.com/$repo/actions/workflows/webgl.yml"

# Releases are never deleted unless asked: -Keep N prunes to the newest N webgl-* releases (and tags).
if ($Keep -le 0) { return }
$old = gh release list -R $repo --limit 100 --json tagName,createdAt |
    ConvertFrom-Json | Where-Object { $_.tagName -like 'webgl-*' } |
    Sort-Object createdAt -Descending | Select-Object -Skip $Keep
foreach ($r in $old) { gh release delete $r.tagName -R $repo --yes --cleanup-tag; "pruned $($r.tagName)" }
