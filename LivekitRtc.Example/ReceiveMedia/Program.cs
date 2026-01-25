using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;

const int BitsPerSample = 16;
const string WavFile = "output.wav";

var url = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("LIVEKIT_URL");
var token = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("LIVEKIT_TOKEN");

if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
{
    Console.WriteLine("Usage: dotnet run <LIVEKIT_URL> <ACCESS_TOKEN>");
    return 1;
}

Console.WriteLine("=== LiveKit RTC - Receive Audio Example ===\n");
Console.WriteLine("This example receives the first audio track published in a room");
Console.WriteLine("and writes that audio data to a WAV file.\n");

var room = new Room();
var cts = new CancellationTokenSource();
FileStream? wavWriter = null;
string? trackToProcess = null;

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nShutting down...");
};

// Subscribe to new tracks - only process the first audio track
room.TrackSubscribed += (sender, e) =>
{
    Console.WriteLine($"✓ Track subscribed: {e.Track.Kind} from {e.Participant.Identity}");

    // Only process audio tracks and only if we haven't started processing one yet
    if (e.Track is RemoteAudioTrack audioTrack && trackToProcess == null)
    {
        trackToProcess = e.Track.Sid;
        Console.WriteLine($"  Recording first audio track to {WavFile}");

        _ = Task.Run(async () =>
        {
            try
            {
                using var audioStream = new AudioStream(audioTrack);

                await foreach (var frameEvent in audioStream.WithCancellation(cts.Token))
                {
                    // Stop if this track is no longer the one we want to process
                    if (trackToProcess != e.Track.Sid)
                    {
                        break;
                    }

                    // Create WAV file on first frame
                    if (wavWriter == null)
                    {
                        wavWriter = new FileStream(WavFile, FileMode.Create, FileAccess.Write);
                        WriteWavHeader(wavWriter, frameEvent.Frame);
                        Console.WriteLine(
                            $"  Format: {frameEvent.Frame.SampleRate}Hz, {frameEvent.Frame.NumChannels} channels, {BitsPerSample}-bit PCM"
                        );
                    }

                    // Write audio data as 16-bit PCM
                    var samples = frameEvent.Frame.DataBytes;
                    wavWriter.Write(samples, 0, samples.Length);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error processing audio: {ex.Message}");
            }
        });
    }
};

// Handle track unsubscription
room.TrackUnsubscribed += (sender, e) =>
{
    Console.WriteLine($"✗ Track unsubscribed: {e.Track.Kind} from {e.Participant.Identity}");

    if (e.Track.Sid == trackToProcess)
    {
        trackToProcess = null;

        if (wavWriter != null)
        {
            wavWriter.Close();
            UpdateWavHeader(WavFile);
            Console.WriteLine($"✓ Recording saved to {WavFile}");
            wavWriter = null;
        }

        // Signal completion
        cts.Cancel();
    }
};

try
{
    await room.ConnectAsync(url, token, new RoomOptions { AutoSubscribe = true });
    Console.WriteLine($"✓ Connected to room: {room.Name}");
    Console.WriteLine("  Waiting for the first audio track to be published...");
    Console.WriteLine("  Press Ctrl+C to stop recording\n");

    // Wait until cancelled
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Normal shutdown
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
    return 1;
}
finally
{
    if (wavWriter != null)
    {
        wavWriter.Close();
        UpdateWavHeader(WavFile);
        Console.WriteLine($"✓ Recording saved to {WavFile}");
    }

    await room.DisconnectAsync();
    room.Dispose();
    Console.WriteLine("✓ Disconnected");
}

return 0;

static void WriteWavHeader(FileStream writer, AudioFrame frame)
{
    var header = new byte[44];
    var byteRate = (frame.SampleRate * frame.NumChannels * BitsPerSample) / 8;
    var blockAlign = (frame.NumChannels * BitsPerSample) / 8;

    // RIFF header
    WriteString(header, 0, "RIFF");
    WriteUInt32LE(header, 4, 0); // ChunkSize placeholder
    WriteString(header, 8, "WAVE");

    // fmt subchunk
    WriteString(header, 12, "fmt ");
    WriteUInt32LE(header, 16, 16); // Subchunk1Size (PCM)
    WriteUInt16LE(header, 20, 1); // AudioFormat (PCM = 1)
    WriteUInt16LE(header, 22, (ushort)frame.NumChannels);
    WriteUInt32LE(header, 24, (uint)frame.SampleRate);
    WriteUInt32LE(header, 28, (uint)byteRate);
    WriteUInt16LE(header, 32, (ushort)blockAlign);
    WriteUInt16LE(header, 34, BitsPerSample);

    // data subchunk
    WriteString(header, 36, "data");
    WriteUInt32LE(header, 40, 0); // Subchunk2Size placeholder

    writer.Write(header, 0, header.Length);
}

static void UpdateWavHeader(string path)
{
    var fileInfo = new FileInfo(path);
    var fileSize = fileInfo.Length;

    var chunkSize = (uint)(fileSize - 8);
    var subchunk2Size = (uint)(fileSize - 44);

    using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
    {
        // Update ChunkSize at offset 4
        fs.Seek(4, SeekOrigin.Begin);
        var chunkSizeBytes = new byte[4];
        WriteUInt32LE(chunkSizeBytes, 0, chunkSize);
        fs.Write(chunkSizeBytes, 0, 4);

        // Update Subchunk2Size at offset 40
        fs.Seek(40, SeekOrigin.Begin);
        var subchunk2SizeBytes = new byte[4];
        WriteUInt32LE(subchunk2SizeBytes, 0, subchunk2Size);
        fs.Write(subchunk2SizeBytes, 0, 4);
    }
}

static void WriteString(byte[] buffer, int offset, string value)
{
    for (int i = 0; i < value.Length; i++)
    {
        buffer[offset + i] = (byte)value[i];
    }
}

static void WriteUInt16LE(byte[] buffer, int offset, ushort value)
{
    buffer[offset] = (byte)(value & 0xFF);
    buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
}

static void WriteUInt32LE(byte[] buffer, int offset, uint value)
{
    buffer[offset] = (byte)(value & 0xFF);
    buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
    buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
}
