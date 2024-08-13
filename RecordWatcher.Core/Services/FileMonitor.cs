using FileWatcherLibrary.Models;
using Npgsql;
using System.Text.RegularExpressions;

namespace FileWatcherLibrary
{
    public class FileMonitor
    {
        private FileSystemWatcher _watcher;
        private readonly string _logFilePath;
        private readonly string _folderPath;
        private readonly Queue<string> _fileQueue;
        private readonly Queue<string> _failedQueue;
        private static readonly object _dbLock = new();
        public string _dbConfig;

        public FileMonitor(string folderPath, string logFilePath, string dbConfig)
        {
            _logFilePath = logFilePath;
            _folderPath = folderPath;
            _dbConfig = dbConfig;
        }

        public FileMonitor(string folderPath, string logFilePath, Queue<string> fileQueue, Queue<string> failedQueue, string dbConfig)
        {
            _logFilePath = logFilePath;
            _fileQueue = fileQueue;
            _folderPath = folderPath;
            _failedQueue = failedQueue;
            _dbConfig = dbConfig;
        }

        public void Start()
        {
            _watcher = new FileSystemWatcher
            {
                Path = _folderPath,
                Filter = "*.wav",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += async (sender, e) => await OnCreated(sender, e);
        }

        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
        }

        private async Task OnCreated(object sender, FileSystemEventArgs e)
        {
            if (Path.GetExtension(e.FullPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                _ = Task.Run(() =>
                {
                    if (DateTime.Now.Second % 5 == 0)
                    {
                        ProcessQueueForFailed();
                    }
                });
                try
                {
                    _ = Task.Run(() =>
                    {
                        _fileQueue.Enqueue(e.FullPath);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                _ = Task.Run(() =>
                {
                    ProcessQueue();
                });
            }
        }
        private void ProcessQueue()
        {
            string filePath = _fileQueue.Peek();
            var res = ParseFileName(filePath);
            if (res == true)
            {
                _fileQueue.Dequeue();
            }
            else
            {
                _failedQueue.Enqueue(filePath);
            }
        }
        private void ProcessQueueForFailed()
        {
            while (_failedQueue.Count > 0)
            {
                string filePath = _failedQueue.Peek();

                var res = ParseFileName(filePath);

                if (res == true)
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
            Console.WriteLine($"Parse process start for {filePath}");
            // string fileName = "[Dialer%3AMakeCall]_0707702777-105_20240805131501(135).wav";
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
            string callIdMatch = Regex.Match(parts[2], @"\((\d+)\)").Groups[1].Value;
            string callId = !string.IsNullOrEmpty(callIdMatch) ? callIdMatch : string.Empty;

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

            LogException("ParseFileName is working, date:" + DateTime.Now);

            var resp = AddFileInfo(callRecord);
            if (resp == true)
            {
                LogFileName(callRecord);
                Console.WriteLine(fileName + " Completed");
                return true;
            }
            else
            {
                Console.WriteLine(fileName + " Skipped");
                return true;
            }
        }

        public void LogFileName(CallFileInfo obj)
        {
            try
            {
                var callInfo = obj.CallInfo;
                var part1 = obj.Part1;
                var part2 = obj.Part2;
                var dateTimeStr = obj.DateTimeStr;
                var callId = obj.CallID;
                var fileName = obj.FileName;
                var filePath = obj.FilePath;
                var folderPath = obj.FolderPath;
                string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
                string logFileName = $"log_{currentDate}.txt";
                string logFilePath = Path.Combine(_logFilePath, logFileName);

                using StreamWriter sw = new(logFilePath, true);
                sw.WriteLine($"{DateTime.Now}: {fileName} was created.");
                sw.WriteLine($"CallInfo: {callInfo}");
                sw.WriteLine($"Part1: {part1}");
                sw.WriteLine($"Part2: {part2}");
                sw.WriteLine($"DateTimeStr: {dateTimeStr}");
                sw.WriteLine($"CallID: {callId}");
                sw.WriteLine($"FileName: {fileName}");
                sw.WriteLine($"FilePath: {filePath}");
                sw.WriteLine($"FolderPath: {folderPath}");
                sw.WriteLine();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while logging the results: {ex.Message}");
            }

        }
        //public bool AddFileInfo(CallFileInfo file)
        //{
        //    LogException("entry of AddFileInfo");
        //    string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        //    string logFileName = $"log_{currentDate}.txt";
        //    string logFilePath = Path.Combine(_logFilePath, logFileName);
        //    using (StreamWriter sw = new(logFilePath, true))
        //    {
        //        sw.WriteLine($"{DateTime.Now}: {file.FileName} started.");
        //    }
        //    var con = new NpgsqlConnection(_dbConfig);
        //    try
        //    {
        //        lock (_dbLock)
        //        {

        //            con.Open();
        //            var cmd = con.CreateCommand();
        //            cmd.CommandText = "INSERT INTO FILEINFO (CallInfo, Part1,Part2,DateTimeStr,FileName,CallID,FilePath,FolderPath) VALUES (@CallInfo, @Part1,@Part2,@DateTimeStr,@FileName,@CallID,@FilePath,@FolderPath)";
        //            cmd.Parameters.AddWithValue("@CallInfo", file.CallInfo);
        //            cmd.Parameters.AddWithValue("@Part1", file.Part1);
        //            cmd.Parameters.AddWithValue("@Part2", file.Part2);
        //            cmd.Parameters.AddWithValue("@DateTimeStr", file.DateTimeStr);
        //            cmd.Parameters.AddWithValue("@FileName", file.FileName);
        //            cmd.Parameters.AddWithValue("@CallID", file.CallID);
        //            cmd.Parameters.AddWithValue("@FilePath", file.FilePath);
        //            cmd.Parameters.AddWithValue("@FolderPath", file.FolderPath);
        //            var result = cmd.ExecuteNonQuery();
        //            con.Close();
        //            using StreamWriter sw = new(logFilePath, true);
        //            sw.WriteLine($"{DateTime.Now}: {file.FileName} ended.");
        //            return true;

        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        LogException(ex.ToString());
        //        return false;
        //    }



        //}
        public bool AddFileInfo(CallFileInfo file)
        {
            LogException("entry of AddFileInfo");
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string logFileName = $"log_{currentDate}.txt";
            string logFilePath = Path.Combine(_logFilePath, logFileName);

            using (StreamWriter sw = new(logFilePath, true))
                sw.WriteLine($"{DateTime.Now}: {file.FileName} started.");

            var con = new NpgsqlConnection(_dbConfig);
            try
            {
                lock (_dbLock)
                {
                    con.Open();

                    // Check if the file already exists in the database
                    var checkCmd = con.CreateCommand();
                    checkCmd.CommandText = "SELECT COUNT(*) FROM FILEINFO WHERE FileName = @FileName";
                    checkCmd.Parameters.AddWithValue("@FileName", file.FileName);
                    int fileCount = Convert.ToInt32(checkCmd.ExecuteScalar());

                    // If the file exists, log the event and continue
                    if (fileCount > 0)
                    {
                        using (StreamWriter sw = new (logFilePath, true))
                            sw.WriteLine($"{DateTime.Now}: {file.FileName} already exists. Skipping insertion.");
                        con.Close();
                        return false;
                    }

                    // Insert new file information into the database
                    var cmd = con.CreateCommand();
                    cmd.CommandText = "INSERT INTO FILEINFO (CallInfo, Part1, Part2, DateTimeStr, FileName, CallID, FilePath, FolderPath) VALUES (@CallInfo, @Part1, @Part2, @DateTimeStr, @FileName, @CallID, @FilePath, @FolderPath)";
                    cmd.Parameters.AddWithValue("@CallInfo", file.CallInfo);
                    cmd.Parameters.AddWithValue("@Part1", file.Part1);
                    cmd.Parameters.AddWithValue("@Part2", file.Part2);
                    cmd.Parameters.AddWithValue("@DateTimeStr", file.DateTimeStr);
                    cmd.Parameters.AddWithValue("@FileName", file.FileName);
                    cmd.Parameters.AddWithValue("@CallID", file.CallID);
                    cmd.Parameters.AddWithValue("@FilePath", file.FilePath);
                    cmd.Parameters.AddWithValue("@FolderPath", file.FolderPath);
                    var result = cmd.ExecuteNonQuery();

                    con.Close();

                    using (StreamWriter sw = new (logFilePath, true))
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

        public void LogException(string obj)
        {

            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string logFileName = $"log_{currentDate}.txt";
            string logFilePath = Path.Combine(_logFilePath, logFileName);

            using StreamWriter sw = new(logFilePath, true);
            sw.WriteLine($"{obj}: exception");

            sw.WriteLine();

            sw.Flush();
            sw.Close();

        }

    }
}


