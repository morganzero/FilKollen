# FilKollen - Förenklad Säkerhetsscanner v2.0

FilKollen är ett minimalistiskt Windows säkerhetsverktyg som fokuserar på **realtidsskydd mot bluffnotiser**, **NirCmd/screenshot/stealer-skript** samt **oönskade fjärrstyrningsverktyg**.

## 🎯 Vad FilKollen Gör

### Primära Funktioner

1. **Realtidsskydd i bakgrunden**
   - Kontinuerlig övervakning av temp-kataloger
   - Detektering av NirCmd, screenshot-verktyg och Telegram bot-skript
   - Automatisk identifiering av fjärrstyrningsverktyg (AnyDesk, TeamViewer, etc.)
   - Enkel crypto mining-heuristik

2. **Automatisk + Manuell "Prune" av bluffnotiser**
   - Rensning av webbläsarers notifikations-tillstånd
   - Blockering av kända malware-domäner
   - Chrome/Edge: Preferences-filer och registry-policies
   - Firefox: permissions.sqlite-databas

3. **Minimalistiskt UI**
   - Endast Av/På-växlare för auto-läge
   - "Prune bluffnotiser"-knapp
   - Enkel statistik och synlig loggvy
   - Logotyp och tray-ikon

4. **Bakgrundsdrift**
   - Minimerar alltid till tray (aldrig fullständig stängning)
   - Avslutas endast via tray-meny
   - Kontinuerlig övervakning även när fönstret är dolt

## 🌐 Hur "Prune Bluffnotiser" Fungerar Per Browser

### Chrome & Edge
```
1. Stänger alla Chrome/Edge-processer
2. Läser profile.content_settings.exceptions.notifications från Preferences
3. Tar bort alla notifikationsinställningar för malware-domäner:
   - Push notification scams (clickadu.com, propellerads.com, etc.)
   - Fake virus alerts (microsoft-security-alert.com, etc.)  
   - Crypto scams (bitcoin-generator.com, etc.)
   - Telegram bot API (api.telegram.org, t.me, etc.)
4. Sätter DefaultNotificationsSetting=2 (block all)
5. Tillämpar säkerhetspolicies via Windows Registry
6. Sparar rensade Preferences-filer
```

### Firefox
```
1. Stänger alla Firefox-processer
2. Öppnar permissions.sqlite-databas för varje profil
3. Kör SQL: DELETE FROM moz_perms WHERE type = 'desktop-notification' 
   AND origin LIKE '%malware-domain%'
4. Tar bort desktop-notification permissions för alla kända malware-domäner
5. Sparar uppdaterad permissions.sqlite
```

### Systemövergripande
```
1. Uppdaterar Windows hosts-fil:
   - Blockerar nirsoft.net, pastebin.com, api.telegram.org
   - Lägger till 200+ kända malware notification-domäner
2. Flushar DNS-cache (ipconfig /flushdns)
3. Sätter PowerShell execution policy till Restricted
4. Tillämpar strikta browser-säkerhetspolicies
```

## ⚙️ Autolägets Beteende

### När Realtidsskydd Aktiveras:
- **Kontinuerlig filsystemövervakning** av temp-kataloger
- **Process-scanning var 10:e sekund** för kritiska hot
- **Periodisk säkerhetskontroll var 5:e minut**
- **Automatisk karantän** för medium/high-hot
- **Automatisk radering** för kritiska hot
- **Automatisk prune av bluffnotiser var 6:e timme**

### Hothantering i Auto-läge:
```
Kritiska hot (NirCmd, Screenshot-verktyg):    → Automatisk radering
Höga hot (Remote Access verktyg):           → Automatisk karantän  
Medium hot (Crypto miners):                 → Automatisk karantän
Låga hot:                                   → Endast loggning
```

### Intrång-detektering:
- **NirCmd och screenshot-verktyg** (savescreenshot, nircmd.exe)
- **Telegram bot-indikatorer** (api.telegram.org/bot, sendDocument)
- **Remote Access verktyg** (anydesk, teamviewer, vnc, rdp)
- **Crypto mining-heuristik** (xmrig, miner, mining, cpuminer)

## 🔧 Hur Man Bygger

### Förutsättningar
```bash
# Installera .NET 6.0 SDK
https://dotnet.microsoft.com/download/dotnet/6.0

# Kontrollera installation
dotnet --version
```

### Snabb Build
```bash
# Klona projektet
git clone [repository-url]
cd FilKollen

# Bygg debug-version
dotnet build -c Debug

# Bygg release-version  
dotnet build -c Release

# Använd PowerShell build-script (rekommenderat)
.\build.ps1 -Configuration Release -Publish
```

### Advanced Build med PowerShell
```powershell
# Full build med publikation
.\build.ps1 -Configuration Release -Publish -Clean

# Bygg och kör direkt
.\build.ps1 -Configuration Debug -Run

# Verbose build för debugging
.\build.ps1 -Configuration Release -Publish -Verbose

# Endast rensning
.\build.ps1 -Clean
```

