namespace FileWatcherLibrary.Models;

public class CallFileInfo
{
    public CallFileInfo()
    {
        Date = DateTime.Now;
    }
    public int Id { get; set; }
    public string CallId { get; set; }
    public string Info { get; set; }
    public string Parties { get; set; }
    public string Source { get; set; }
    public string Dest { get; set; }
    public string Ext { get; set; }
    public string ExternalNumber { get; set; }
    public DateTime Date { get; set; }
    public int Seconds { get; set; }
    public string FileName { get; set; }
    public string Path { get; set; }
    public string FullName { get; set; }
}

