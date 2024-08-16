using FileWatcherLibrary.Models;
using NAudio.Wave;
using Npgsql;
using System.Dynamic;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

namespace FileWatcherLibrary
{
    public class FileMonitor

    {
        private readonly FileSystemWatcher _watcher;
        private readonly string _logFilePath;
        private readonly string _folderPath;
        private readonly Queue<string> _fileQueue;
        private readonly Queue<string> _failedQueue;
        private static readonly object _dbLock = new();
        private readonly string _dbConfig;
        private readonly Timer _restartTimer;
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
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024 // 64 KB buffer size
            };

            _watcher.Created += OnCreated;
            _watcher.Error += OnError;
            _restartTimer = new Timer(RestartWatcher, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        }
        public void Start()
        {
            // Enable FileSystemWatcher when starting the monitor
            _watcher.EnableRaisingEvents = true;
            LogMessage($"File Monitor started: {DateTime.Now}");

            // Start periodic status checks
            StartStatusCheckTimer();
        }


        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _restartTimer.Change(Timeout.Infinite, Timeout.Infinite);
                LogMessage($"File Monitor stopped: {DateTime.Now}");
            }
        }
        private void StartStatusCheckTimer()
        {
            // Check the status of FileSystemWatcher every 5 minutes
            Timer statusCheckTimer = new Timer(CheckWatcherStatus, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        private void CheckWatcherStatus(object state)
        {
            // Log the current status of FileSystemWatcher
            LogMessage($"Status check - FileSystemWatcher status: {_watcher.EnableRaisingEvents} - {DateTime.Now}");

            // If the FileSystemWatcher is not enabled, attempt to restart it
            if (!_watcher.EnableRaisingEvents)
            {
                LogMessage("FileSystemWatcher is not enabled. Attempting to restart...");
                RestartWatcher(null);
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
            _watcher.EnableRaisingEvents = true;
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
        private void OnError(object sender, ErrorEventArgs e)
        {
            // Log the error message
            LogMessage($"FileSystemWatcher error message: {e.GetException().Message} - {DateTime.Now}");

            // Log the status of FileSystemWatcher
            LogMessage($"FileSystemWatcher status: {_watcher.EnableRaisingEvents} - {DateTime.Now}");

            // Attempt to restart the watcher
            RestartWatcher(null);
        }


        private void RestartWatcher(object state)
        {
            try
            {
                if (!_watcher.EnableRaisingEvents)
                {
                    _watcher.EnableRaisingEvents = true;
                    LogMessage($"FileSystemWatcher restarted: {DateTime.Now}");
                }
            }
            catch (Exception ex)
            {
                LogException($"Error restarting FileSystemWatcher: {ex.Message}");
            }
        }


        private void LogFailedFile(string filePath)
        {
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string logFileName = $"log_{currentDate}.txt";
            string logFilePath = Path.Combine(_logFilePath, logFileName);

            using StreamWriter writer = new(logFilePath, true);
            writer.WriteLine($"Failed to process file: {filePath} at {DateTime.Now}");
        }

        public bool ParseFileName(string filePath)
        {
            LogMessage($"Parse process started for {filePath}: {DateTime.Now}");

            string fileName = Path.GetFileName(filePath);
            string folderPath = Path.GetDirectoryName(filePath);
            string[] parts = fileName.Split('_');
            if (parts.Length < 3)
            {
                LogMessage($"File name does not match expected format: {DateTime.Now}");
                return false;
            }
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

            using var audioFileReader = new AudioFileReader(filePath);


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
                Date = dateTimeStr,
                Parties = parties,
                Source = source,
                Dest = dest,
                Ext = ext,
                ExternalNumber = externalNumber,
                Seconds = (int)audioFileReader.TotalTime.TotalSeconds
            };

            LogMessage("ParseFileName is working, date: " + DateTime.Now);

            if (AddFileInfo(callRecord))
            {
                LogFileName(callRecord);
                LogMessage($"{fileName} completed: {DateTime.Now}");
                return true;
            }
            return true;
        }

        public void LogFileName(CallFileInfo obj)
        {
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string logFileName = $"log_{currentDate}.txt";
            string logFilePath = Path.Combine(_logFilePath, logFileName);

            using StreamWriter sw = new(logFilePath, true);
            sw.WriteLine($"{DateTime.Now}: {obj.FileName} FileName was created.");
            sw.WriteLine($"FullName: {obj.FullName}");
            sw.WriteLine($"FileName: {obj.FileName}");
            sw.WriteLine($"CallId: {obj.CallId}");
            sw.WriteLine($"Info: {obj.Info}");
            sw.WriteLine($"Path: {obj.Path}");
            sw.WriteLine($"Parties: {obj.Parties}");
            sw.WriteLine($"Ext: {obj.Ext}");
            sw.WriteLine($"Dest: {obj.Dest}");
            sw.WriteLine($"ExternalNumber: {obj.ExternalNumber}");
            sw.WriteLine($"Date: {obj.Date}");
            sw.WriteLine($"Seconds: {obj.Seconds}");
            sw.WriteLine();
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
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM \"CallRecords\" WHERE \"FileName\" = @FileName";

                    cmd.Parameters.AddWithValue("@FileName", file.FileName);
                    int fileCount = Convert.ToInt32(cmd.ExecuteScalar());

                    if (fileCount > 0)
                    {
                        con.Close();
                        using (StreamWriter sw = new(logFilePath, true))
                            sw.WriteLine($"{DateTime.Now}: {file.FileName} already exists. Skipping insertion.");
                        return true;
                    }

                        cmd.CommandText = @"INSERT INTO ""CallRecords"" (
                    ""CallId"", ""Info"", ""Ext"", ""Dest"", ""Date"", 
                    ""Parties"", ""Source"", ""FileName"", ""FullName"",
                    ""Path"", ""ExternalNumber"", ""Seconds""
                    ) VALUES (
                        @CallId, @Info, @Ext, @Dest, @Date, @Parties, @Source, @FileName, @FullName, @Path, @ExternalNumber, @Seconds
                    );";

                    cmd.Parameters.AddWithValue("@CallId", file.CallId);
                    cmd.Parameters.AddWithValue("@Info", file.Info);
                    cmd.Parameters.AddWithValue("@Ext", file.Ext);
                    cmd.Parameters.AddWithValue("@Dest", file.Dest);
                    cmd.Parameters.AddWithValue("@Date", file.Date);
                    cmd.Parameters.AddWithValue("@Parties", file.Parties);
                    cmd.Parameters.AddWithValue("@Source", file.Source);
                    cmd.Parameters.AddWithValue("@FileName", file.FileName);
                    cmd.Parameters.AddWithValue("@FullName", file.FullName);
                    cmd.Parameters.AddWithValue("@Path", file.Path);
                    cmd.Parameters.AddWithValue("@ExternalNumber", file.ExternalNumber);
                    cmd.Parameters.AddWithValue("@Seconds", file.Seconds);
                    cmd.ExecuteNonQuery();
                    con.Close();
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
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string logFileName = $"log_{currentDate}.txt";
            string logFilePath = Path.Combine(_logFilePath, logFileName);

            using StreamWriter sw = new(logFilePath, true);
            sw.WriteLine($"{DateTime.Now}: {message}");
            sw.WriteLine();
        }

        public void LogException(string exception)
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