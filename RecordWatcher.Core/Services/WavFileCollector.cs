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
    public List<string> GetWavFiles(string folderPath)
    {
        List<string> wavFiles = [];

        if (Directory.Exists(folderPath))
        {
            string[] files = Directory.GetFiles(folderPath, "*.wav", SearchOption.AllDirectories);
            Console.WriteLine($"files count : {files.Length}");
            wavFiles.AddRange(files);
        }
        else
        {
            _monitor.LogException("Path is wrong");
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
