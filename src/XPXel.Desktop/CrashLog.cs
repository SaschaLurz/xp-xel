using System.Diagnostics;
using System.Globalization;

namespace XPXel.Desktop;

/// <summary>
/// Writes what went wrong to a file the artist can find and send on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> An unhandled exception in a desktop application is a dialog nobody
/// can read followed by a process that is gone, and with it any answer to "what was it doing?".
/// A drawing tool that throws is a bug report worth having, and the artist should not have to
/// reproduce it in front of someone to file it. Everything lands beside the autosaved backups,
/// which is the one folder they are already told about.
/// </para>
/// <para>
/// <b>It changes nothing about how the application behaves.</b> The handlers observe and write;
/// they do not swallow. An exception that would have brought the process down still does, and
/// the backups taken in the three minutes before it are still on disk.
/// </para>
/// <para>
/// Avalonia's own diagnostics — binding failures above all, which are silent by design — are
/// routed to the same file, because a binding that quietly does nothing looks exactly like a
/// feature that does not work.
/// </para>
/// </remarks>
internal static class CrashLog
{
    private static readonly object Gate = new();

    private static string? _path;

    /// <summary>Where the log is written, for showing the artist.</summary>
    public static string Path => _path ??= System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XP-xel",
        "Logs",
        "xpxel.log");

    /// <summary>Starts recording unhandled exceptions and framework diagnostics.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("Unhandled exception", e.ExceptionObject as Exception);

        // An exception inside an async void handler — a command, a pointer handler — never
        // reaches the line above, and this is the only place it surfaces at all.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            Trace.Listeners.Add(new TextWriterTraceListener(Path) { TraceOutputOptions = TraceOptions.None });
            Trace.AutoFlush = true;

            Write("Session started", null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log that cannot be written must never be the reason the editor will not start.
        }
    }

    /// <summary>Records one entry.</summary>
    /// <param name="what">A short description of the event.</param>
    /// <param name="error">The exception, if there was one.</param>
    public static void Write(string what, Exception? error)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string body = error is null ? string.Empty : Environment.NewLine + error;

                File.AppendAllText(Path, $"[{stamp}] {what}{body}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful can be done about a log that will not write.
        }
    }
}
