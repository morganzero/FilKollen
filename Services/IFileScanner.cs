using System.Collections.Generic;
using System.Threading.Tasks;
using FilKollen.Models;

namespace FilKollen.Services
{
    public interface IFileScanner
    {
        Task<List<ScanResult>> ScanAsync();
        Task<List<ScanResult>> ScanTempDirectoriesAsync();
        Task<ScanResult?> ScanSingleFileAsync(string filePath);
        void AddToWhitelist(string path);
        void RemoveFromWhitelist(string path);
    }
}

// Uppdatera TempFileScanner.cs för att implementera interfacet
namespace FilKollen.Services
{
    public class TempFileScanner : IFileScanner
    {
        // Existing implementation...
        // Alla befintliga metoder behålls som de är
    }

    // Lägg även till detta alias för bakåtkompatibilitet
    public class FileScanner : TempFileScanner
    {
        public FileScanner(AppConfig config, ILogger logger) : base(config, logger)
        {
        }
    }
}