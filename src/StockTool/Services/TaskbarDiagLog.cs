using System.Diagnostics;
using System.IO;
using System.Text;

namespace StockTool.Services;

/// <summary>任务栏叠层诊断日志，写入 %AppData%\StockTool\logs\taskbar-overlay.log</summary>
public static class TaskbarDiagLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string LogPath
    {
        get
        {
            EnsurePath();
            return _path!;
        }
    }

    public static void Info(string message) => Write("INFO", message, includeStack: false);

    public static void Warn(string message) => Write("WARN", message, includeStack: false);

    public static void Error(string message, Exception? ex = null)
    {
        var sb = new StringBuilder(message);
        if (ex != null)
            sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        Write("ERROR", sb.ToString(), includeStack: false);
        if (ex != null)
            Write("ERROR", ex.ToString(), includeStack: false);
    }

    /// <summary>关键路径（关闭窗口 / Hide / items=0）附带精简调用栈。</summary>
    public static void Trace(string message) => Write("TRACE", message, includeStack: true);

    private static void EnsurePath()
    {
        if (_path != null) return;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockTool", "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "taskbar-overlay.log");
    }

    private static void Write(string level, string message, bool includeStack)
    {
        try
        {
            EnsurePath();
            var line = new StringBuilder(256);
            line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            line.Append(" [").Append(level).Append("] ");
            line.Append("T").Append(Environment.CurrentManagedThreadId).Append(' ');
            line.Append(message);

            if (includeStack)
            {
                var st = new StackTrace(1, fNeedFileInfo: false);
                var frames = st.GetFrames();
                if (frames != null)
                {
                    int n = 0;
                    foreach (var f in frames)
                    {
                        var m = f.GetMethod();
                        if (m == null) continue;
                        var type = m.DeclaringType?.FullName ?? "?";
                        if (type.StartsWith("System.", StringComparison.Ordinal)
                            || type.StartsWith("MS.Internal", StringComparison.Ordinal)
                            || type.Contains("TaskbarDiagLog", StringComparison.Ordinal))
                            continue;
                        line.Append(" <- ").Append(type).Append('.').Append(m.Name);
                        if (++n >= 8) break;
                    }
                }
            }

            line.AppendLine();
            lock (Gate)
                File.AppendAllText(_path!, line.ToString(), Encoding.UTF8);
        }
        catch
        {
            // 诊断日志失败不影响主流程
        }
    }
}
