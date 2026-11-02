# Сборка релизов: портативный zip + MSI + контрольные суммы.
#   powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1        (всё)
#   powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -SkipMsi   (только портатив)
# Результат — папка artifacts\. Подробные журналы сборки — build\*.log
param([switch]$SkipMsi)
$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $root 'MyPlayerMVVM\MyPlayer.csproj'
$wixproj  = Join-Path $root 'installer\MyPlayer.Installer.wixproj'
$build    = Join-Path $root 'build'
$out      = Join-Path $root 'artifacts'

# Версия — прямо из csproj (без зависимости от версии SDK)
$version = ([xml](Get-Content $proj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'В MyPlayer.csproj не найдена <Version>' }
$version = $version.Trim()
Write-Host "=== MyPlayer $version ===" -ForegroundColor Cyan

Remove-Item $build, $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $build, $out | Out-Null

# ---------- 1. Портативная версия ----------
Write-Host '--- Портативная версия ---' -ForegroundColor Cyan
$portableDir = Join-Path $build 'portable\MyPlayer'
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:Portable=true -o $portableDir "-flp:logfile=$build\portable.log;verbosity=normal"
if ($LASTEXITCODE) { throw "Публикация портативной версии не удалась (см. $build\portable.log)" }
if (-not (Test-Path (Join-Path $portableDir 'MyPlayer.exe'))) { throw 'MyPlayer.exe не найден после публикации' }
if (-not (Test-Path (Join-Path $portableDir 'portable.flag'))) { throw 'portable.flag не создан' }

$zip = Join-Path $out "MyPlayer-$version-portable-win-x64.zip"
Compress-Archive -Path $portableDir -DestinationPath $zip

# ---------- 2. MSI ----------
if (-not $SkipMsi) {
    Write-Host '--- Файлы для установщика ---' -ForegroundColor Cyan
    $appDir = Join-Path $build 'installer-files'
    dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $appDir "-flp:logfile=$build\installer-publish.log;verbosity=normal"
    if ($LASTEXITCODE) { throw "Публикация для установщика не удалась (см. $build\installer-publish.log)" }
    if (Test-Path (Join-Path $appDir 'portable.flag')) { Remove-Item (Join-Path $appDir 'portable.flag') -Force }

    function Build-Msi([string]$withAssoc) {
        Remove-Item (Join-Path $root 'installer\bin'), (Join-Path $root 'installer\obj') -Recurse -Force -ErrorAction SilentlyContinue
        $log = Join-Path $build "msi-assoc-$withAssoc.log"
        Write-Host "--- Сборка MSI (ассоциации файлов: $withAssoc) ---" -ForegroundColor Cyan
        dotnet build $wixproj -c Release -nologo -v:minimal "-p:ProductVersion=$version" "-p:AppDir=$appDir" "-p:WithAssoc=$withAssoc" "-flp:logfile=$log;verbosity=normal"
        return ($LASTEXITCODE -eq 0)
    }

    $ok = Build-Msi 'true'
    if (-not $ok) {
        Write-Warning 'MSI с ассоциациями файлов не собрался — пробую без них, чтобы установщик получился в любом случае.'
        $ok = Build-Msi 'false'
        if ($ok) { Write-Warning 'MSI собран БЕЗ ассоциаций файлов (mp3 двойным кликом не откроется). Журнал: build\msi-assoc-true.log' }
    }
    if (-not $ok) {
        Write-Host '--- Ошибки сборки MSI ---' -ForegroundColor Red
        Select-String -Path (Join-Path $build 'msi-assoc-*.log') -Pattern 'error' | Select-Object -Last 20 | ForEach-Object { $_.Line }
        throw 'MSI не собрался. Пришлите файлы build\msi-assoc-true.log и build\msi-assoc-false.log'
    }

    $msi = Get-ChildItem (Join-Path $root 'installer\bin') -Recurse -Filter *.msi | Select-Object -First 1
    if (-not $msi) { throw 'Сборка прошла, но .msi не найден в installer\bin' }
    Copy-Item $msi.FullName (Join-Path $out "MyPlayer-$version-win-x64.msi")
}

# ---------- 3. Контрольные суммы ----------
Get-ChildItem $out -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    "$h  $($_.Name)"
} | Set-Content (Join-Path $out 'SHA256SUMS.txt')

Write-Host ''
Write-Host "ГОТОВО. Файлы релиза:" -ForegroundColor Green
Get-ChildItem $out | ForEach-Object { Write-Host ("  {0}  ({1:N1} МБ)" -f $_.Name, ($_.Length / 1MB)) }
