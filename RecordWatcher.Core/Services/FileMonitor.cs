using FileWatcherLibrary.Models;
using NAudio.Wave;
using Npgsql;
using RecordWatcher.Core.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FileWatcherLibrary;

public class FileMonitor
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _logFilePath;
    private readonly string _folderPath;
    private readonly Queue<string> _fileQueue;
    private readonly Queue<string> _failedQueue;
    private static readonly object _dbLock = new();
    private static readonly object _logLock = new();
    private readonly string _dbConfig;

    public FileMonitor(string folderPath, string logFilePath, string dbConfig)
    {
        if (string.IsNullOrEmpty(folderPath))
            throw new ArgumentException("Folder path must be provided.");

        _folderPath = folderPath;
        _logFilePath = logFilePath;
        _dbConfig = dbConfig;
        _fileQueue = new Queue<string>();
        _failedQueue = new Queue<string>();

        // Initialize and configure the FileSystemWatcher
        _watcher = new FileSystemWatcher(_folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
            Filter = "*.wav",
            IncludeSubdirectories = true
        };
        _watcher.Created += OnCreated;
    }

    public void Start()
    {
        // Enable FileSystemWatcher when starting the monitor
        _watcher.EnableRaisingEvents = true;
        Console.WriteLine("File Monitor started.");
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            Console.WriteLine("File Monitor stopped.");
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (Path.GetExtension(e.FullPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            lock (_fileQueue)
            {
                _fileQueue.Enqueue(e.FullPath);
            }

            Task.Run(() => ProcessQueue());

            // Periodically process the failed queue
            Task.Run(() =>
            {
                if (DateTime.Now.Second % 5 == 0)
                {
                    ProcessFailedQueue();
                }
            });
        }
    }

    private void ProcessQueue()
    {
        lock (_fileQueue)
        {
            if (_fileQueue.Count > 0)
            {
                string filePath = _fileQueue.Peek();
                if (ParseFileName(filePath))
                {
                    _fileQueue.Dequeue();
                }
                else
                {
                    lock (_failedQueue)
                    {
                        _failedQueue.Enqueue(filePath);
                    }
                }
            }
        }
    }

    private void ProcessFailedQueue()
    {
        lock (_failedQueue)
        {
            while (_failedQueue.Count > 0)
            {
                string filePath = _failedQueue.Peek();
                if (ParseFileName(filePath))
                {
                    _failedQueue.Dequeue();
                }
                else
                {
                    LogFailedFile(filePath);
                    _failedQueue.Dequeue();
                }
            }
        }
    }

    private void LogFailedFile(string filePath)
    {
        string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        string logFileName = $"log_{currentDate}.txt";
        string logFilePath = Path.Combine(_logFilePath, logFileName);

        lock (_logLock)
        {
            using StreamWriter writer = new(logFilePath, true);
            writer.WriteLine($"Failed to process file: {filePath} at {DateTime.Now}");
        }
    }

    public bool ParseFileName(string filePath)
    {
        Console.WriteLine($"Parse process started for {filePath}");

        string folderPath = Path.GetDirectoryName(filePath);
        string fileName = Path.GetFileName(filePath);
        string[] parts = fileName.Split('_');

        string parties = parts[1];
        string source = parties.Split("-")[0].Length > 4 ? parties.Split("-")[0][^9..] : parties.Split("-")[0];
        string dest = parties.Split("-")[1];
        string ext = "";
        string externalNumber = "";
        if (dest.Length <= 4)
            ext = dest;
        else if (dest.Length > 4)
            externalNumber = dest;

        if (source.Length <= 4)
            ext = source;
        else if (source.Length > 4)
            externalNumber = source;
        int seconds = 0;

        try
        {
            using var waveFileReader = new WaveFileReader(filePath);
            seconds = (int)waveFileReader.TotalTime.TotalSeconds;
        }
        catch (FormatException ex)
        {
            LogMessage($"Invalid WAV file - No fmt chunk found {ex}: {DateTime.Now}");
        }
        catch (Exception ex)
        {
            LogMessage($"Unexpected error while reading WAV file {ex}: {DateTime.Now}");
        }

        string callInfo = Regex.Replace(parts[0], @"[\[\]]", "");

        string dateTimeStr = Regex.Match(parts[2], @"(\d{14})").Value;
        string callId = Regex.Match(parts[2], @"\((\d+)\)").Groups[1].Value;

        CallFileInfo callRecord = new()
        {
            Info = callInfo,
            CallId = callId,
            FullName = filePath,
            Path = folderPath,
            FileName = fileName,
            Date = DateTime.ParseExact(dateTimeStr, "yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            Parties = parties,
            Source = source,
            Dest = dest,
            Ext = ext,
            ExternalNumber = Utils.Normalize(externalNumber),
            Seconds = seconds
        };

        LogMessage("ParseFileName is working, date: " + DateTime.Now);

        if (AddFileInfo(callRecord))
        {
            LogFileName(callRecord);
            Console.WriteLine(fileName + " completed.");
            return true;
        }
        else
        {
            Console.WriteLine(fileName + " skipped.");
            return false;
        }
    }

    public void LogFileName(CallFileInfo obj)
    {
        string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        string logFileName = $"log_{currentDate}.txt";
        string logFilePath = Path.Combine(_logFilePath, logFileName);

        lock (_logLock)
        {
            using StreamWriter sw = new(logFilePath, true);
            sw.WriteLine($"{DateTime.Now}: {obj.FileName} was created.");
            sw.WriteLine($"CallId: {obj.CallId}");
            sw.WriteLine($"FullName (Path): {obj.FullName}");
            sw.WriteLine($"Path:  {obj.Path}");
            sw.WriteLine($"FileName: {obj.FileName}");
            sw.WriteLine($"Date: {obj.Date}");
            sw.WriteLine($"Parties: {obj.Parties}");
            sw.WriteLine($"Source: {obj.Source}");
            sw.WriteLine($"Dest: {obj.Dest}");
            sw.WriteLine($"Ext: {obj.Ext}");
            sw.WriteLine($"ExternalNumber: {obj.ExternalNumber}");
            sw.WriteLine($"Seconds: {obj.Seconds}");
            sw.WriteLine($"Info: {obj.Info}");
            sw.WriteLine();
        }
    }

    public bool AddFileInfo(CallFileInfo file)
    {
        LogMessage("Entry of AddFileInfo");

        string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        string logFileName = $"log_{currentDate}.txt";
        string logFilePath = Path.Combine(_logFilePath, logFileName);

        using (StreamWriter sw = new(logFilePath, true))
            sw.WriteLine($"{DateTime.Now}: {file.FileName} started.");

        using var con = new NpgsqlConnection(_dbConfig);

        try
        {
            lock (_dbLock)
            {
                con.Open();

                // Check if the file already exists in the database
                using var checkCmd = con.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM \"CallRecords\" WHERE \"FileName\" = @FileName";
                checkCmd.Parameters.AddWithValue("@FileName", file.FileName);
                int fileCount = Convert.ToInt32(checkCmd.ExecuteScalar());

                if (fileCount > 0)
                {
                    using (StreamWriter sw = new(logFilePath, true))
                        sw.WriteLine($"{DateTime.Now}: {file.FileName} already exists. Skipping insertion.");
                    return false;
                }

                // Insert new file information into the database

                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ""CallRecords"" 
                    (""CallId"", ""Info"", ""Parties"", ""Source"", ""Dest"", ""Ext"", ""ExternalNumber"", ""Date"", ""Seconds"", ""FileName"", ""Path"", ""FullName"") 
                    VALUES 
                    (@CallId, @Info, @Parties, @Source, @Dest, @Ext, @ExternalNumber, @Date, @Seconds, @FileName, @Path, @FullName)";

                cmd.Parameters.AddWithValue("@CallId", file.CallId);
                cmd.Parameters.AddWithValue("@Info", file.Info);
                cmd.Parameters.AddWithValue("@Parties", file.Parties);
                cmd.Parameters.AddWithValue("@Source", file.Source);
                cmd.Parameters.AddWithValue("@Dest", file.Dest);
                cmd.Parameters.AddWithValue("@Ext", file.Ext);
                cmd.Parameters.AddWithValue("@ExternalNumber", file.ExternalNumber);
                cmd.Parameters.AddWithValue("@Date", file.Date);
                cmd.Parameters.AddWithValue("@Seconds", file.Seconds);
                cmd.Parameters.AddWithValue("@FileName", file.FileName);
                cmd.Parameters.AddWithValue("@Path", file.Path);
                cmd.Parameters.AddWithValue("@FullName", file.FullName);
                cmd.ExecuteNonQuery();

                using (StreamWriter sw = new(logFilePath, true))
                    sw.WriteLine($"{DateTime.Now}: {file.FileName} insertion completed.");

                return true;
            }
        }
        catch (Exception ex)
        {
            LogException(ex.ToString());
            return false;
        }
    }

    public void LogMessage(string message)
    {
        lock (_logLock)
        {
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string logFileName = $"log_{currentDate}.txt";
            string logFilePath = Path.Combine(_logFilePath, logFileName);

            using StreamWriter sw = new(logFilePath, true);
            sw.WriteLine($"{DateTime.Now}: {message}");
            sw.WriteLine();
        }
    }

    public void LogException(string exception)
    {
        lock (_logLock)
        {
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string logFileName = $"log_{currentDate}.txt";
            string logFilePath = Path.Combine(_logFilePath, logFileName);

            using StreamWriter sw = new(logFilePath, true);
            sw.WriteLine($"{DateTime.Now}: {exception}");
            sw.WriteLine();
        }
    }
}
