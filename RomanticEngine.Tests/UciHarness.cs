using System.Collections.Concurrent;
using RomanticEngine.Core;

namespace RomanticEngine.Tests;

public sealed class UciHarness : IDisposable
{
    private readonly Engine _engine;
    private readonly UciAdapter _adapter;
    private readonly ConcurrentQueue<string> _outputs = new();
    private readonly List<string> _allHistory = [];
    private readonly AutoResetEvent _outputEvent = new(false);

    public UciHarness()
    {
        _engine = new Engine(new FakeSystemInfo());
        _adapter = new UciAdapter(_engine, s =>
        {
            _outputs.Enqueue(s);
            lock (_allHistory) _allHistory.Add(s);
            _outputEvent.Set();
        });
    }

    public IReadOnlyList<string> AllHistory
    {
        get
        {
            lock (_allHistory) return _allHistory.ToList();
        }
    }

    public void Send(string command) => _adapter.ReceiveCommand(command);

    public string? WaitForLine(Func<string, bool> predicate, int timeoutMs = 5000)
    {
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
        {
            if (_outputs.TryDequeue(out var line))
            {
                if (predicate(line)) return line;
            }
            else
            {
                _outputEvent.WaitOne(100);
            }
        }

        return null;
    }

    public List<string> DrainOutput()
    {
        var result = new List<string>();
        while (_outputs.TryDequeue(out var line)) result.Add(line);
        return result;
    }

    public void Dispose()
    {
        _outputEvent.Dispose();
        _engine.Stop();
    }

    private class FakeSystemInfo : ISystemInfo
    {
        public int MaxThreads => 28;
        public int MaxHashMb => 120395;
    }
}
