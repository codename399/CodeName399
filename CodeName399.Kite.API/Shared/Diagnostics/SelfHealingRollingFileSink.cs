using Serilog.Core;
using Serilog.Events;
using System.Globalization;
using System.Text;

namespace CodeName399.Kite.API.Diagnostics;

/// <summary>
/// Writes one log event by opening the current day's file for each event.
/// Unlike Serilog's long-lived file sink, this intentionally does not keep a
/// file handle open. If the log file is deleted while the application is
/// running, the next log event recreates it automatically.
/// </summary>
public sealed class SelfHealingRollingFileSink : ILogEventSink
{
    private readonly string _directory;
    private readonly object _sync = new();
    private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public SelfHealingRollingFileSink(string directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        Directory.CreateDirectory(_directory);
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
            return;

        try
        {
            var filePath = Path.Combine(
                _directory,
                $"log-{logEvent.Timestamp:yyyyMMdd}.txt");

            var line = Format(logEvent);

            lock (_sync)
            {
                Directory.CreateDirectory(_directory);

                // Open/close for every event so a deleted log file is recreated
                // on the very next event without requiring an application restart.
                using var stream = new FileStream(
                    filePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);

                using var writer = new StreamWriter(stream, _encoding);
                writer.WriteLine(line);
                writer.Flush();
            }
        }
        catch
        {
            // Logging must never take down the trading process.
            // Console logging remains available as the secondary sink.
        }
    }

    private static string Format(LogEvent logEvent)
    {
        var timestamp = logEvent.Timestamp.ToString(
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            CultureInfo.InvariantCulture);

        var message = logEvent.RenderMessage(CultureInfo.InvariantCulture);

        var properties = logEvent.Properties.Count == 0
            ? string.Empty
            : " " + string.Join(
                " ",
                logEvent.Properties.Select(p =>
                    $"{p.Key}={p.Value}"));

        var exception = logEvent.Exception is null
            ? string.Empty
            : Environment.NewLine + logEvent.Exception;

        return $"{timestamp} [{logEvent.Level}] {message}{properties}{exception}";
    }
}
