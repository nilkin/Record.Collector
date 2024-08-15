using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileWatcherLibrary;

public class WavFileCollector
{
    private readonly FileMonitor _monitor;
    public WavFileCollector(FileMonitor monitor)
    {
        _monitor = monitor;
    }
    public List<string> GetWavFiles(string folderPath, DateTime? startDate = null, DateTime? endDate = null)
    {
        List<string> wavFiles = new();
        DateTime start = startDate ?? DateTime.MinValue;
        DateTime end = endDate ?? DateTime.MaxValue;

        if (Directory.Exists(folderPath))
        {
            string[] files = Directory.GetFiles(folderPath, "*.wav", SearchOption.AllDirectories);
            Console.WriteLine($"Total files found: {files.Length}");

            foreach (string file in files)
            {
                DateTime lastWriteTime = File.GetLastWriteTime(file);
                if (lastWriteTime >= start && lastWriteTime <= end)
                {
                    wavFiles.Add(file);
                }
            }
        }
        else
        {
            _monitor.LogException("Invalid folder path.");
        }

        return wavFiles;
    }




    public void SaveWavFilesToDatabase(List<string> wavFiles)
    {
        foreach (string wavFile in wavFiles)
        {
            _monitor.ParseFileName(wavFile);
        }
    }
}
