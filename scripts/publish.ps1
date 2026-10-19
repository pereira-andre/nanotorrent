param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$output = Join-Path "artifacts" $Runtime
dotnet publish src/TorrentClassic.App -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Copy-Item LICENSE,README.md,THIRD-PARTY-NOTICES.md $output
Copy-Item licenses (Join-Path $output "licenses") -Recurse -Force
$packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath("UserProfile")) ".nuget/packages" }
$config = Get-Content "src/TorrentClassic.App/bin/Release/net10.0-windows/$Runtime/TorrentClassic.runtimeconfig.json" -Raw | ConvertFrom-Json
foreach ($framework in $config.runtimeOptions.includedFrameworks) {
    $package = $framework.name.ToLowerInvariant() + ".runtime." + $Runtime
    $packagePath = Join-Path $packageCache "$package/$($framework.version)"
    foreach ($file in (Get-ChildItem $packagePath -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })) {
        Copy-Item $file.FullName (Join-Path $output "licenses/$($framework.name)-$($file.Name)")
    }
}
$zip = "artifacts/TorrentClassic-$Runtime-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$output/*" -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content "$zip.sha256" -Encoding utf8
Write-Host "Portable package: $zip"
