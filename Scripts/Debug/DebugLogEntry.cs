namespace Fableland.Debug;

/// <summary>One entry in the debug log.</summary>
public struct DebugLogEntry
{
    public string Time;
    public string Category;
    public string Message;

    public DebugLogEntry(string time, string category, string message)
    {
        Time = time;
        Category = category;
        Message = message;
    }

    public override string ToString() => $"[{Time}] [{Category}] {Message}";
}