### Manuell Publikation
```bash
# Self-contained executable
dotnet publish -c Release -r win-x64 --self-contained true -o publish/

# Kopiera config-filer
copy appsettings.json publish/
copy branding.json publish/
```

## 📁 Projektstruktur

```
FilKollen/
├── Services/
│   ├── AdvancedBrowserCleaner.cs     # Browser notification cleaning
│   ├── IntrusionDetectionService.cs  # NirCmd/RA-verktyg detection
│   ├── RealTimeProtectionService.cs  # Kontinuerlig övervakning
│   ├── TempFileScanner.cs           # Temp-katalog scanning
│   ├── QuarantineManager.cs         # Säker filhantering
│   └── SystemTrayService.cs         # Tray-ikon och bakgrundsdrift
├── Models/                          # Data models
├── Resources/                       # Logo och ikoner
├── MainWindow.xaml                  # Minimalistiskt UI
├── App.xaml                        # Transparent tema
├── appsettings.json                # Konfiguration
├── branding.json                   # White-label inställningar
└── build.ps1                      # Build-script
```

## 🚀 Användning

### Första Start
1. Kör `FilKollen.exe` som administratör (rekommenderat)
2. Aktivera realtidsskydd med Av/På-växlaren
3. Klicka "Prune bluffnotiser" för omedelbar rensning
4. Applikationen minimeras till systemfältet

### Daglig Användning
- **Bakgrundsdrift**: FilKollen körs automatiskt i systemfältet
- **Auto-rensning**: Realtidsskyddet hanterar hot automatiskt
- **Manuell rensning**: Klicka "Prune bluffnotiser" vid behov
- **Statistik**: Se antal hot funna/hanterade i huvudfönstret

### Tray-meny Funktioner
- **Visa FilKollen**: Öppna huvudfönstret
- **Real-time Skydd**: Växla skydd av/på
- **Automatisk Rensning**: Aktivera/inaktivera auto-läge
- **Avsluta**: Stäng FilKollen helt

## 🛡️ Säkerhetsfunktioner

### Realtidsskydd
- Kontinuerlig temp-katalog övervakning
- Process-analys för kritiska hot
- Automatisk karantän/radering
- DNS-cache rensning
- Hosts-fil uppdatering

### Browser Säkerhet
- Notification permission rensning
- Malware domain blocking
- Security policy enforcement
- Extension analysis
- Cookie/cache rensning

### Intrång-prevention
- NirCmd/screenshot detection
- Telegram bot monitoring
- Remote access tool alerts
- Crypto mining detection
- Pastebin/paste site blocking

## 📊 Prestanda

- **CPU-användning**: <1% idle, <5% under scanning
- **RAM-förbrukning**: <150MB
- **Disk-påverkan**: Minimal (endast temp-filer)
- **Nätverks-trafik**: Ingen (förutom DNS cache flush)

## 🔧 Konfiguration

### appsettings.json
```json
{
  "AppSettings": {
    "AutoDelete": false,
    "QuarantineDays": 30,
    "LogLevel": "Information"
  },
  "ScanPaths": [
    "%TEMP%",
    "C:\\Windows\\Temp",
    "%LOCALAPPDATA%\\Temp"
  ]
}
```

### Anpassning
- **Scan-sökvägar**: Modifiera `ScanPaths` i appsettings.json
- **Auto-radering**: Sätt `AutoDelete: true` för automatisk radering
- **Karantän-tid**: Ändra `QuarantineDays` för längre/kortare karantän
- **Logg-nivå**: Sätt till `Debug` för detaljerad loggning

## 🏷️ White-Label Möjligheter

FilKollen stöder white-label anpassning via `branding.json`:

```json
{
  "CompanyName": "Ditt Företag AB",
  "ProductName": "Säkerhetsscanner Pro", 
  "LogoPath": "Resources/Branding/custom-logo.png",
  "PrimaryColor": "#2196F3",
  "SecondaryColor": "#FF9800",
  "ContactEmail": "support@dittforetag.se",
  "Website": "https://dittforetag.se"
}
```

## 🐛 Felsökning

### Vanliga Problem
```
Problem: "Åtkomst nekad" vid start
Lösning: Kör som administratör

Problem: Browser notifications återkommer
Lösning: Stäng alla browser-fönster innan "Prune bluffnotiser"

Problem: FilKollen startar inte
Lösning: Kontrollera att .NET 6.0 Runtime är installerat
```

### Debug-körning
```bash
# Kör med debug-manifest (ingen admin-rättighet)
# Ändra i FilKollen.csproj:
<ApplicationManifest>app-dev.manifest</ApplicationManifest>

# Aktivera debug-loggning
# I appsettings.json: "LogLevel": "Debug"

# Använd diagnos-script
.\diagnose-crash.ps1
```

### Logg-filer
- **Applikationsloggar**: `logs/filkollen-*.log`
- **Crash-loggar**: `crash-*.log`
- **System-händelser**: Windows Event Viewer → Application

## 📜 Licens

Proprietär programvara för kommersiell användning. Kontakta för licensalternativ.

## 🤝 Support

- **Bug-rapporter**: GitHub Issues
- **Feature-förfrågningar**: GitHub Discussions
