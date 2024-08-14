using FileWatcherLibrary;
using Microsoft.AspNetCore.Mvc;

namespace RecordWatcher.Api.Controllers;
[ApiController]
[Route("api/[controller]")]
public class FileWatcherController : ControllerBase
{
    private readonly FileMonitor _fileMonitor;
    private readonly WavFileCollector _wavFileCollector;
    private readonly string _folderPath;
    public FileWatcherController(IConfiguration configuration)
    {
        _folderPath= configuration["FileSettings:FolderPath"];
        string logFilePath = configuration["FileSettings:LogFilePath"];
        string databaseConfig = configuration["ConnectionStrings:DefaultConnection"];

        _fileMonitor = new FileMonitor(_folderPath, logFilePath,databaseConfig);
        _wavFileCollector = new WavFileCollector(_fileMonitor);
    }

    [HttpPost("start")]
    public IActionResult Start()
    {
        _fileMonitor.Start();
        return Ok("File monitor started.");
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        _fileMonitor.Stop();
        return Ok("File monitor stopped.");
    }
    [HttpPost("collect-wav-files")]
    public IActionResult CollectAndSaveWavFiles()
    {
        var wavFiles = _wavFileCollector.GetWavFiles(_folderPath);

        if (wavFiles.Count == 0)
        {
            return NotFound("No WAV files found.");
        }

        _wavFileCollector.SaveWavFilesToDatabase(wavFiles);
        return Ok($"{wavFiles.Count} WAV files have been saved to the database.");
    }
}
