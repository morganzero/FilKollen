# test-run.ps1 - Debug Test Runner
Write-Host "🔍 FilKollen Debug Test Runner" -ForegroundColor Cyan
Write-Host "==============================" -ForegroundColor Cyan

# Bygg först
Write-Host "🔨 Building project..." -ForegroundColor Yellow
dotnet build -c Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Skapa logs-mapp
if (-not (Test-Path "logs")) {
    New-Item -ItemType Directory -Path "logs" -Force | Out-Null
    Write-Host "📁 Created logs directory" -ForegroundColor Blue
}

# Starta appen och vänta lite
Write-Host "🚀 Starting FilKollen..." -ForegroundColor Yellow
$exePath = ".\bin\Debug\net6.0-windows\win-x64\FilKollen.exe"

Write-Host "📍 Executable path: $exePath" -ForegroundColor Gray
Write-Host "📍 Working directory: $(Get-Location)" -ForegroundColor Gray

# Starta processen och vänta
$process = Start-Process -FilePath $exePath -PassThru -WindowStyle Normal

Write-Host "🔢 Process started with PID: $($process.Id)" -ForegroundColor Green
Write-Host "⏰ Waiting 5 seconds to see if app starts properly..." -ForegroundColor Yellow

Start-Sleep -Seconds 5

# Kontrollera om processen fortfarande körs
if ($process.HasExited) {
    Write-Host "❌ Process exited with code: $($process.ExitCode)" -ForegroundColor Red
    
    # Kolla om det finns crash logs
    $crashLogs = Get-ChildItem -Path "." -Filter "crash-*.log" | Sort-Object LastWriteTime -Descending
    if ($crashLogs) {
        Write-Host "💥 Found crash log: $($crashLogs[0].Name)" -ForegroundColor Red
        Write-Host "📄 Crash log content:" -ForegroundColor Red
        Get-Content $crashLogs[0].FullName | Write-Host -ForegroundColor Yellow
    }
    
    # Kolla vanliga log-filer
    $logFiles = Get-ChildItem -Path "logs" -Filter "*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    if ($logFiles) {
        Write-Host "📋 Latest log file: $($logFiles[0].Name)" -ForegroundColor Blue
        Write-Host "📄 Last 20 lines from log:" -ForegroundColor Blue
        Get-Content $logFiles[0].FullName -Tail 20 | Write-Host -ForegroundColor Gray
    }
} else {
    Write-Host "✅ Process is running! UI should be visible now." -ForegroundColor Green
    Write-Host "🖱️  Check if the FilKollen window is open." -ForegroundColor Cyan
    Write-Host "🔍 If no window appears, check Windows notification area (system tray)" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "🛠️  Debug Tips:" -ForegroundColor Cyan
Write-Host "   - Check Event Viewer for application errors" -ForegroundColor White
Write-Host "   - Look for crash-*.log files in project directory" -ForegroundColor White
Write-Host "   - Check logs/ directory for application logs" -ForegroundColor White
Write-Host "   - Try running without admin rights using app-dev.manifest" -ForegroundColor White