# diagnose-crash.ps1 - SYSTEMATISK KRASCHANALYS för FilKollen
Write-Host "🔍 FilKollen Kraschdiagnos" -ForegroundColor Red
Write-Host "===========================" -ForegroundColor Red

$exePath = "bin\Debug\net6.0-windows\win-x64\FilKollen.exe"

# 1. Kontrollera att filen finns
Write-Host "📁 Kontrollerar executable..." -ForegroundColor Yellow
if (-not (Test-Path $exePath)) {
    Write-Host "❌ Executable saknas: $exePath" -ForegroundColor Red
    exit 1
}
$fileInfo = Get-Item $exePath
Write-Host "✅ Fil finns: $($fileInfo.Length) bytes, skapad: $($fileInfo.CreationTime)" -ForegroundColor Green

# 2. Kontrollera dependencies
Write-Host "📦 Kontrollerar dependencies..." -ForegroundColor Yellow
$depsPath = "bin\Debug\net6.0-windows\win-x64"
$criticalDlls = @("MaterialDesignThemes.Wpf.dll", "MaterialDesignColors.dll", "Serilog.dll")
foreach ($dll in $criticalDlls) {
    $dllPath = Join-Path $depsPath $dll
    if (Test-Path $dllPath) {
        Write-Host "✅ $dll finns" -ForegroundColor Green
    } else {
        Write-Host "❌ $dll SAKNAS!" -ForegroundColor Red
    }
}

# 3. Kontrollera config-filer
Write-Host "⚙️ Kontrollerar config-filer..." -ForegroundColor Yellow
$configFiles = @("appsettings.json", "branding.json")
foreach ($config in $configFiles) {
    if (Test-Path $config) {
        Write-Host "✅ $config finns" -ForegroundColor Green
    } else {
        Write-Host "⚠️ $config saknas - skapar basic version" -ForegroundColor Yellow
        
        if ($config -eq "appsettings.json") {
            @'
{
  "AppSettings": {
    "AutoDelete": false,
    "QuarantineDays": 30,
    "LogLevel": "Information"
  },
  "ScanPaths": [
    "%TEMP%"
  ]
}
'@ | Out-File -FilePath $config -Encoding UTF8
        } elseif ($config -eq "branding.json") {
            @'
{
  "CompanyName": "FilKollen Security",
  "ProductName": "FilKollen",
  "LogoPath": "default-logo.png",
  "PrimaryColor": "#2196F3",
  "SecondaryColor": "#FF9800"
}
'@ | Out-File -FilePath $config -Encoding UTF8
        }
        Write-Host "✅ Skapade $config" -ForegroundColor Green
    }
}

# 4. Kontrollera resursfiler
Write-Host "🖼️ Kontrollerar resursfiler..." -ForegroundColor Yellow
$resourceDirs = @("Resources", "Resources\Branding", "Resources\Icons", "logs")
foreach ($dir in $resourceDirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "✅ Skapade mapp: $dir" -ForegroundColor Green
    }
}

# 5. Skapa minimal logo-fil
$logoPath = "Resources\Branding\default-logo.png"
if (-not (Test-Path $logoPath)) {
    Write-Host "🖼️ Skapar minimal logo..." -ForegroundColor Yellow
    # Minimal 1x1 PNG
    $pngBytes = @(0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x08,0x02,0x00,0x00,0x00,0x90,0x77,0x53,0xDE,0x00,0x00,0x00,0x0C,0x49,0x44,0x41,0x54,0x08,0xD7,0x63,0xF8,0x0F,0x00,0x00,0x00,0x01,0x00,0x01,0x5C,0xCC,0x40,0x0C,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,0x60,0x82)
    [System.IO.File]::WriteAllBytes($logoPath, $pngBytes)
    Write-Host "✅ Skapade minimal logo" -ForegroundColor Green
}

# 6. Rensa gamla crash-loggar för clean test
Write-Host "🧹 Rensar gamla crash-loggar..." -ForegroundColor Yellow
Get-ChildItem -Path "." -Filter "crash-*.log" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path "." -Filter "*error*.log" | Remove-Item -Force -ErrorAction SilentlyContinue

# 7. KONTROLLERAD START med detaljerad monitoring
Write-Host ""
Write-Host "🚀 STARTAR FILKOLLEN MED DETALJERAD MONITORING..." -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# Kontrollera admin-status
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Host "🔐 Admin-rättigheter: $(if ($isAdmin) { "JA" } else { "NEJ" })" -ForegroundColor $(if ($isAdmin) { "Green" } else { "Yellow" })

Write-Host "⏰ Startar process..." -ForegroundColor Yellow

