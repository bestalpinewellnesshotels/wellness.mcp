# Publish-Script für Linux-Deployment
# Erstellt Self-Contained Build mit .NET Runtime

Write-Host "Building HotelChatbot.Api for Linux x64 (self-contained)..." -ForegroundColor Green

$sourcePath = "src/HotelChatbot.Api"
$outputPath = "publish/linux-x64"

# Clean previous builds
if (Test-Path $outputPath) {
    Write-Host "Cleaning previous build..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $outputPath
}

# Publish for Linux x64 with runtime included
Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish $sourcePath `
    --configuration Release `
    --runtime linux-x64 `
    --self-contained true `
    --output $outputPath `
    /p:PublishSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild erfolgreich!" -ForegroundColor Green
    Write-Host "Output: $outputPath" -ForegroundColor Cyan
} else {
    Write-Host "`nBuild fehlgeschlagen!" -ForegroundColor Red
    exit 1
}
