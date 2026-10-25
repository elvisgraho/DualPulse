# Builds the release download: a self-contained, single-file DualPulse.exe (no .NET install needed) zipped with the
# license and notices, as artifacts/DualPulse-<version>-win-x64.zip.
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts/publish"
$zip = Join-Path $root "artifacts/DualPulse-$Version-win-x64.zip"

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root "src/DualPulse.App") -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
# Debug symbols that come with the native graphics libraries.
Remove-Item (Join-Path $out "*.pdb")

Copy-Item (Join-Path $root "LICENSE") $out
Copy-Item (Join-Path $root "README.md") $out
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Output $zip