try {
    # Starta med ProcessStartInfo för bättre kontroll
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $exePath
    $startInfo.WorkingDirectory = (Get-Location).Path
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $false
    
    $process = [System.Diagnostics.Process]::Start($startInfo)
    Write-Host "✅ Process startad med PID: $($process.Id)" -ForegroundColor Green
    
    # Vänta kort tid och kontrollera status
    for ($i = 1; $i -le 10; $i++) {
        Start-Sleep -Milliseconds 500
        
        if ($process.HasExited) {
            Write-Host "❌ PROCESS AVSLUTAD efter $($i * 0.5) sekunder!" -ForegroundColor Red
            Write-Host "💀 Exit Code: $($process.ExitCode)" -ForegroundColor Red
            
            # Läs stdout/stderr
            try {
                $stdout = $process.StandardOutput.ReadToEnd()
                $stderr = $process.StandardError.ReadToEnd()
                
                if ($stdout) {
                    Write-Host "📤 STDOUT:" -ForegroundColor Blue
                    Write-Host $stdout -ForegroundColor Gray
                }
                if ($stderr) {
                    Write-Host "📤 STDERR:" -ForegroundColor Red
                    Write-Host $stderr -ForegroundColor Gray
                }
            } catch {}
            
            break
        } else {
            Write-Host "⏳ $i. Process körs fortfarande..." -ForegroundColor Green
        }
    }
    
    # Om processen fortfarande körs efter 5 sekunder
    if (-not $process.HasExited) {
        Write-Host "✅ FRAMGÅNG! Process körs stabilt efter 5 sekunder" -ForegroundColor Green
        Write-Host "🖥️ Kontrollera om FilKollen-fönstret visas" -ForegroundColor Cyan
        
        Write-Host ""
        Write-Host "❓ Tryck Enter för att stänga applikationen..." -ForegroundColor Yellow
        Read-Host
        
        # Stäng processen
        try {
            $process.CloseMainWindow()
            Start-Sleep -Seconds 2
            if (-not $process.HasExited) {
                $process.Kill()
            }
        } catch {}
    }
    
} catch {
    Write-Host "❌ KRITISKT FEL VID START:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}

# 8. POST-KRASCH ANALYS
Write-Host ""
Write-Host "🔍 POST-KRASCH ANALYS" -ForegroundColor Yellow
Write-Host "=====================" -ForegroundColor Yellow

# Kontrollera crash logs
$crashLogs = Get-ChildItem -Path "." -Filter "crash-*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if ($crashLogs) {
    Write-Host "💥 CRASH LOGS FUNNA:" -ForegroundColor Red
    foreach ($log in $crashLogs | Select-Object -First 3) {
        Write-Host "   📄 $($log.Name) ($(Get-Date $log.LastWriteTime -Format 'HH:mm:ss'))" -ForegroundColor Red
        Write-Host "   --- Innehåll (första 10 rader) ---" -ForegroundColor Gray
        Get-Content $log.FullName -TotalCount 10 | ForEach-Object { Write-Host "   $_" -ForegroundColor Yellow }
        Write-Host ""
    }
}

# Kontrollera vanliga loggar
$logFiles = Get-ChildItem -Path "logs" -Filter "*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if ($logFiles) {
    Write-Host "📋 SENASTE LOG-FILER:" -ForegroundColor Blue
    foreach ($log in $logFiles | Select-Object -First 2) {
        Write-Host "   📄 $($log.Name)" -ForegroundColor Blue
        Write-Host "   --- Senaste 5 rader ---" -ForegroundColor Gray
        Get-Content $log.FullName -Tail 5 | ForEach-Object { Write-Host "   $_" -ForegroundColor Cyan }
        Write-Host ""
    }
}

# Windows Event Log (om admin)
if ($isAdmin) {
    Write-Host "🖥️ WINDOWS EVENT LOG (senaste 5 application errors):" -ForegroundColor Magenta
    try {
        Get-WinEvent -FilterHashtable @{LogName='Application'; Level=2} -MaxEvents 5 -ErrorAction SilentlyContinue | 
            Where-Object { $_.TimeCreated -gt (Get-Date).AddMinutes(-5) } |
            ForEach-Object { 
                Write-Host "   $($_.TimeCreated.ToString('HH:mm:ss')) - $($_.LevelDisplayName): $($_.Message.Split("`n")[0])" -ForegroundColor Red
            }
    } catch {
        Write-Host "   (Kunde inte läsa Event Log)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "🎯 DIAGNOS SLUTFÖRD" -ForegroundColor Green
Write-Host "Kontrollera ovan information för att identifiera kraschorsaken." -ForegroundColor White