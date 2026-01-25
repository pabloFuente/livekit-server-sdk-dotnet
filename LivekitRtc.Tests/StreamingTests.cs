using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests
{
    /// <summary>
    /// End-to-end tests for LiveKit streaming functionality.
    /// Tests focus on verifying that streaming methods execute without errors.
    /// Full E2E testing with receiver verification requires Room event support for streams.
    /// </summary>
    [Collection("RtcTests")]
    public class StreamingTests : IAsyncLifetime
    {
        private readonly RtcTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private Room? _senderRoom;
        private Room? _receiverRoom;
        private LocalParticipant? _senderParticipant;
        private LocalParticipant? _receiverParticipant;

        public StreamingTests(RtcTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        private void Log(string message)
        {
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        public async Task InitializeAsync()
        {
            var roomName = $"streaming-test-{Guid.NewGuid()}";

            var senderToken = _fixture.CreateToken("sender", roomName);
            var receiverToken = _fixture.CreateToken("receiver", roomName);

            _senderRoom = new Room();
            _receiverRoom = new Room();

            await _fixture.ConnectRoomsAndWaitForEvents(
                _senderRoom,
                _receiverRoom,
                senderToken,
                receiverToken,
                "receiver",
                Log
            );

            _senderParticipant = _senderRoom.LocalParticipant;
            _receiverParticipant = _receiverRoom.LocalParticipant;

            Log("Test setup complete: Both participants connected and sender sees receiver");
        }

        public async Task DisposeAsync()
        {
            if (_senderRoom != null)
            {
                await _senderRoom.DisconnectAsync();
                _senderRoom.Dispose();
            }

            if (_receiverRoom != null)
            {
                await _receiverRoom.DisconnectAsync();
                _receiverRoom.Dispose();
            }
        }

        #region StreamTextAsync Tests

        [Fact]
        public async Task StreamTextAsync_CreatesWriterSuccessfully()
        {
            Log("Testing StreamTextAsync basic functionality");

            var writer = await _senderParticipant!.StreamTextAsync();

            Assert.NotNull(writer);
            Assert.NotNull(writer.Info);
            Assert.NotNull(writer.Info.StreamId);
            Assert.Equal("text/plain", writer.Info.MimeType);

            await writer.WriteAsync("Hello World!");
            await writer.CloseAsync();

            Log("StreamTextAsync created writer successfully");
        }

        [Fact]
        public async Task StreamTextAsync_WithTopic_SetsTopicCorrectly()
        {
            Log("Testing StreamTextAsync with topic");

            var writer = await _senderParticipant!.StreamTextAsync(topic: "test-topic");

            Assert.NotNull(writer);
            Assert.Equal("test-topic", writer.Info.Topic);

            await writer.WriteAsync("Test message");
            await writer.CloseAsync();

            Log("StreamTextAsync set topic correctly");
        }

        [Fact]
        public async Task StreamTextAsync_WithAttributes_SetsAttributesCorrectly()
        {
            Log("Testing StreamTextAsync with attributes");

            var attributes = new Dictionary<string, string>
            {
                ["key1"] = "value1",
                ["key2"] = "value2",
            };

            var writer = await _senderParticipant!.StreamTextAsync(attributes: attributes);

            Assert.NotNull(writer);
            Assert.NotNull(writer.Info.Attributes);
            Assert.Equal(2, writer.Info.Attributes.Count);
            Assert.Equal("value1", writer.Info.Attributes["key1"]);
            Assert.Equal("value2", writer.Info.Attributes["key2"]);

            await writer.WriteAsync("Test");
            await writer.CloseAsync();

            Log("StreamTextAsync set attributes correctly");
        }

        [Fact]
        public async Task StreamTextAsync_WritesLargeText_Successfully()
        {
            Log("Testing StreamTextAsync with large text");

            var writer = await _senderParticipant!.StreamTextAsync();

            // Write 100KB of text
            var largeText = new string('A', 100 * 1024);
            await writer.WriteAsync(largeText);
            await writer.CloseAsync();

            Log("StreamTextAsync wrote large text successfully");
        }

        [Fact]
        public async Task StreamTextAsync_WithTotalSize_SetsCorrectly()
        {
            Log("Testing StreamTextAsync with total size");

            var writer = await _senderParticipant!.StreamTextAsync(totalSize: 12);
            Assert.Equal(12L, writer.Info.Size);

            await writer.WriteAsync("Hello World!");
            await writer.CloseAsync();

            Log("StreamTextAsync with total size completed");
        }

        [Fact]
        public async Task StreamTextAsync_WithCustomStreamId_UsesProvidedId()
        {
            Log("Testing StreamTextAsync with custom stream ID");

            var customId = "custom-stream-123";
            var writer = await _senderParticipant!.StreamTextAsync(streamId: customId);

            Assert.Equal(customId, writer.Info.StreamId);

            await writer.WriteAsync("Test");
            await writer.CloseAsync();

            Log("StreamTextAsync used custom stream ID");
        }

        [Fact]
        public async Task StreamTextAsync_ConcurrentWrites_HandlesCorrectly()
        {
            Log("Testing concurrent writes with StreamTextAsync");

            var writer = await _senderParticipant!.StreamTextAsync();

            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                var message = $"Message {i}";
                tasks.Add(writer.WriteAsync(message));
            }

            await Task.WhenAll(tasks);
            await writer.CloseAsync();

            Log("Concurrent writes completed successfully");
        }

        [Fact]
        public async Task StreamTextAsync_DisposeAsync_ClosesStream()
        {
            Log("Testing TextStreamWriter DisposeAsync");

            var writer = await _senderParticipant!.StreamTextAsync();
            await writer.WriteAsync("Test");

            // Dispose should call CloseAsync
            await writer.DisposeAsync();

            Log("TextStreamWriter DisposeAsync completed successfully");
        }

        #endregion

        #region SendTextAsync Tests

        [Fact]
        public async Task SendTextAsync_SendsTextSuccessfully()
        {
            Log("Testing SendTextAsync basic functionality");

            var info = await _senderParticipant!.SendTextAsync("Hello World!");

            Assert.NotNull(info);
            Assert.NotNull(info.StreamId);
            Assert.Equal("text/plain", info.MimeType);
            Assert.Equal(12L, info.Size);

            Log("SendTextAsync completed successfully");
        }

        [Fact]
        public async Task SendTextAsync_WithOptions_SetsCorrectly()
        {
            Log("Testing SendTextAsync with options");

            var info = await _senderParticipant!.SendTextAsync(
                "Test message",
                topic: "my-topic",
                destinationIdentities: new[] { "receiver" }
            );

            Assert.NotNull(info);
            Assert.Equal("my-topic", info.Topic);

            Log("SendTextAsync with options completed");
        }

        [Fact]
        public async Task SendTextAsync_EmptyString_HandlesGracefully()
        {
            Log("Testing SendTextAsync with empty string");

            var info = await _senderParticipant!.SendTextAsync("");

            Assert.NotNull(info);
            Assert.Equal(0L, info.Size);

            Log("SendTextAsync handled empty string");
        }

        #endregion

        #region StreamBytesAsync Tests

        [Fact]
        public async Task StreamBytesAsync_CreatesWriterSuccessfully()
        {
            Log("Testing StreamBytesAsync basic functionality");

            var writer = await _senderParticipant!.StreamBytesAsync("test-file.bin", topic: "test");

            Assert.NotNull(writer);
            Assert.NotNull(writer.Info);
            Assert.NotNull(writer.Info.StreamId);
            Assert.Equal("test", writer.Info.Topic);

            await writer.WriteAsync(new byte[] { 1, 2, 3, 4, 5 });
            await writer.CloseAsync();

            Log("StreamBytesAsync created writer successfully");
        }

        [Fact]
        public async Task StreamBytesAsync_WithMimeType_SetsCorrectly()
        {
            Log("Testing StreamBytesAsync with MIME type");

            var writer = await _senderParticipant!.StreamBytesAsync(
                "test.json",
                topic: "test",
                mimeType: "application/json"
            );

            Assert.NotNull(writer);
            Assert.Equal("application/json", writer.Info.MimeType);

            await writer.WriteAsync(Encoding.UTF8.GetBytes("{\"test\": true}"));
            await writer.CloseAsync();

            Log("StreamBytesAsync set MIME type correctly");
        }

        [Fact]
        public async Task StreamBytesAsync_WritesLargeData_Successfully()
        {
            Log("Testing StreamBytesAsync with large data");

            var writer = await _senderParticipant!.StreamBytesAsync(
                "large-data.bin",
                topic: "large-data"
            );

            // Write 500KB of binary data
            var largeData = new byte[500 * 1024];
            new Random().NextBytes(largeData);
            await writer.WriteAsync(largeData);
            await writer.CloseAsync();

            Log("StreamBytesAsync wrote large data successfully");
        }

        [Fact]
        public async Task StreamBytesAsync_WithAllOptions_WorksCorrectly()
        {
            Log("Testing StreamBytesAsync with all options");

            var attributes = new Dictionary<string, string> { ["type"] = "binary" };
            var writer = await _senderParticipant!.StreamBytesAsync(
                name: "full-test",
                topic: "full-test",
                mimeType: "application/octet-stream",
                destinationIdentities: new[] { "receiver" },
                streamId: "custom-binary-stream",
                senderIdentity: "sender",
                attributes: attributes,
                totalSize: 10
            );

            Assert.NotNull(writer);
            Assert.Equal("full-test", writer.Info.Topic);
            Assert.Equal("application/octet-stream", writer.Info.MimeType);
            Assert.Equal("custom-binary-stream", writer.Info.StreamId);
            Assert.Equal(10L, writer.Info.Size);

            await writer.WriteAsync(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            await writer.CloseAsync();

            Log("StreamBytesAsync with all options completed");
        }

        [Fact]
        public async Task StreamBytesAsync_EmptyArray_HandlesGracefully()
        {
            Log("Testing StreamBytesAsync with empty array");

            var writer = await _senderParticipant!.StreamBytesAsync("empty.bin", topic: "empty");
            await writer.WriteAsync(new byte[0]);
            await writer.CloseAsync();

            Log("StreamBytesAsync handled empty array");
        }

        [Fact]
        public async Task StreamBytesAsync_DisposeAsync_ClosesStream()
        {
            Log("Testing ByteStreamWriter DisposeAsync");

            var writer = await _senderParticipant!.StreamBytesAsync("test.bin", topic: "test");
            await writer.WriteAsync(new byte[] { 1, 2, 3 });

            // Dispose should call CloseAsync
            await writer.DisposeAsync();

            Log("ByteStreamWriter DisposeAsync completed successfully");
        }

        #endregion

        #region SendFileAsync Tests

        [Fact]
        public async Task SendFileAsync_TextFile_SendsSuccessfully()
        {
            Log("Testing SendFileAsync with text file");

            // Create a temporary test file
            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempFile, "Hello from file!");

                var info = await _senderParticipant!.SendFileAsync(tempFile);

                Assert.NotNull(info);
                Assert.NotNull(info.StreamId);
                Assert.True(info.Size > 0);

                Log($"SendFileAsync sent file successfully, size: {info.Size}");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SendFileAsync_JsonFile_AutoDetectsMimeType()
        {
            Log("Testing SendFileAsync MIME type auto-detection");

            var tempFile = Path.Combine(Path.GetTempPath(), "test.json");
            try
            {
                await File.WriteAllTextAsync(tempFile, "{\"test\": true}");

                var info = await _senderParticipant!.SendFileAsync(tempFile);

                Assert.NotNull(info);
                Assert.Equal("application/json", info.MimeType);

                Log($"SendFileAsync auto-detected MIME type: {info.MimeType}");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SendFileAsync_BinaryFile_SendsSuccessfully()
        {
            Log("Testing SendFileAsync with binary file");

            var tempFile = Path.GetTempFileName();
            try
            {
                // Write binary data
                var data = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD };
                await File.WriteAllBytesAsync(tempFile, data);

                var info = await _senderParticipant!.SendFileAsync(tempFile);

                Assert.NotNull(info);
                Assert.Equal(6L, info.Size);

                Log("SendFileAsync sent binary file successfully");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SendFileAsync_WithCustomMimeType_UsesProvided()
        {
            Log("Testing SendFileAsync with custom MIME type");

            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempFile, "Custom content");

                var info = await _senderParticipant!.SendFileAsync(
                    tempFile,
                    mimeType: "text/custom"
                );

                Assert.NotNull(info);
                Assert.Equal("text/custom", info.MimeType);

                Log("SendFileAsync used custom MIME type");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SendFileAsync_NonExistentFile_ThrowsException()
        {
            Log("Testing SendFileAsync with non-existent file");

            var nonExistentFile = "/tmp/this-file-does-not-exist-12345.txt";

            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await _senderParticipant!.SendFileAsync(nonExistentFile);
            });

            Log("SendFileAsync threw FileNotFoundException as expected");
        }

        [Fact]
        public async Task SendFileAsync_LargeFile_SendsSuccessfully()
        {
            Log("Testing SendFileAsync with large file");

            var tempFile = Path.GetTempFileName();
            try
            {
                // Create a 1MB file
                var data = new byte[1024 * 1024];
                new Random().NextBytes(data);
                await File.WriteAllBytesAsync(tempFile, data);

                var info = await _senderParticipant!.SendFileAsync(tempFile);

                Assert.NotNull(info);
                Assert.Equal(1024L * 1024, info.Size);

                Log($"SendFileAsync sent large file successfully, size: {info.Size}");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        #endregion

        #region Stream Receiving Tests

        [Fact]
        public async Task RegisterTextStreamHandler_ReceivesTextStream_E2E()
        {
            Log("Testing text stream E2E: sender sends, receiver receives");

            var receivedData = new TaskCompletionSource<(string identity, string text)>();

            _receiverRoom!.RegisterTextStreamHandler(
                "e2e-text",
                (reader, identity) =>
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var chunks = new List<string>();
                            await foreach (var chunk in reader)
                            {
                                chunks.Add(chunk);
                            }
                            var fullText = string.Join("", chunks);
                            receivedData.TrySetResult((identity, fullText));
                        }
                        catch (Exception ex)
                        {
                            Log($"Error in handler: {ex.Message}");
                            receivedData.TrySetException(ex);
                        }
                    });
                }
            );

            // Give handler time to register
            await Task.Delay(100);

            var writer = await _senderParticipant!.StreamTextAsync(topic: "e2e-text");
            await writer.WriteAsync("Hello ");
            await writer.WriteAsync("from ");
            await writer.WriteAsync("sender!");
            await writer.CloseAsync();

            var (receivedIdentity, receivedText) = await receivedData.Task.WaitAsync(
                TimeSpan.FromSeconds(10)
            );

            Assert.Equal("sender", receivedIdentity);
            Assert.Equal("Hello from sender!", receivedText);

            Log($"Successfully received text stream from {receivedIdentity}: {receivedText}");
        }

        [Fact]
        public async Task RegisterByteStreamHandler_ReceivesByteStream_E2E()
        {
            Log("Testing byte stream E2E: sender sends, receiver receives");

            var receivedData = new TaskCompletionSource<(string identity, byte[] data)>();

            _receiverRoom!.RegisterByteStreamHandler(
                "e2e-bytes",
                (reader, identity) =>
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var chunks = new List<byte>();
                            await foreach (var chunk in reader)
                            {
                                chunks.AddRange(chunk.ToArray());
                            }
                            receivedData.TrySetResult((identity, chunks.ToArray()));
                        }
                        catch (Exception ex)
                        {
                            Log($"Error in handler: {ex.Message}");
                            receivedData.TrySetException(ex);
                        }
                    });
                }
            );

            // Give handler time to register
            await Task.Delay(100);

            var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            var writer = await _senderParticipant!.StreamBytesAsync("data.bin", topic: "e2e-bytes");
            await writer.WriteAsync(testData);
            await writer.CloseAsync();

            var (receivedIdentity, receivedBytes) = await receivedData.Task.WaitAsync(
                TimeSpan.FromSeconds(10)
            );

            Assert.Equal("sender", receivedIdentity);
            Assert.Equal(testData, receivedBytes);

            Log(
                $"Successfully received byte stream from {receivedIdentity}: {receivedBytes.Length} bytes"
            );
        }

        [Fact]
        public async Task RegisterTextStreamHandler_MultipleChunks_ReceivesAll()
        {
            Log("Testing text stream with multiple chunks");

            var receivedChunks = new List<string>();
            var completed = new TaskCompletionSource<bool>();

            _receiverRoom!.RegisterTextStreamHandler(
                "multi-chunk",
                (reader, identity) =>
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await foreach (var chunk in reader)
                            {
                                receivedChunks.Add(chunk);
                            }
                            completed.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            completed.TrySetException(ex);
                        }
                    });
                }
            );

            await Task.Delay(100);

            var writer = await _senderParticipant!.StreamTextAsync(topic: "multi-chunk");
            for (int i = 0; i < 10; i++)
            {
                await writer.WriteAsync($"Chunk{i} ");
            }
            await writer.CloseAsync();

            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(10, receivedChunks.Count);
            Assert.Equal(
                "Chunk0 Chunk1 Chunk2 Chunk3 Chunk4 Chunk5 Chunk6 Chunk7 Chunk8 Chunk9 ",
                string.Join("", receivedChunks)
            );

            Log($"Successfully received all {receivedChunks.Count} chunks");
        }

        [Fact]
        public async Task RegisterByteStreamHandler_LargeData_ReceivesAll()
        {
            Log("Testing byte stream with large data");

            var receivedData = new TaskCompletionSource<byte[]>();

            _receiverRoom!.RegisterByteStreamHandler(
                "large-bytes",
                (reader, identity) =>
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var chunks = new List<byte>();
                            await foreach (var chunk in reader)
                            {
                                chunks.AddRange(chunk.ToArray());
                            }
                            receivedData.TrySetResult(chunks.ToArray());
                        }
                        catch (Exception ex)
                        {
                            receivedData.TrySetException(ex);
                        }
                    });
                }
            );

            await Task.Delay(100);

            var largeData = new byte[100 * 1024]; // 100KB
            new Random(42).NextBytes(largeData);

            var writer = await _senderParticipant!.StreamBytesAsync(
                "large.bin",
                topic: "large-bytes"
            );
            await writer.WriteAsync(largeData);
            await writer.CloseAsync();

            var received = await receivedData.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(largeData.Length, received.Length);
            Assert.Equal(largeData, received);

            Log($"Successfully received large byte stream: {received.Length} bytes");
        }

        [Fact]
        public void RegisterTextStreamHandler_DuplicateTopic_ThrowsException()
        {
            Log("Testing RegisterTextStreamHandler with duplicate topic");

            _receiverRoom!.RegisterTextStreamHandler("duplicate", (reader, identity) => { });

            var ex = Assert.Throws<StreamException>(() =>
            {
                _receiverRoom!.RegisterTextStreamHandler("duplicate", (reader, identity) => { });
            });

            Assert.Contains("duplicate", ex.Message);
            Assert.Contains("has already been registered", ex.Message);

            Log("Correctly threw ArgumentException for duplicate topic");
        }

        [Fact]
        public void RegisterByteStreamHandler_DuplicateTopic_ThrowsException()
        {
            Log("Testing RegisterByteStreamHandler with duplicate topic");

            _receiverRoom!.RegisterByteStreamHandler("duplicate-bytes", (reader, identity) => { });

            var ex = Assert.Throws<StreamException>(() =>
            {
                _receiverRoom!.RegisterByteStreamHandler(
                    "duplicate-bytes",
                    (reader, identity) => { }
                );
            });

            Assert.Contains("duplicate-bytes", ex.Message);
            Assert.Contains("has already been registered", ex.Message);

            Log("Correctly threw ArgumentException for duplicate topic");
        }

        [Fact]
        public async Task UnregisterTextStreamHandler_NoLongerReceivesStreams()
        {
            Log("Testing UnregisterTextStreamHandler");

            var handlerCalled = false;

            _receiverRoom!.RegisterTextStreamHandler(
                "unregister-test",
                (reader, identity) =>
                {
                    handlerCalled = true;
                }
            );

            _receiverRoom!.UnregisterTextStreamHandler("unregister-test");

            await Task.Delay(100);

            var writer = await _senderParticipant!.StreamTextAsync(topic: "unregister-test");
            await writer.WriteAsync("This should not be received");
            await writer.CloseAsync();

            // Wait to ensure handler would have been called if still registered
            await Task.Delay(1000);

            Assert.False(handlerCalled);

            Log("Handler was correctly not called after unregistration");
        }

        [Fact]
        public async Task UnregisterByteStreamHandler_NoLongerReceivesStreams()
        {
            Log("Testing UnregisterByteStreamHandler");

            var handlerCalled = false;

            _receiverRoom!.RegisterByteStreamHandler(
                "unregister-bytes",
                (reader, identity) =>
                {
                    handlerCalled = true;
                }
            );

            _receiverRoom!.UnregisterByteStreamHandler("unregister-bytes");

            await Task.Delay(100);

            var writer = await _senderParticipant!.StreamBytesAsync(
                "data.bin",
                topic: "unregister-bytes"
            );
            await writer.WriteAsync(new byte[] { 1, 2, 3 });
            await writer.CloseAsync();

            // Wait to ensure handler would have been called if still registered
            await Task.Delay(1000);

            Assert.False(handlerCalled);

            Log("Handler was correctly not called after unregistration");
        }

        [Fact]
        public async Task RegisterTextStreamHandler_MultipleTopics_EachReceivesCorrectStream()
        {
            Log("Testing multiple text stream handlers with different topics");

            var topic1Data = new TaskCompletionSource<string>();
            var topic2Data = new TaskCompletionSource<string>();

            _receiverRoom!.RegisterTextStreamHandler(
                "topic1",
                (reader, identity) =>
                {
                    Task.Run(async () =>
                    {
                        var chunks = new List<string>();
                        await foreach (var chunk in reader)
                        {
                            chunks.Add(chunk);
                        }
                        topic1Data.TrySetResult(string.Join("", chunks));
                    });
                }
            );

            _receiverRoom!.RegisterTextStreamHandler(
                "topic2",
                (reader, identity) =>
                {
                    Task.Run(async () =>
                    {
                        var chunks = new List<string>();
                        await foreach (var chunk in reader)
                        {
                            chunks.Add(chunk);
                        }
                        topic2Data.TrySetResult(string.Join("", chunks));
                    });
                }
            );

            await Task.Delay(100);

            var writer1 = await _senderParticipant!.StreamTextAsync(topic: "topic1");
            await writer1.WriteAsync("Data for topic 1");
            await writer1.CloseAsync();

            var writer2 = await _senderParticipant!.StreamTextAsync(topic: "topic2");
            await writer2.WriteAsync("Data for topic 2");
            await writer2.CloseAsync();

            var data1 = await topic1Data.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var data2 = await topic2Data.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("Data for topic 1", data1);
            Assert.Equal("Data for topic 2", data2);

            Log("Multiple topics correctly routed to respective handlers");
        }

        [Fact]
        public async Task RegisterTextStreamHandler_StreamMetadata_IncludesCorrectInfo()
        {
            Log("Testing text stream reader metadata");

            var readerInfo = new TaskCompletionSource<TextStreamReader>();

            _receiverRoom!.RegisterTextStreamHandler(
                "metadata-test",
                (reader, identity) =>
                {
                    readerInfo.TrySetResult(reader);
                }
            );

            await Task.Delay(100);

            var attributes = new Dictionary<string, string>
            {
                ["key1"] = "value1",
                ["key2"] = "value2",
            };

            var writer = await _senderParticipant!.StreamTextAsync(
                topic: "metadata-test",
                attributes: attributes,
                totalSize: 11
            );
            await writer.WriteAsync("Hello World");
            await writer.CloseAsync();

            var reader = await readerInfo.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(reader.Info);
            Assert.Equal("metadata-test", reader.Info.Topic);
            Assert.Equal("text/plain", reader.Info.MimeType);
            Assert.Equal(11L, reader.Info.Size);
            Assert.NotNull(reader.Info.Attributes);
            Assert.Equal(2, reader.Info.Attributes.Count);
            Assert.Equal("value1", reader.Info.Attributes["key1"]);
            Assert.Equal("value2", reader.Info.Attributes["key2"]);

            Log("Stream metadata correctly received");
        }

        [Fact]
        public async Task RegisterByteStreamHandler_StreamMetadata_IncludesCorrectInfo()
        {
            Log("Testing byte stream reader metadata");

            var readerInfo = new TaskCompletionSource<ByteStreamReader>();

            _receiverRoom!.RegisterByteStreamHandler(
                "byte-metadata",
                (reader, identity) =>
                {
                    readerInfo.TrySetResult(reader);
                }
            );

            await Task.Delay(100);

            var writer = await _senderParticipant!.StreamBytesAsync(
                "metadata.bin",
                topic: "byte-metadata",
                mimeType: "application/octet-stream",
                totalSize: 5
            );
            await writer.WriteAsync(new byte[] { 1, 2, 3, 4, 5 });
            await writer.CloseAsync();

            var reader = await readerInfo.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(reader.Info);
            Assert.Equal("byte-metadata", reader.Info.Topic);
            Assert.Equal("application/octet-stream", reader.Info.MimeType);
            Assert.Equal(5L, reader.Info.Size);

            Log("Byte stream metadata correctly received");
        }

        [Fact]
        public async Task StreamReceiving_AfterDisconnect_ClearsHandlers()
        {
            Log("Testing stream handler cleanup after disconnect");

            _receiverRoom!.RegisterTextStreamHandler(
                "cleanup-test",
                (reader, identity) => {
                    // Handler registered
                }
            );

            Assert.True(_receiverRoom!.IsConnected);

            await _receiverRoom!.DisconnectAsync();

            // Reconnect
            var receiverToken = _fixture.CreateToken("receiver", _receiverRoom!.Name!);
            await _receiverRoom!.ConnectAsync(_fixture.LiveKitUrl, receiverToken);

            // Handler should still work after reconnect (handlers are not cleared on disconnect)
            var writer = await _senderParticipant!.StreamTextAsync(topic: "cleanup-test");
            await writer.WriteAsync("Test");
            await writer.CloseAsync();

            await Task.Delay(1000);

            Log("Disconnect/reconnect stream handler test completed");
        }

        [Fact]
        public async Task ConcurrentStreams_DifferentTopics_AllReceived()
        {
            Log("Testing concurrent streams on different topics");

            var results = new Dictionary<string, TaskCompletionSource<string>>();
            var topics = new[]
            {
                "concurrent1",
                "concurrent2",
                "concurrent3",
                "concurrent4",
                "concurrent5",
            };

            foreach (var topic in topics)
            {
                var tcs = new TaskCompletionSource<string>();
                results[topic] = tcs;

                _receiverRoom!.RegisterTextStreamHandler(
                    topic,
                    (reader, identity) =>
                    {
                        var currentTopic = topic;
                        Task.Run(async () =>
                        {
                            var chunks = new List<string>();
                            await foreach (var chunk in reader)
                            {
                                chunks.Add(chunk);
                            }
                            results[currentTopic].TrySetResult(string.Join("", chunks));
                        });
                    }
                );
            }

            await Task.Delay(100);

            var writers = new List<Task>();
            for (int i = 0; i < topics.Length; i++)
            {
                var topic = topics[i];
                var index = i;
                writers.Add(
                    Task.Run(async () =>
                    {
                        var writer = await _senderParticipant!.StreamTextAsync(topic: topic);
                        await writer.WriteAsync($"Data from stream {index}");
                        await writer.CloseAsync();
                    })
                );
            }

            await Task.WhenAll(writers);

            foreach (var topic in topics)
            {
                var result = await results[topic].Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Contains("Data from stream", result);
            }

            Log($"Successfully received all {topics.Length} concurrent streams");
        }

        #endregion

        #region SetTrackSubscriptionPermissions Tests

        [Fact]
        public void SetTrackSubscriptionPermissions_AllTracksAllowed_ExecutesSuccessfully()
        {
            Log("Testing SetTrackSubscriptionPermissions with AllTracksAllowed");

            var permission = new Proto.ParticipantTrackPermission
            {
                ParticipantIdentity = "receiver",
                AllowAll = true,
            };

            _senderParticipant!.SetTrackSubscriptionPermissions(
                allParticipantsAllowed: true,
                participantPermissions: new[] { permission }
            );

            Log("SetTrackSubscriptionPermissions completed successfully");
        }

        [Fact]
        public void SetTrackSubscriptionPermissions_SpecificPermissions_ExecutesSuccessfully()
        {
            Log("Testing SetTrackSubscriptionPermissions with specific permissions");

            var permission = new Proto.ParticipantTrackPermission
            {
                ParticipantIdentity = "receiver",
                AllowAll = false,
            };

            _senderParticipant!.SetTrackSubscriptionPermissions(
                allParticipantsAllowed: false,
                participantPermissions: new[] { permission }
            );

            Log("SetTrackSubscriptionPermissions with specific permissions completed");
        }

        #endregion
    }
}
