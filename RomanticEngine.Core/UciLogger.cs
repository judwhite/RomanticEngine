namespace RomanticEngine.Core;

public sealed class UciLogger : IDisposable
{
    private string _filePath = string.Empty;
    private StreamWriter? _writer;
    private readonly Lock _lock = new();

    public void SetLogFile(string path, Action<string>? onInfo)
    {
        lock (_lock)
        {
            if (_filePath == path)
                return;

            TryDisposeWriter();

            _filePath = path;

            if (string.IsNullOrWhiteSpace(_filePath))
                return;

            try
            {
                var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                onInfo?.Invoke($"logging to {_filePath}");
            }
            catch (Exception ex)
            {
                onInfo?.Invoke($"unable to open debug log file {_filePath}: {ex.Message}");
                TryDisposeWriter();
            }
        }
    }

    public void Log(LogDirection direction, string message)
    {
        lock (_lock)
        {
            if (_writer == null)
                return;

            try
            {
                var dir = direction switch
                {
                    LogDirection.In => "IN ",
                    LogDirection.Out => "OUT",
                    _ => "???"
                };

                _writer.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{dir}] {message}");
            }
            catch
            {
                TryDisposeWriter();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            TryDisposeWriter();
        }
    }

    private void TryDisposeWriter()
    {
        try
        {
            _filePath = string.Empty;
            _writer?.Dispose();
        }
        catch
        {
            // Suppress errors to allow a new log file to be opened
        }
        finally
        {
            _writer = null;
        }
    }
}

public enum LogDirection
{
    In,
    Out,
}
