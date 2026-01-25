using System.Text;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// End-to-end tests for LiveKit RTC Data Streams (data channels).
/// </summary>
[Collection("RtcTests")]
public class DataStreamTests : IAsyncLifetime
{
    private readonly RtcTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private Room? _room1;
    private Room? _room2;

    public DataStreamTests(RtcTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private void Log(string message)
    {
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_room1 != null)
        {
            await _room1.DisconnectAsync();
            _room1.Dispose();
        }

        if (_room2 != null)
        {
            await _room2.DisconnectAsync();
            _room2.Dispose();
        }
    }

    [Fact]
    public async Task PublishData_ReliableMessage_ReceiverGetsData()
    {
        const string roomName = "test-data-room";
        const string sender = "data-sender";
        const string receiver = "data-receiver";
        const string testMessage = "Hello from data channel!";

        Log("Starting PublishData_ReliableMessage_ReceiverGetsData test");

        // Create tokens
        var senderToken = _fixture.CreateToken(sender, roomName);
        var receiverToken = _fixture.CreateToken(receiver, roomName);
        Log("Tokens created");

        // Connect both participants
        _room1 = new Room();
        _room2 = new Room();

        var dataReceived =
            new TaskCompletionSource<(byte[] data, string topic, Proto.DataPacketKind kind)>();

        // Subscribe to DataReceived event on receiver
        _room2.DataReceived += (sender, e) =>
        {
            Log(
                $"Data received from {e.Participant?.Identity ?? "unknown"}: {Encoding.UTF8.GetString(e.Data)}"
            );
            dataReceived.TrySetResult((e.Data, e.Topic ?? string.Empty, e.Kind));
        };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, senderToken);
        Log($"Sender '{sender}' connected");

        await _room2.ConnectAsync(_fixture.LiveKitUrl, receiverToken);
        Log($"Receiver '{receiver}' connected");

        // Wait for participants to see each other
        await Task.Delay(1000);
        Log("Participants discovery complete");

        // Send data
        var dataBytes = Encoding.UTF8.GetBytes(testMessage);
        await _room1.LocalParticipant!.PublishDataAsync(
            dataBytes,
            new DataPublishOptions
            {
                Reliable = true,
                Topic = null, // No topic
            }
        );
        Log("Data published");

        // Wait for receiver to get the data
        var result = await dataReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Verify received data
        var receivedMessage = Encoding.UTF8.GetString(result.data);
        Assert.Equal(testMessage, receivedMessage);
        Assert.Equal(string.Empty, result.topic);
        // Verify correct kind: Reliable=true should give KindReliable
        Assert.Equal(Proto.DataPacketKind.KindReliable, result.kind);

