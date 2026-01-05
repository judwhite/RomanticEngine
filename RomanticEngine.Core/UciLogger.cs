namespace RomanticEngine.Core;

public sealed class UciLogger : IDisposable
{
    private string? _filePath;
    private StreamWriter? _writer;
    private readonly Lock _lock = new();

    public void SetLogFile(string? path, Action<string>? onInfo = null)
    {
        lock (_lock)
        {
            if (_filePath == path) return;

            _writer?.Dispose();
            _writer = null;
            _filePath = path;

            if (string.IsNullOrWhiteSpace(_filePath)) return;

            try
            {
                var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                onInfo?.Invoke($"string logging to {_filePath}");
            }
            catch (Exception ex)
            {
                onInfo?.Invoke($"string unable to open debug log file: {ex.Message}");
                _filePath = null;
            }
        }
    }

    public void Log(string direction, string message)
    {
        if (_writer == null) return;

        lock (_lock)
        {
            try
            {
                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{direction}] {message}");
            }
            catch
            {
                // Suppress logging errors to avoid crashing search/main loop
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
        }
    }
}
