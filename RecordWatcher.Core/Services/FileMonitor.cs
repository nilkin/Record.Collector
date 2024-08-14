using FileWatcherLibrary.Models;
using Npgsql;
using System.Text.RegularExpressions;

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

            string fileName = Path.GetFileName(filePath);
            string[] parts = fileName.Split('_');

            if (parts.Length < 3)
            {
                Console.WriteLine("File name does not match expected format.");
                return false;
            }

            string callInfo = Regex.Replace(parts[0], @"[\[\]]", "");
            string[] partyParts = parts[1].Split('-');
            string part1 = partyParts.Length > 0 ? partyParts[0] : string.Empty;
            string part2 = partyParts.Length > 1 ? partyParts[1] : string.Empty;

            string dateTimeStr = Regex.Match(parts[2], @"(\d{14})").Value;
            string callId = Regex.Match(parts[2], @"\((\d+)\)").Groups[1].Value;

            CallFileInfo callRecord = new()
            {
                CallInfo = callInfo,
                Part1 = part1,
                Part2 = part2,
                DateTimeStr = dateTimeStr,
                CallID = callId,
                FileName = fileName,
                FilePath = filePath,
                FolderPath = _folderPath
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
                sw.WriteLine($"CallInfo: {obj.CallInfo}");
                sw.WriteLine($"Part1: {obj.Part1}");
                sw.WriteLine($"Part2: {obj.Part2}");
                sw.WriteLine($"DateTimeStr: {obj.DateTimeStr}");
                sw.WriteLine($"CallID: {obj.CallID}");
                sw.WriteLine($"FileName: {obj.FileName}");
                sw.WriteLine($"FilePath: {obj.FilePath}");
                sw.WriteLine($"FolderPath: {obj.FolderPath}");
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
                    checkCmd.CommandText = "SELECT COUNT(*) FROM FILEINFO WHERE FileName = @FileName";
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
                    cmd.CommandText = "INSERT INTO FILEINFO (CallInfo, Part1, Part2, DateTimeStr, FileName, CallID, FilePath, FolderPath) VALUES (@CallInfo, @Part1, @Part2, @DateTimeStr, @FileName, @CallID, @FilePath, @FolderPath)";
                    cmd.Parameters.AddWithValue("@CallInfo", file.CallInfo);
                    cmd.Parameters.AddWithValue("@Part1", file.Part1);
                    cmd.Parameters.AddWithValue("@Part2", file.Part2);
                    cmd.Parameters.AddWithValue("@DateTimeStr", file.DateTimeStr);
                    cmd.Parameters.AddWithValue("@FileName", file.FileName);
                    cmd.Parameters.AddWithValue("@CallID", file.CallID);
                    cmd.Parameters.AddWithValue("@FilePath", file.FilePath);
                    cmd.Parameters.AddWithValue("@FolderPath", file.FolderPath);
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
}