        Log("Test completed successfully");
    }

    [Fact]
    public async Task PublishData_WithTopic_ReceiverGetsTopicedData()
    {
        const string roomName = "test-data-topic-room";
        const string sender = "data-sender-topic";
        const string receiver = "data-receiver-topic";
        const string testTopic = "chat-messages";
        const string testMessage = "Message with topic";

        Log("Starting PublishData_WithTopic_ReceiverGetsTopicedData test");

        var senderToken = _fixture.CreateToken(sender, roomName);
        var receiverToken = _fixture.CreateToken(receiver, roomName);

        _room1 = new Room();
        _room2 = new Room();

        var dataReceived = new TaskCompletionSource<(byte[] data, string topic)>();

        _room2.DataReceived += (sender, e) =>
        {
            Log($"Data received with topic '{e.Topic}': {Encoding.UTF8.GetString(e.Data)}");
            dataReceived.TrySetResult((e.Data, e.Topic ?? string.Empty));
        };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, senderToken);
        await _room2.ConnectAsync(_fixture.LiveKitUrl, receiverToken);
        await Task.Delay(1000);

        // Send data with topic
        var dataBytes = Encoding.UTF8.GetBytes(testMessage);
        await _room1.LocalParticipant!.PublishDataAsync(
            dataBytes,
            new DataPublishOptions { Reliable = true, Topic = testTopic }
        );
        Log($"Data published with topic '{testTopic}'");

        // Wait for receiver to get the data
        var result = await dataReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Verify received data and topic
        var receivedMessage = Encoding.UTF8.GetString(result.data);
        Assert.Equal(testMessage, receivedMessage);
        Assert.Equal(testTopic, result.topic);

        Log("Topic test completed successfully");
    }

    [Fact]
    public async Task PublishData_LargePayload_SuccessfullyTransmitted()
    {
        const string roomName = "test-data-large-room";
        const string sender = "data-sender-large";
        const string receiver = "data-receiver-large";
        const int payloadSize = 10_000; // 10KB (reduced from 100KB for data channel limits)

        Log(
            $"Starting PublishData_LargePayload_SuccessfullyTransmitted test with {payloadSize} bytes"
        );

        var senderToken = _fixture.CreateToken(sender, roomName);
        var receiverToken = _fixture.CreateToken(receiver, roomName);

        _room1 = new Room();
        _room2 = new Room();

        var dataReceived = new TaskCompletionSource<byte[]>();

        _room2.DataReceived += (sender, e) =>
        {
            Log($"Large data received: {e.Data.Length} bytes");
            dataReceived.TrySetResult(e.Data);
        };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, senderToken);
        await _room2.ConnectAsync(_fixture.LiveKitUrl, receiverToken);
        await Task.Delay(1000);

        // Create large payload with a pattern
        var largeData = new byte[payloadSize];
        for (int i = 0; i < payloadSize; i++)
        {
            largeData[i] = (byte)(i % 256);
        }

        // Send large data
        await _room1.LocalParticipant!.PublishDataAsync(
            largeData,
            new DataPublishOptions { Reliable = true }
        );
        Log("Large data published");

        // Wait for receiver to get the data (increased timeout for large payloads)
        var receivedData = await dataReceived.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Verify size and content
        Assert.Equal(payloadSize, receivedData.Length);

        // Verify pattern in received data
        for (int i = 0; i < Math.Min(1000, payloadSize); i++) // Check first 1000 bytes
        {
            Assert.Equal((byte)(i % 256), receivedData[i]);
        }

        Log("Large payload test completed successfully");
    }

    /// <summary>
    /// Tests that data can be sent to specific participants using DestinationIdentities.
    /// Only the targeted participant should receive the message.
    /// </summary>
    [Fact]
    public async Task PublishData_TargetSpecificParticipant_OnlyTargetReceives()
    {
        const string roomName = "test-data-targeted-room";
        const string sender = "data-sender-targeted";
        const string targetReceiver = "target-receiver";
        const string otherReceiver = "other-receiver";
        const string testMessage = "Targeted message";

        Log("Starting PublishData_TargetSpecificParticipant_OnlyTargetReceives test");

        var senderToken = _fixture.CreateToken(sender, roomName);
        var targetToken = _fixture.CreateToken(targetReceiver, roomName);
        var otherToken = _fixture.CreateToken(otherReceiver, roomName);

        _room1 = new Room(); // sender
        _room2 = new Room(); // target receiver
        var room3 = new Room(); // other receiver

        // Track participant arrivals
        var participantConnected = new TaskCompletionSource<bool>();
        int participantCount = 0;

        _room1.ParticipantConnected += (sender, participant) =>
        {
            Log($"Sender room saw participant connect: {participant.Identity}");
            Interlocked.Increment(ref participantCount);
            if (participantCount >= 2) // We expect 2 remote participants
            {
                participantConnected.TrySetResult(true);
            }
        };

        var targetReceived = new TaskCompletionSource<string>();
        var otherReceived = new TaskCompletionSource<string>();

        _room2.DataReceived += (sender, e) =>
        {
            var message = Encoding.UTF8.GetString(e.Data);
            Log($"Target receiver got: {message}");
            targetReceived.TrySetResult(message);
        };

        room3.DataReceived += (sender, e) =>
        {
            var message = Encoding.UTF8.GetString(e.Data);
            Log($"Other receiver got: {message}");
            otherReceived.TrySetResult(message);
        };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, senderToken);
        Log($"Sender connected to room");

        await _room2.ConnectAsync(_fixture.LiveKitUrl, targetToken);
        Log($"Target receiver connected to room");

        await room3.ConnectAsync(_fixture.LiveKitUrl, otherToken);
        Log($"Other receiver connected to room");

        // Wait for participants to be discovered (with timeout)
        Log($"Waiting for participants to discover each other...");
        var discoveryTask = participantConnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await discoveryTask;
            Log($"Participants discovered successfully");
        }
        catch (TimeoutException)
        {
            Log($"Timeout waiting for participant discovery. Current count: {participantCount}");
        }

        // Give a bit more time for state to settle
        await Task.Delay(500);

        Log($"Room1 remote participants: {_room1.RemoteParticipants.Count}");
        foreach (var p in _room1.RemoteParticipants.Values)
        {
            Log($"  - {p.Identity} (SID: {p.Sid})");
        }

        // Verify we can find the target participant
        var targetParticipant = _room1.RemoteParticipants.Values.FirstOrDefault(p =>
            p.Identity == targetReceiver
        );
        Assert.NotNull(targetParticipant);
        Log($"Target participant found: {targetParticipant.Identity}");

        // Send data targeted to specific participant using their identity
        Assert.NotNull(_room1.LocalParticipant);
        var dataBytes = Encoding.UTF8.GetBytes(testMessage);
        await _room1.LocalParticipant.PublishDataAsync(
            dataBytes,
            new DataPublishOptions
            {
                Reliable = true,
                DestinationIdentities = new[] { targetReceiver }, // Use identity directly!
            }
        );
        Log("Targeted data published");

        // Target should receive
        var targetMessage = await targetReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(testMessage, targetMessage);
        Log("Target receiver correctly received the message");

        // Other receiver should NOT receive (timeout expected)
        var otherTask = otherReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var otherTimedOut = false;
        try
        {
            await otherTask;
            Log(
                "ERROR: Other receiver also received the message - DestinationIdentities not working!"
            );
        }
        catch (TimeoutException)
        {
            otherTimedOut = true;
            Log("Other receiver correctly did not receive the targeted message");
        }

        Assert.True(otherTimedOut, "Other receiver should not have received the targeted message");

        await room3.DisconnectAsync();
        room3.Dispose();

        Log("Targeted data test completed successfully");
    }

    [Fact]
    public async Task PublishData_UnreliableMessage_StillTransmitted()
    {
        const string roomName = "test-data-unreliable-room";
        const string sender = "data-sender-unreliable";
        const string receiver = "data-receiver-unreliable";
        const string testMessage = "Unreliable message";

        Log("Starting PublishData_UnreliableMessage_StillTransmitted test");

        var senderToken = _fixture.CreateToken(sender, roomName);
        var receiverToken = _fixture.CreateToken(receiver, roomName);

        _room1 = new Room();
        _room2 = new Room();

        var dataReceived = new TaskCompletionSource<string>();

        _room2.DataReceived += (sender, e) =>
        {
            var message = Encoding.UTF8.GetString(e.Data);
            Log($"Unreliable data received: {message}");
            dataReceived.TrySetResult(message);
        };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, senderToken);
        await _room2.ConnectAsync(_fixture.LiveKitUrl, receiverToken);
        await Task.Delay(1000);

        // Send unreliable data
        var dataBytes = Encoding.UTF8.GetBytes(testMessage);
        await _room1.LocalParticipant!.PublishDataAsync(
            dataBytes,
            new DataPublishOptions
            {
                Reliable = false, // Unreliable
                Topic = "unreliable-test",
            }
        );
        Log("Unreliable data published");

        // Wait for receiver to get the data
        // Note: In good network conditions, unreliable messages usually arrive
        var receivedMessage = await dataReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(testMessage, receivedMessage);

        Log("Unreliable data test completed successfully");
    }

    [Fact]
    public async Task PublishData_MultipleMessages_AllReceived()
    {
        const string roomName = "test-data-multiple-room";
        const string sender = "data-sender-multiple";
        const string receiver = "data-receiver-multiple";
        const int messageCount = 10;

        Log($"Starting PublishData_MultipleMessages_AllReceived test with {messageCount} messages");

        var senderToken = _fixture.CreateToken(sender, roomName);
        var receiverToken = _fixture.CreateToken(receiver, roomName);

        _room1 = new Room();
        _room2 = new Room();

        var receivedMessages = new List<string>();
        var allMessagesReceived = new TaskCompletionSource<bool>();

        _room2.DataReceived += (sender, e) =>
        {
            var message = Encoding.UTF8.GetString(e.Data);
            lock (receivedMessages)
            {
                receivedMessages.Add(message);
                Log($"Message {receivedMessages.Count}/{messageCount} received: {message}");

                if (receivedMessages.Count == messageCount)
                {
                    allMessagesReceived.TrySetResult(true);
                }
            }
        };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, senderToken);
        await _room2.ConnectAsync(_fixture.LiveKitUrl, receiverToken);
        await Task.Delay(1000);

        // Send multiple messages
        var sendTasks = new List<Task>();
        for (int i = 0; i < messageCount; i++)
        {
            var message = $"Message {i}";
            var dataBytes = Encoding.UTF8.GetBytes(message);
            var task = _room1.LocalParticipant!.PublishDataAsync(
                dataBytes,
                new DataPublishOptions { Reliable = true }
            );
            sendTasks.Add(task);

            // Small delay between messages to avoid overwhelming
            await Task.Delay(50);
        }

        await Task.WhenAll(sendTasks);
        Log($"All {messageCount} messages published");

        // Wait for all messages to be received
        await allMessagesReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Verify all messages received
        Assert.Equal(messageCount, receivedMessages.Count);

        // Verify order and content (messages should arrive in order for reliable transmission)
        for (int i = 0; i < messageCount; i++)
        {
            Assert.Equal($"Message {i}", receivedMessages[i]);
        }

        Log("Multiple messages test completed successfully");
    }

    [Fact]
    public async Task PublishData_EmptyPayload_ReceivedSuccessfully()
    {
        const string roomName = "test-data-empty-room";
        const string sender = "data-sender-empty";
        const string receiver = "data-receiver-empty";

        Log("Starting PublishData_EmptyPayload_ReceivedSuccessfully test");

        var senderToken = _fixture.CreateToken(sender, roomName);
        var receiverToken = _fixture.CreateToken(receiver, roomName);

        _room1 = new Room();
        _room2 = new Room();

        var dataReceived = new TaskCompletionSource<byte[]>();

        _room2.DataReceived += (sender, e) =>
        {
            Log($"Empty data received: {e.Data.Length} bytes");
            dataReceived.TrySetResult(e.Data);
        };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, senderToken);
        await _room2.ConnectAsync(_fixture.LiveKitUrl, receiverToken);
        await Task.Delay(1000);

        // Send empty data
        var emptyData = Array.Empty<byte>();
        await _room1.LocalParticipant!.PublishDataAsync(
            emptyData,
            new DataPublishOptions { Reliable = true, Topic = "empty-test" }
        );
        Log("Empty data published");

        // Wait for receiver to get the data
        var receivedData = await dataReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(receivedData);

        Log("Empty payload test completed successfully");
    }

    [Fact]
    public async Task DataReceived_Event_ProvidesParticipantInfo()
    {
        const string roomName = "test-data-participant-info-room";
        const string sender = "data-sender-info";
        const string receiver = "data-receiver-info";
        const string testMessage = "Test message";

        Log("Starting DataReceived_Event_ProvidesParticipantInfo test");

        var senderToken = _fixture.CreateToken(sender, roomName);
        var receiverToken = _fixture.CreateToken(receiver, roomName);

        _room1 = new Room();
        _room2 = new Room();

        var participantInfo =
            new TaskCompletionSource<(string? identity, Proto.DataPacketKind kind)>();

        _room2.DataReceived += (sender, e) =>
        {
            Log($"Data from participant: Identity={e.Participant?.Identity}, Kind={e.Kind}");
            participantInfo.TrySetResult((e.Participant?.Identity, e.Kind));
        };

        await _room1.ConnectAsync(_fixture.LiveKitUrl, senderToken);
        await _room2.ConnectAsync(_fixture.LiveKitUrl, receiverToken);
        await Task.Delay(1000);

        // Send data
        var dataBytes = Encoding.UTF8.GetBytes(testMessage);
        await _room1.LocalParticipant!.PublishDataAsync(
            dataBytes,
            new DataPublishOptions { Reliable = true }
        );

        // Wait and verify participant info
        var result = await participantInfo.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(sender, result.identity);
        // Verify correct kind: Reliable=true should give KindReliable
        Assert.Equal(Proto.DataPacketKind.KindReliable, result.kind);

        Log("Participant info test completed successfully");
    }
}
