using FileWatcherLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RecordWatcher.GatherAll;

try
{
    var config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddCommandLine(args)
        .Build();
        CreateHostBuilder(args, config).Build().Run();
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
    throw;
}

IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton(provider =>
            {
                string folderPath = configuration["FileSettings:FolderPath"];
                string logFilePath = configuration["FileSettings:LogFilePath"];
                string databaseConfig = configuration["ConnectionStrings:DefaultConnection"];

                return new FileMonitor(folderPath, logFilePath, databaseConfig);
            });

            services.AddSingleton<WavFileCollector>();
            services.AddHostedService<WavFileCollectorService>();
        })
        .ConfigureLogging(logging =>
        {
            logging.AddConsole();
            logging.AddEventLog();
        });

