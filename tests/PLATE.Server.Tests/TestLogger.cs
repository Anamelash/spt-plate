using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Logging;
using SPTarkov.Server.Core.Models.Utils;

namespace PLATE.Server.Tests;

/// <summary>
/// Server services take an <see cref="ISptLogger{T}"/>; the tests do not care what they
/// write, only that writing it does not need a server. Lines are kept so a test can
/// assert that a refusal was announced rather than silent.
/// </summary>
public sealed class TestLogger<T> : ISptLogger<T>
{
    public List<string> Lines { get; } = [];

    public void LogWithColor(string data, LogTextColor? textColor = null,
        LogBackgroundColor? backgroundColor = null, Exception? ex = null) => Lines.Add(data);

    public void Success(string data, Exception? ex = null) => Lines.Add(data);

    public void Error(string data, Exception? ex = null) => Lines.Add(data);

    public void Warning(string data, Exception? ex = null) => Lines.Add(data);

    public void Info(string data, Exception? ex = null) => Lines.Add(data);

    public void Debug(string data, Exception? ex = null) => Lines.Add(data);

    public void Critical(string data, Exception? ex = null) => Lines.Add(data);

    public void Log(LogLevel level, string data, LogTextColor? textColor = null,
        LogBackgroundColor? backgroundColor = null, Exception? ex = null) => Lines.Add(data);

    public bool IsLogEnabled(LogLevel level) => true;

    public void DumpAndStop()
    {
    }
}
