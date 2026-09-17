param(
    [string]$Cs2 = "",
    [string]$Addon = "jbforsaken_ui",
    [switch]$NoLocalInstall
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$Name = "jbf_menu"

if (-not $Cs2) {
    $roots = @("C:\Program Files (x86)\Steam")
    $vdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"

    if (Test-Path $vdf) {
        $found = Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' -AllMatches
        foreach ($line in $found) {
            foreach ($m in $line.Matches) {
                $roots += $m.Groups[1].Value.Replace('\\', '\')
            }
        }
    }

    foreach ($root in $roots) {
        $candidate = Join-Path $root "steamapps\common\Counter-Strike Global Offensive"
        if (Test-Path $candidate) {
            $Cs2 = $candidate
            break
        }
    }
}

if (-not $Cs2 -or -not (Test-Path $Cs2)) {
    Write-Output "CS2 not found. Run with -Cs2 '<path to Counter-Strike Global Offensive>'."
    exit 1
}

$compiler = Join-Path $Cs2 "game\bin\win64\resourcecompiler.exe"
if (-not (Test-Path $compiler)) {
    Write-Output "Workshop Tools are not installed: resourcecompiler.exe was not found."
    exit 1
}

$contentAddon = Join-Path $Cs2 "content\csgo_addons\$Addon"
$gameAddon = Join-Path $Cs2 "game\csgo_addons\$Addon"
$layoutDir = Join-Path $contentAddon "panorama\layout\custom_game"
$styleDir = Join-Path $contentAddon "panorama\styles\custom_game"
New-Item -ItemType Directory -Force -Path $layoutDir, $styleDir | Out-Null

Copy-Item (Join-Path $src "layout\$Name.xml") $layoutDir -Force
Copy-Item (Join-Path $src "styles\$Name.css") $styleDir -Force

& $compiler -i (Join-Path $layoutDir "$Name.xml") -r
if ($LASTEXITCODE -ne 0) { throw "Layout compilation failed." }

& $compiler -i (Join-Path $styleDir "$Name.css") -r
if ($LASTEXITCODE -ne 0) { throw "Stylesheet compilation failed." }

$outLayout = Join-Path $gameAddon "panorama\layout\custom_game\$Name.vxml_c"
$outStyle = Join-Path $gameAddon "panorama\styles\custom_game\$Name.vcss_c"

if (-not (Test-Path $outLayout) -or -not (Test-Path $outStyle)) {
    throw "Compilation finished without the expected .vxml_c/.vcss_c files."
}

Write-Output ""
Write-Output "JBForsaken UI addon compiled successfully."
Write-Output "Addon source:   $contentAddon"
Write-Output "Addon compiled: $gameAddon"
Write-Output "Layout:         $outLayout"
Write-Output "Style:          $outStyle"

if (-not $NoLocalInstall) {
    $clientLayoutDir = Join-Path $Cs2 "game\csgo\panorama\layout\custom_game"
    $clientStyleDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game"
    New-Item -ItemType Directory -Force -Path $clientLayoutDir, $clientStyleDir | Out-Null
    Copy-Item $outLayout (Join-Path $clientLayoutDir "$Name.vxml_c") -Force
    Copy-Item $outStyle (Join-Path $clientStyleDir "$Name.vcss_c") -Force

    Write-Output "Local client copy updated. Restart CS2 before testing."
}

Write-Output ""
Write-Output "Workshop addon name: $Addon"
Write-Output "Publish/update this addon through Counter-Strike 2 Workshop Tools."
