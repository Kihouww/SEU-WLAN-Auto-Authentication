# Builds bin\seu-auth.exe and packages it into dist\seu-wlan-auto-auth-v<version>.zip with a SHA256 file.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

& cmd /c "`"$root\build.cmd`""
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$exe = Join-Path $root 'bin\seu-auth.exe'
$version = (Get-Item $exe).VersionInfo.ProductVersion -replace '\.0$', ''
$name = "seu-wlan-auto-auth-v$version"
$stage = Join-Path $root "dist\$name"
$zip = Join-Path $root "dist\$name.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $exe, (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $stage
Compress-Archive -Path "$stage\*" -DestinationPath $zip

$exeHash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$exeHash  seu-auth.exe`n$zipHash  $name.zip" | Set-Content (Join-Path $root "dist\$name.sha256.txt") -Encoding ascii

Write-Host "Package: $zip"
Write-Host "SHA256 seu-auth.exe: $exeHash"
Write-Host "SHA256 $name.zip: $zipHash"
