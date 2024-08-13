using FileWatcherLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace RecordWatcher.GatherAll;

public class WavFileCollectorService(WavFileCollector wavFileCollector, IConfiguration configuration) : BackgroundService
{
    private readonly WavFileCollector _wavFileCollector = wavFileCollector;
    private readonly IConfiguration _configuration = configuration;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Implement the background task logic here
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string folderPath = _configuration["FileSettings:FolderPath"];
                List<string> files = _wavFileCollector.GetWavFiles(folderPath);
                _wavFileCollector.SaveWavFilesToDatabase(files);

                // Wait for a certain period before the next execution
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // Adjust as necessary
            }
            catch (OperationCanceledException ocx)
            {
                Console.WriteLine($"Wav File Collector Service: {ocx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}
