// author: https://github.com/pabloFuente

using System.Text;

namespace LiveKit.Rtc.Tests.Reliability;

/// <summary>
/// Captures what the SDK writes to <see cref="Console.Error"/> during a test. The SDK reports
/// what it could not handle there (a dropped room event, a handler that threw, an FFI callback
/// that failed) instead of throwing, so a reliability test that passes while the SDK logged
/// <c>[WARN] RoomEvent...</c> hides a bug. The reliability collections run serially, so the
/// lines written during a scope belong to its test.
/// </summary>
internal static class SdkLog
{
    private static readonly object InstallLock = new();
    private static LineCapture? s_capture;

    public static SdkLogScope BeginScope()
    {
        lock (InstallLock)
        {
            if (s_capture == null)
            {
                // Wraps whatever stderr is now (the LIVEKIT_TEST_STDERR_FILE tee included)
                s_capture = new LineCapture(Console.Error);
                Console.SetError(TextWriter.Synchronized(s_capture));
            }
            return new SdkLogScope(s_capture);
        }
    }

    internal sealed class LineCapture : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly object _lock = new();
        private readonly List<string> _lines = new();
        private readonly StringBuilder _current = new();

        public LineCapture(TextWriter inner)
        {
            _inner = inner;
        }

        public override Encoding Encoding => _inner.Encoding;

        public int Mark()
        {
            lock (_lock)
                return _lines.Count;
        }

        public List<string> Since(int mark)
        {
            lock (_lock)
                return _lines.Skip(mark).ToList();
        }

        public override void Write(char value)
        {
            _inner.Write(value);
            Append(value);
        }

        public override void Write(string? value)
        {
            _inner.Write(value);
            AppendAll(value);
        }

        public override void WriteLine(string? value)
        {
            _inner.WriteLine(value);
            AppendAll(value);
            Append('\n');
        }

        public override void WriteLine()
        {
            _inner.WriteLine();
            Append('\n');
        }

        public override void Flush() => _inner.Flush();

        private void AppendAll(string? value)
        {
            if (value != null)
                foreach (var c in value)
                    Append(c);
        }

        private void Append(char c)
        {
            lock (_lock)
            {
                if (c == '\n')
                {
                    _lines.Add(_current.ToString().TrimEnd('\r'));
                    _current.Clear();
                }
                else
                {
                    _current.Append(c);
                }
            }
        }
    }
}

internal sealed class SdkLogScope
{
    // What the SDK writes when it drops, cannot apply or fails to deliver something
    private static readonly string[] ProblemMarkers =
    {
        "[WARN]",
        "[ERROR]",
        "FFI Panic",
        "Error in FFI Callback",
    };

    // Expected lines that may land in any later test: a connect cancelled on purpose whose
    // callback the FFI never delivers is given up on, and logged, 60 s later
    private static readonly string[] AlwaysAllowed =
    {
        "[WARN] Could not release an abandoned room connection: FFI event wait timed out",
    };

    private readonly SdkLog.LineCapture _capture;
    private readonly int _mark;

    public SdkLogScope(SdkLog.LineCapture capture)
    {
        _capture = capture;
        _mark = capture.Mark();
    }

    public IReadOnlyList<string> Lines => _capture.Since(_mark);

    public IReadOnlyList<string> Problems(params string[] allowed) =>
        Lines
            .Where(line => ProblemMarkers.Any(marker => line.Contains(marker)))
            .Where(line => !allowed.Any(line.Contains) && !AlwaysAllowed.Any(line.Contains))
            .ToList();

    /// <summary>
    /// Fails if the SDK reported a problem during the scope, except the lines containing one of
    /// <paramref name="allowed"/> (the problems the test provokes on purpose).
    /// </summary>
    public void AssertNoProblems(params string[] allowed)
    {
        var problems = Problems(allowed);
        Assert.True(
            problems.Count == 0,
            "The SDK reported problems on stderr:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Take(20))
        );
    }
}
