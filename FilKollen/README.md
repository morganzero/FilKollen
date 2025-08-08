# FilKollen - Windows Säkerhetsscanner

FilKollen är ett säkerhetsverktyg för Windows som skannar och rensar suspekta filer från temp-kataloger för att förhindra malware och obehörig åtkomst.

## 🔧 Funktioner

### Kärnfunktioner
- **Automatisk skanning** - Schema för daglig, veckovis eller månadsvis skanning
- **Manuell skanning** - Skanna när du vill
- **Smart hotdetektering** - Identifierar suspekta filtyper och beteenden
- **Säker karantän** - Isolerar hot utan att förstöra dem permanent
- **Säker radering** - Permanent borttagning med dataöverskrivning

### Detektionsmetoder
- Suspekta filextensions (.exe, .bat, .ps1, .vbs, etc.)
- Extensionlösa filer med PE-headers
- Kända hackerverktyg (NirCmd, PSExec, etc.)
- Dubbla filextensions (.txt.exe)
- Filer i temp-kataloger

### Säkerhetsfunktioner
- Kör med administratörsrättigheter för fullständig åtkomst
- Quarantine-system för säker filhantering
- Detaljerad logging av alla operationer
- Whitelist för att undvika falska positiver

## 🚀 Installation och Setup

### Systemkrav
- Windows 10/11 (x64)
- .NET 6.0 Runtime (inkluderas i self-contained build)
- Administratörsrättigheter

### Snabb Start

```bash
# Klona och bygg projektet
git clone [repository-url]
cd FilKollen

# Bygge med PowerShell
.\build.ps1 -Configuration Release -Publish

# Eller med dotnet CLI
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained -o publish/
```

### Första körningen
1. Kör `FilKollen.exe` som administratör
2. Välj mellan manuellt och automatiskt läge
3. Konfigurera schema om automatiskt läge väljs
4. Klicka "Starta skanning" för första test

## 🖥️ Användargränssnitt

### Huvudfönster
- **Mode Toggle** - Växla mellan manuellt/automatiskt
- **Schema-konfiguration** - Ställ in automatisk skanning
- **Skanningsresultat** - Detaljerad lista över funna hot
- **Åtgärdspanel** - Hantera funna filer

### Hotklassificering
- 🟢 **Låg** - Misstänkt men relativt säker
- 🟠 **Medium** - Potentiellt farlig
- 🔴 **Hög** - Troligt hot
- 🟣 **Kritisk** - Känt hackerverktyg

## ⚙️ Konfiguration

### Standardsökvägar
```
%TEMP%                 - Användarspecifik temp
C:\Windows\Temp        - System temp
%LOCALAPPDATA%\Temp    - Lokal appdata temp
```

### Schemaläggning
- **Dagligen** - Kör varje dag vid vald tid
- **Veckovis** - Kör på måndagar vid vald tid  
- **Månadsvis** - Kör den första i månaden vid vald tid

### Automatisk hantering
- **Karantän** - Säkra suspekta filer för granskning
- **Radera** - Permanent borttagning av kritiska hot
- **Notifikationer** - Desktop-meddelanden vid hot

## 🔒 Säkerhet och Sekretess

### Datasäkerhet
- Inga filer skickas externt
- Lokal bearbetning endast
- Säker filradering med dataöverskrivning
- Quarantine med återställningsmöjlighet

### Logging
- Alla operationer loggas lokalt
- Automatisk log-rotation
- Inga känsliga data i loggar
- 30 dagars retention som standard

### Falska Positiver
- Whitelist-system för kända bra filer
- Digital signature verification
- Konfigurerbar känslighet

## 🛠️ Avancerad Användning

### Command Line Interface
```bash
# Schemalagd skanning (används av Task Scheduler)
FilKollen.exe --scheduled

# Debug-läge med verbose logging
FilKollen.exe --debug
```

### Konfigurationsfil
Redigera `appsettings.json` för avancerade inställningar:

```json
{
  "AppSettings": {
    "AutoDelete": false,
    "QuarantineDays": 30,
    "LogLevel": "Information"
  },
  "ScanPaths": [
    "%TEMP%",
    "C:\\Windows\\Temp"
  ],
  "SuspiciousExtensions": [
    ".exe", ".bat", ".ps1"
  ]
}
```

## 📊 Prestanda

### Systembelastning
- Låg CPU-användning under skanning
- Minimal minnesförbrukning (~50MB)
- Ingen påverkan på systemstart
- Effektiv filhantering

### Skanningshastighet
- ~1000 filer/sekund på SSD
- Smart filtrering minskar skanningstid
- Multithreaded för bättre prestanda

## 🐛 Felsökning

### Vanliga Problem

**Problem**: "Åtkomst nekad" fel  
**Lösning**: Kör som administratör

**Problem**: Schema fungerar inte  
**Lösning**: Kontrollera Task Scheduler tjänsten körs

**Problem**: Falska positiver  
**Lösning**: Lägg till sökvägar i whitelist

### Loggar och Debugging
```bash
# Visa loggar
Get-Content logs/filkollen-*.log -Tail 50

# Debug-körning
.\FilKollen.exe --debug --verbose
```

## 🔄 Uppdateringar

### Automatiska Uppdateringar
- Kontrollerar nya versioner vid start
- Säkra digitalt signerade uppdateringar
- Opt-out möjligt via inställningar

### Manuella Uppdateringar
1. Ladda ner senaste release
2. Stoppa FilKollen tjänster
3. Ersätt executable
4. Starta som admin

## 📄 Licens och Support

### Licens
- Proprietär programvara
- Kommersiell användning tillåten
- Ingen vidaredistribution utan tillstånd

### Support och Dokumentation
- GitHub Issues för buggrapporter
- Dokumentation på projektets wiki
- Community support via forum

### Utveckling
- Öppen för bidrag via pull requests
- Code style: Microsoft C# conventions
- Testing framework: xUnit

---

**⚠️ Viktig säkerhetsanmärkning**: FilKollen är ett verktyg för att förbättra säkerheten, men ersätter inte fullständig antivirus-programvara. Använd tillsammans med etablerade säkerhetslösningar för bästa skydd.
