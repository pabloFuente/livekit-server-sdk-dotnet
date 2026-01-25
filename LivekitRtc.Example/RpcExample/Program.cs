using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;

var url = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("LIVEKIT_URL");
var token = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("LIVEKIT_TOKEN");

if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
{
    Console.WriteLine("Usage: dotnet run <LIVEKIT_URL> <ACCESS_TOKEN>");
    return 1;
}

Console.WriteLine("=== LiveKit RTC - RPC Communication Example ===\n");
Console.WriteLine("This example demonstrates RPC method registration and invocation.");
Console.WriteLine("Connect multiple instances to test RPC calls between participants.\n");

var room = new Room();
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await room.ConnectAsync(url, token, new RoomOptions { AutoSubscribe = true });
    Console.WriteLine($"✓ Connected to room: {room.Name}");
    Console.WriteLine($"  Local participant: {room.LocalParticipant?.Identity}\n");

    // Register RPC method handlers
    room.LocalParticipant!.RegisterRpcMethod(
        "greet",
        (data) =>
        {
            Console.WriteLine($"[RPC Received] 'greet' from {data.CallerIdentity}");
            var response = $"Hello, {data.CallerIdentity}! I'm {room.LocalParticipant.Identity}";
            Console.WriteLine($"[RPC Response] Sending: {response}");
            return Task.FromResult(response);
        }
    );

    room.LocalParticipant.RegisterRpcMethod(
        "calculate",
        (data) =>
        {
            Console.WriteLine($"[RPC Received] 'calculate' from {data.CallerIdentity}");
            Console.WriteLine($"  Payload: {data.Payload}");

            try
            {
                var request = JsonSerializer.Deserialize<CalculateRequest>(data.Payload);

                if (request == null)
                {
                    Console.WriteLine($"[RPC Error] Invalid payload");
                    throw new RpcError(RpcErrorCode.ApplicationError, "Invalid payload");
                }

                double result = request.Operation switch
                {
                    "add" => request.A + request.B,
                    "subtract" => request.A - request.B,
                    "multiply" => request.A * request.B,
                    "divide" => request.B != 0
                        ? request.A / request.B
                        : throw new RpcError(RpcErrorCode.ApplicationError, "Division by zero"),
                    _ => throw new RpcError(
                        RpcErrorCode.UnsupportedMethod,
                        $"Unknown operation: {request.Operation}"
                    ),
                };

                var response = new CalculateResponse { Result = result };
                var responseJson = JsonSerializer.Serialize(response);
                Console.WriteLine($"[RPC Response] Sending: {responseJson}");
                return Task.FromResult(responseJson);
            }
            catch (RpcError)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RPC Error] {ex.Message}");
                throw new RpcError(RpcErrorCode.ApplicationError, ex.Message);
            }
        }
    );

    Console.WriteLine("✓ Registered RPC methods: 'greet', 'calculate'\n");

    // Wait for participants
    room.ParticipantConnected += (sender, participant) =>
    {
        Console.WriteLine($"[Participant] {participant.Identity} joined");

        // Automatically call RPC methods on new participants
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000); // Give them time to register handlers

            try
            {
                // Call 'greet' method
                Console.WriteLine($"\n[RPC Call] Calling 'greet' on {participant.Identity}...");
                var greetResponse = await room.LocalParticipant!.PerformRpcAsync(
                    destinationIdentity: participant.Identity,
                    method: "greet",
                    payload: "{}",
                    responseTimeout: 5.0
                );
                Console.WriteLine($"[RPC Result] Greet response: {greetResponse}\n");

                // Call 'calculate' method
                var calcRequest = new CalculateRequest
                {
                    Operation = "multiply",
                    A = 7,
                    B = 6,
                };
                var calcPayload = JsonSerializer.Serialize(calcRequest);

                Console.WriteLine($"[RPC Call] Calling 'calculate' on {participant.Identity}...");
                Console.WriteLine($"  Request: 7 * 6");
                var calcResponse = await room.LocalParticipant!.PerformRpcAsync(
                    destinationIdentity: participant.Identity,
                    method: "calculate",
                    payload: calcPayload,
                    responseTimeout: 5.0
                );

                var result = JsonSerializer.Deserialize<CalculateResponse>(calcResponse);
                Console.WriteLine($"[RPC Result] Calculate response: {result?.Result}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RPC Error] {ex.Message}\n");
            }
        });
    };

    room.ParticipantDisconnected += (sender, participant) =>
    {
        Console.WriteLine($"[Participant] {participant.Identity} left");
    };

    Console.WriteLine("Waiting for participants to test RPC...");
    Console.WriteLine("(Press Ctrl+C to exit)\n");

    // Keep running
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nShutting down...");
}
catch (Exception ex)
{
    Console.WriteLine($"\n✗ Error: {ex.Message}");
    return 1;
}
finally
{
    await room.DisconnectAsync();
    room.Dispose();
    Console.WriteLine("✓ Disconnected");
}

return 0;

// Helper classes for JSON serialization
class CalculateRequest
{
    public string Operation { get; set; } = "";
    public double A { get; set; }
    public double B { get; set; }
}

class CalculateResponse
{
    public double Result { get; set; }
}
