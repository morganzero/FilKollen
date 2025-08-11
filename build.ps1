# build.ps1 - Build script för FilKollen v2.1
param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [Parameter()]
    [switch]$Clean,
    
    [Parameter()]
    [switch]$Publish,
    
    [Parameter()]
    [switch]$Run,
    
    [Parameter()]
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-ColorOutput {
    param([string]$Message, [string]$Color = "White")
    
    $colors = @{
        "Green" = [ConsoleColor]::Green
        "Yellow" = [ConsoleColor]::Yellow
        "Red" = [ConsoleColor]::Red
        "Cyan" = [ConsoleColor]::Cyan
        "White" = [ConsoleColor]::White
        "Blue" = [ConsoleColor]::Blue
    }
    
    Write-Host $Message -ForegroundColor $colors[$Color]
}

# Header
Write-Host ""
Write-ColorOutput "🔨 FilKollen v2.1 Build Script" "Cyan"
Write-ColorOutput "===============================" "Cyan"
Write-ColorOutput "Configuration: $Configuration" "Yellow"

if ($Verbose) {
    Write-ColorOutput "Verbose mode enabled" "Blue"
    $VerbosePreference = "Continue"
}

# Kontrollera .NET installation
try {
    $dotnetVersion = dotnet --version
    Write-ColorOutput "✅ .NET version: $dotnetVersion" "Green"
} catch {
    Write-ColorOutput "❌ Fel: .NET SDK hittades inte" "Red"
    exit 1
}

# Skapa nödvändiga mappar och filer
Write-ColorOutput "📁 Skapar nödvändiga mappar och filer..." "Yellow"

$directories = @(
    "Resources",
    "Resources\Branding", 
    "Resources\Icons",
    "Themes",
    "logs"
)

foreach ($dir in $directories) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-ColorOutput "   Skapade: $dir" "Blue"
    }
}

# Skapa placeholder icon om den inte finns
$iconPath = "Resources\Icons\filkollen.ico"
if (-not (Test-Path $iconPath)) {
    Write-ColorOutput "🖼️ Skapar placeholder ikon..." "Yellow"
    
    # Skapa en minimal ICO-fil (16x16 transparent)
    $icoBytes = @(
        0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00, 0x68, 0x04,
        0x00, 0x00, 0x16, 0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x20, 0x00,
        0x00, 0x00, 0x01, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x04, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    )
    
    # Lägg till transparent pixeldata (16x16x4 = 1024 bytes)
    $pixelData = @(0x00) * 1024
    $icoBytes += $pixelData
    
    [System.IO.File]::WriteAllBytes((Resolve-Path $iconPath -ErrorAction SilentlyContinue) ?? $iconPath, $icoBytes)
    Write-ColorOutput "   Skapade placeholder ikon" "Green"
}

# Skapa placeholder PNG logo
$logoPath = "Resources\Branding\default-logo.png"
if (-not (Test-Path $logoPath)) {
    Write-ColorOutput "🖼️ Skapar placeholder logo..." "Yellow"
    
    # Minimal 1x1 PNG
    $pngBytes = @(
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
        0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x08,0x02,0x00,0x00,0x00,0x90,0x77,0x53,
        0xDE,0x00,0x00,0x00,0x0C,0x49,0x44,0x41,0x54,0x08,0xD7,0x63,0xF8,0x0F,0x00,0x00,
        0x00,0x01,0x00,0x01,0x5C,0xCC,0x40,0x0C,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,
        0xAE,0x42,0x60,0x82
    )
    
    [System.IO.File]::WriteAllBytes((Resolve-Path $logoPath -ErrorAction SilentlyContinue) ?? $logoPath, $pngBytes)
    Write-ColorOutput "   Skapade placeholder logo" "Green"
}

# Clean om begärt
if ($Clean) {
    Write-ColorOutput "🧹 Rensar tidigare builds..." "Yellow"
    
    $cleanDirs = @("bin", "obj", "publish")
    foreach ($dir in $cleanDirs) {
        if (Test-Path $dir) {
            Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
            Write-ColorOutput "   Rensade: $dir" "Blue"
        }
    }
    
    dotnet clean --verbosity quiet
}

# Restore packages
Write-ColorOutput "📦 Återställer NuGet-paket..." "Yellow"
dotnet restore --verbosity $(if ($Verbose) { "normal" } else { "quiet" })

