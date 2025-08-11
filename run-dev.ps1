# run-dev.ps1 - Snabbstart för utveckling utan admin-krav
param(
    [Parameter()]
    [switch]$Clean,
    
    [Parameter()]
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

function Write-ColorOutput {
    param([string]$Message, [string]$Color = "White")
    
    $colors = @{
        "Green" = [ConsoleColor]::Green
        "Yellow" = [ConsoleColor]::Yellow
        "Red" = [ConsoleColor]::Red
        "Cyan" = [ConsoleColor]::Cyan
        "Blue" = [ConsoleColor]::Blue
    }
    
    Write-Host $Message -ForegroundColor $colors[$Color]
}

Write-ColorOutput "🚀 FilKollen v2.1 Development Runner" "Cyan"
Write-ColorOutput "====================================" "Cyan"

# Säkerställ att utvecklingsmanifest finns
if (-not (Test-Path "app-dev.manifest")) {
    Write-ColorOutput "📄 Skapar utvecklingsmanifest..." "Yellow"
    
    $devManifest = @'
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="FilKollen"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
'@
    
    $devManifest | Out-File -FilePath "app-dev.manifest" -Encoding UTF8
    Write-ColorOutput "   ✅ Utvecklingsmanifest skapat" "Green"
}

# Clean om begärt
if ($Clean) {
    Write-ColorOutput "🧹 Rensar debug-filer..." "Yellow"
    if (Test-Path "bin\Debug") { Remove-Item -Recurse -Force "bin\Debug" -ErrorAction SilentlyContinue }
    if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" -ErrorAction SilentlyContinue }
    dotnet clean --verbosity quiet
}

# Skapa nödvändiga mappar
$directories = @("Resources\Icons", "Resources\Branding", "Themes", "logs")
foreach ($dir in $directories) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# Build och kör
Write-ColorOutput "🔨 Bygger debug-version..." "Yellow"

$buildArgs = @("build", "-c", "Debug")
if ($Verbose) { $buildArgs += "--verbosity", "normal" }

& dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-ColorOutput "❌ Build misslyckades!" "Red"
    exit 1
}

Write-ColorOutput "✅ Build lyckades!" "Green"
Write-ColorOutput "🚀 Startar FilKollen..." "Cyan"

# Kör utan dotnet (direkt executable)
$exePath = "bin\Debug\net6.0-windows\win-x64\FilKollen.exe"

if (Test-Path $exePath) {
    Start-Process -FilePath $exePath -WorkingDirectory (Get-Location)
    Write-ColorOutput "✅ FilKollen startad från: $exePath" "Green"
    Write-ColorOutput "💡 Utvecklingsläge: Inga admin-rättigheter krävs" "Blue"
} else {
    Write-ColorOutput "❌ Kunde inte hitta executable: $exePath" "Red"
    Write-ColorOutput "💡 Försök: dotnet run --project FilKollen.csproj" "Yellow"
}

Write-ColorOutput "" "White"
Write-ColorOutput "🎯 För production-build med admin-rättigheter:" "Cyan"
Write-ColorOutput "   .\build.ps1 -Publish" "White"