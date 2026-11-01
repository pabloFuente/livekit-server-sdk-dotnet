// author: https://github.com/pabloFuente

using System.Runtime.CompilerServices;
using System.Text;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// The SDK reports dropped events and handler failures on <see cref="Console.Error"/> with
/// <c>[WARN]</c> and <c>[ERROR]</c> prefixes, but <c>dotnet test</c> does not surface the test
/// host's stderr. When <c>LIVEKIT_TEST_STDERR_FILE</c> is set, everything written to stderr is
/// also appended to that file, so a run can be checked with
/// <c>grep "\[WARN\] RoomEvent" $LIVEKIT_TEST_STDERR_FILE</c>.
/// </summary>
internal static class SdkStderrCapture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var path = Environment.GetEnvironmentVariable("LIVEKIT_TEST_STDERR_FILE");
        if (string.IsNullOrEmpty(path))
            return;

        var file = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
        )
        {
            AutoFlush = true,
        };
        Console.SetError(TextWriter.Synchronized(new TeeWriter(Console.Error, file)));
    }

    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _first;
        private readonly TextWriter _second;

        public TeeWriter(TextWriter first, TextWriter second)
        {
            _first = first;
            _second = second;
        }

        public override Encoding Encoding => _first.Encoding;

        public override void Write(char value)
        {
            _first.Write(value);
            _second.Write(value);
        }

        public override void Write(string? value)
        {
            _first.Write(value);
            _second.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _first.WriteLine(value);
            _second.WriteLine(value);
        }

        public override void Flush()
        {
            _first.Flush();
            _second.Flush();
        }
    }
}