if ($LASTEXITCODE -ne 0) {
    Write-ColorOutput "❌ NuGet restore misslyckades!" "Red"
    exit 1
}

# Build projekt
Write-ColorOutput "🔨 Kompilerar FilKollen v2.1..." "Yellow"

$buildArgs = @(
    "build"
    "-c", $Configuration
    "--no-restore"
)

if ($Verbose) {
    $buildArgs += "--verbosity", "normal"
} else {
    $buildArgs += "--verbosity", "quiet"
}

& dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-ColorOutput "❌ Build misslyckades med felkod: $LASTEXITCODE" "Red"
    exit 1
}

Write-ColorOutput "✅ Build slutförd framgångsrikt!" "Green"

# Publish om begärt
if ($Publish) {
    Write-ColorOutput "📦 Skapar publikation..." "Yellow"
    
    $publishArgs = @(
        "publish"
        "-c", $Configuration
        "-r", "win-x64"
        "--self-contained", "true"
        "-o", "publish\win-x64"
        "/p:PublishSingleFile=true"
        "/p:PublishReadyToRun=false"
        "/p:IncludeNativeLibrariesForSelfExtract=true"
    )
    
    if ($Verbose) {
        $publishArgs += "--verbosity", "normal"
    } else {
        $publishArgs += "--verbosity", "quiet"
    }
    
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-ColorOutput "✅ Publikation slutförd!" "Green"
        
        $exePath = "publish\win-x64\FilKollen.exe"
        if (Test-Path $exePath) {
            $fileInfo = Get-Item $exePath
            $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
            Write-ColorOutput "📄 Utdata: $exePath ($fileSizeMB MB)" "Cyan"
        }
        
        # Kopiera config-filer
        $configFiles = @("appsettings.json", "branding.json")
        foreach ($configFile in $configFiles) {
            if (Test-Path $configFile) {
                Copy-Item $configFile "publish\win-x64\" -Force
                Write-ColorOutput "📋 Kopierade: $configFile" "Blue"
            }
        }
    } else {
        Write-ColorOutput "❌ Publikation misslyckades med felkod: $LASTEXITCODE" "Red"
        exit 1
    }
}

# Kör applikationen om begärt
if ($Run) {
    if ($Publish) {
        $exePath = "publish\win-x64\FilKollen.exe"
        if (Test-Path $exePath) {
            Write-ColorOutput "🚀 Startar published version..." "Yellow"
            Start-Process -FilePath $exePath
        } else {
            Write-ColorOutput "❌ Kunde inte hitta published executable" "Red"
        }
    } else {
        Write-ColorOutput "🚀 Startar med dotnet run..." "Yellow"
        dotnet run -c $Configuration
    }
}

# Sammanfattning
Write-Host ""
Write-ColorOutput "📊 BUILD SAMMANFATTNING" "Cyan"
Write-ColorOutput "======================" "Cyan"
Write-ColorOutput "✅ Status: Framgångsrik" "Green"
Write-ColorOutput "🔧 Configuration: $Configuration" "White"
Write-ColorOutput "📋 Version: FilKollen v2.1" "White"

if ($Publish) {
    Write-ColorOutput "📦 Output: publish\win-x64\FilKollen.exe" "Cyan"
    Write-ColorOutput "" "White"
    Write-ColorOutput "🚀 För att köra:" "Cyan"
    Write-ColorOutput "   .\publish\win-x64\FilKollen.exe" "White"
} else {
    Write-ColorOutput "📂 Output: bin\$Configuration" "Cyan"
    Write-ColorOutput "" "White"
    Write-ColorOutput "🚀 För att köra:" "Cyan"
    Write-ColorOutput "   dotnet run" "White"
}

Write-Host ""
Write-ColorOutput "🎉 FilKollen v2.1 build slutförd!" "Green"

# Visa användning om inget specifikt gjordes
if (-not $Publish -and -not $Run -and -not $Clean) {
    Write-Host ""
    Write-ColorOutput "💡 ANVÄNDNING:" "Cyan"
    Write-ColorOutput "   .\build.ps1 -Publish    # Bygg och publicera" "White"
    Write-ColorOutput "   .\build.ps1 -Run        # Bygg och kör" "White"
    Write-ColorOutput "   .\build.ps1 -Clean      # Rensa och bygg" "White"
    Write-ColorOutput "   .\build.ps1 -Verbose    # Detaljerad output" "White"
}