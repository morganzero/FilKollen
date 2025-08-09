# build.ps1 - Förbättrat build script för FilKollen
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

# Projektinformation
$ProjectName = "FilKollen"
$ProjectFile = "$ProjectName.csproj"
$OutputDir = "bin\$Configuration"
$PublishDir = "publish"

# Färger för output
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
Write-ColorOutput "🔨 FilKollen Build Script v2.0" "Cyan"
Write-ColorOutput "================================" "Cyan"
Write-ColorOutput "Configuration: $Configuration" "Yellow"
Write-ColorOutput "Project: $ProjectFile" "Yellow"

if ($Verbose) {
    Write-ColorOutput "Verbose mode enabled" "Blue"
    $VerbosePreference = "Continue"
}

# Kontrollera att projektfilen finns
if (-not (Test-Path $ProjectFile)) {
    Write-ColorOutput "❌ Fel: Kunde inte hitta $ProjectFile" "Red"
    Write-ColorOutput "Kontrollera att du kör scriptet från projektmappen." "Yellow"
    exit 1
}

# Kontrollera .NET installation
try {
    $dotnetVersion = dotnet --version
    Write-ColorOutput "✅ .NET version: $dotnetVersion" "Green"
} catch {
    Write-ColorOutput "❌ Fel: .NET SDK hittades inte" "Red"
    Write-ColorOutput "Installera .NET 6.0 SDK från: https://dotnet.microsoft.com/download" "Yellow"
    exit 1
}

# Clean om begärt
if ($Clean) {
    Write-ColorOutput "🧹 Rensar tidigare builds..." "Yellow"
    
    if (Test-Path $OutputDir) { 
        Remove-Item -Recurse -Force $OutputDir -ErrorAction SilentlyContinue
        Write-ColorOutput "   Rensade: $OutputDir" "Blue"
    }
    if (Test-Path $PublishDir) { 
        Remove-Item -Recurse -Force $PublishDir -ErrorAction SilentlyContinue
        Write-ColorOutput "   Rensade: $PublishDir" "Blue"
    }
    
    Write-ColorOutput "🧽 Kör dotnet clean..." "Yellow"
    dotnet clean --verbosity quiet
    
    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput "⚠️ Varning: dotnet clean returnerade felkod $LASTEXITCODE" "Yellow"
    }
}

# Skapa nödvändiga mappar
$directories = @("logs", "Resources\Branding", "Resources\Icons")
foreach ($dir in $directories) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-ColorOutput "📁 Skapade mapp: $dir" "Blue"
    }
}

# Restore dependencies
Write-ColorOutput "📦 Återställer NuGet-paket..." "Yellow"
dotnet restore --verbosity $(if ($Verbose) { "normal" } else { "quiet" })

if ($LASTEXITCODE -ne 0) {
    Write-ColorOutput "❌ NuGet restore misslyckades!" "Red"
    exit 1
}

Write-ColorOutput "✅ NuGet-paket återställda" "Green"

# Build projekt
Write-ColorOutput "🔨 Kompilerar $ProjectName..." "Yellow"

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
    Write-ColorOutput "Kontrollera fel-meddelanden ovan och fixa koden." "Yellow"
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
        "-o", "$PublishDir\win-x64"
        "/p:PublishSingleFile=true"
        "/p:PublishReadyToRun=true"
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
        
        $exePath = "$PublishDir\win-x64\$ProjectName.exe"
        if (Test-Path $exePath) {
            $fileInfo = Get-Item $exePath
            $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
            Write-ColorOutput "📄 Utdata: $exePath ($fileSizeMB MB)" "Cyan"
        }
        
        # Kopiera config-filer
        $configFiles = @("appsettings.json", "branding.json")
        foreach ($configFile in $configFiles) {
            if (Test-Path $configFile) {
                Copy-Item $configFile "$PublishDir\win-x64\" -Force
                Write-ColorOutput "📋 Kopierade: $configFile" "Blue"
            }
        }
        
        # Skapa installer om NSIS finns
        $nsisPath = Get-Command makensis -ErrorAction SilentlyContinue
        if ($nsisPath -and (Test-Path "installer.nsi")) {
            Write-ColorOutput "📦 Skapar installer med NSIS..." "Yellow"
            & makensis installer.nsi
            
            if ($LASTEXITCODE -eq 0) {
                Write-ColorOutput "✅ Installer skapad!" "Green"
            } else {
                Write-ColorOutput "⚠️ Varning: Installer-skapande misslyckades" "Yellow"
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
        $exePath = "$PublishDir\win-x64\$ProjectName.exe"
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

if ($Publish) {
    Write-ColorOutput "📦 Output: $PublishDir\win-x64\$ProjectName.exe" "Cyan"
    Write-ColorOutput "" "White"
    Write-ColorOutput "🚀 För att köra:" "Cyan"
    Write-ColorOutput "   $PublishDir\win-x64\$ProjectName.exe" "White"
} else {
    Write-ColorOutput "📂 Output: $OutputDir" "Cyan"
    Write-ColorOutput "" "White"
    Write-ColorOutput "🚀 För att köra:" "Cyan"
    Write-ColorOutput "   dotnet run" "White"
}

Write-Host ""
Write-ColorOutput "🎉 Build slutförd framgångsrikt!" "Green"

# Visa användning om inget specifikt gjordes
if (-not $Publish -and -not $Run -and -not $Clean) {
    Write-Host ""
    Write-ColorOutput "💡 ANVÄNDNING:" "Cyan"
    Write-ColorOutput "   .\build.ps1 -Publish    # Bygg och publicera" "White"
    Write-ColorOutput "   .\build.ps1 -Run        # Bygg och kör" "White"
    Write-ColorOutput "   .\build.ps1 -Clean      # Rensa och bygg" "White"
    Write-ColorOutput "   .\build.ps1 -Verbose    # Detaljerad output" "White"
}