# build.ps1 - Build script för FilKollen
param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [Parameter()]
    [switch]$Clean,
    
    [Parameter()]
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

# Projektinformation
$ProjectName = "FilKollen"
$ProjectFile = "$ProjectName.csproj"
$OutputDir = "bin\$Configuration"
$PublishDir = "publish"

Write-Host "🔨 Bygger FilKollen..." -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow

# Clean om begärt
if ($Clean) {
    Write-Host "🧹 Rensar tidigare builds..." -ForegroundColor Yellow
    if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    dotnet clean
}

# Restore dependencies
Write-Host "📦 Återställer NuGet-paket..." -ForegroundColor Yellow
dotnet restore

# Build projekt
Write-Host "🔨 Kompilerar..." -ForegroundColor Yellow
dotnet build -c $Configuration --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build misslyckades!"
    exit 1
}

# Publish om begärt
if ($Publish) {
    Write-Host "📦 Skapar publikation..." -ForegroundColor Yellow
    
    # Windows x64
    dotnet publish -c $Configuration -r win-x64 --self-contained true -o "$PublishDir\win-x64" /p:PublishSingleFile=true
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Build och publikation klar!" -ForegroundColor Green
        Write-Host "📂 Utdata: $PublishDir\win-x64\$ProjectName.exe" -ForegroundColor Cyan
        
        # Skapa installer om NSIS finns
        $nsisPath = Get-Command makensis -ErrorAction SilentlyContinue
        if ($nsisPath -and (Test-Path "installer.nsi")) {
            Write-Host "📦 Skapar installer..." -ForegroundColor Yellow
            & makensis installer.nsi
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ Installer skapad!" -ForegroundColor Green
            }
        }
    } else {
        Write-Error "Publikation misslyckades!"
        exit 1
    }
} else {
    Write-Host "✅ Build klar!" -ForegroundColor Green
    Write-Host "📂 Utdata: $OutputDir" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "🚀 För att köra FilKollen:" -ForegroundColor Cyan
if ($Publish) {
    Write-Host "   $PublishDir\win-x64\$ProjectName.exe" -ForegroundColor White
} else {
    Write-Host "   dotnet run" -ForegroundColor White
}
Write-Host ""
