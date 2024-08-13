using FileWatcherLibrary;
using Microsoft.Extensions.Configuration;
// Setup configuration
var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

string folderPath = config["FileSettings:FolderPath"];
string logFilePath = config["FileSettings:LogFilePath"];
string databaseConfig = config["ConnectionStrings:DefaultConnection"];
Queue<string> fileQueue = new();
Queue<string> failedQueue = new();

// Initialize file monitor
FileMonitor fileMonitor = new(folderPath, logFilePath, fileQueue, failedQueue, databaseConfig);
fileMonitor.Start();

Console.WriteLine("FileWatcherConsoleApp is running. Press [Enter] to exit...");

// Keep the console application running
Console.ReadLine();

// Clean up resources and stop the file monitor
fileMonitor.Stop();
Console.WriteLine("FileWatcherConsoleApp has stopped.");