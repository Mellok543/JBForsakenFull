param(
    [string]$Cs2 = "",
    [string]$Addon = "jbf_menu"
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

$layoutDir = Join-Path $Cs2 "content\csgo_addons\$Addon\panorama\layout\custom_game"
$styleDir = Join-Path $Cs2 "content\csgo_addons\$Addon\panorama\styles\custom_game"
New-Item -ItemType Directory -Force -Path $layoutDir, $styleDir | Out-Null

Copy-Item (Join-Path $src "layout\$Name.xml") $layoutDir -Force
Copy-Item (Join-Path $src "styles\$Name.css") $styleDir -Force

& $compiler -i (Join-Path $layoutDir "$Name.xml") -r
& $compiler -i (Join-Path $styleDir "$Name.css") -r

$outLayout = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\layout\custom_game\$Name.vxml_c"
$outStyle = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\styles\custom_game\$Name.vcss_c"
$clientLayoutDir = Join-Path $Cs2 "game\csgo\panorama\layout\custom_game"
$clientStyleDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game"

if (-not (Test-Path $outLayout) -or -not (Test-Path $outStyle)) {
    Write-Output "Compilation failed. Check resourcecompiler output above."
    exit 1
}

New-Item -ItemType Directory -Force -Path $clientLayoutDir, $clientStyleDir | Out-Null
Copy-Item $outLayout (Join-Path $clientLayoutDir "$Name.vxml_c") -Force
Copy-Item $outStyle (Join-Path $clientStyleDir "$Name.vcss_c") -Force

Write-Output ""
Write-Output "JBF.Menu Panorama resources compiled and copied to the local CS2 client."
Write-Output "Layout: $clientLayoutDir\$Name.vxml_c"
Write-Output "Style:  $clientStyleDir\$Name.vcss_c"
Write-Output "Restart CS2 completely before testing because Panorama resources are cached."
