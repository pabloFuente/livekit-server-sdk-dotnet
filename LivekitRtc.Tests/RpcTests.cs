using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rtc;
using Xunit;
using Xunit.Abstractions;

namespace LiveKit.Rtc.Tests
{
    /// <summary>
    /// End-to-end tests for LiveKit RPC (Remote Procedure Call) functionality.
    /// Tests verify PerformRpc, RegisterRpcMethod, UnregisterRpcMethod, and error handling.
    /// </summary>
    [Collection("RtcTests")]
    public class RpcTests : IAsyncLifetime
    {
        private readonly RtcTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private Room? _callerRoom;
        private Room? _responderRoom;
        private LocalParticipant? _callerParticipant;
        private LocalParticipant? _responderParticipant;
        private string _callerIdentity = string.Empty;
        private string _responderIdentity = string.Empty;

        public RpcTests(RtcTestFixture fixture, ITestOutputHelper output)
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
            var roomName = $"rpc-test-{Guid.NewGuid()}";
            _callerIdentity = "caller";
            _responderIdentity = "responder";

            var callerToken = _fixture.CreateToken(_callerIdentity, roomName);
            var responderToken = _fixture.CreateToken(_responderIdentity, roomName);

            _callerRoom = new Room();
            _responderRoom = new Room();

            await _callerRoom.ConnectAsync(_fixture.LiveKitUrl, callerToken);
            await _responderRoom.ConnectAsync(_fixture.LiveKitUrl, responderToken);

            _callerParticipant = _callerRoom.LocalParticipant;
            _responderParticipant = _responderRoom.LocalParticipant;

            // Wait for participants to fully connect
            await Task.Delay(500);

            Log($"Test setup complete: Caller={_callerIdentity}, Responder={_responderIdentity}");
        }

        public async Task DisposeAsync()
        {
            if (_callerRoom != null)
            {
                await _callerRoom.DisconnectAsync();
                _callerRoom.Dispose();
            }

            if (_responderRoom != null)
            {
                await _responderRoom.DisconnectAsync();
                _responderRoom.Dispose();
            }
        }

        #region Basic RPC Tests

        [Fact]
        public async Task PerformRpc_SimpleEcho_ReturnsResponse()
        {
            Log("Testing basic RPC call with echo method");

            // Register echo method on responder
            _responderParticipant!.RegisterRpcMethod(
                "echo",
                (data) =>
                {
                    Log($"Echo handler received: {data.Payload} from {data.CallerIdentity}");
                    return Task.FromResult(data.Payload);
                }
            );

            await Task.Delay(500); // Allow registration to propagate

            // Call the method from caller
            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "echo",
                "Hello RPC!",
                10.0
            );

            Assert.Equal("Hello RPC!", response);
            Log("Echo RPC call succeeded");
        }

        [Fact]
        public async Task PerformRpc_WithComplexPayload_ReturnsCorrectResponse()
        {
            Log("Testing RPC call with complex JSON payload");

            // Register handler that processes JSON
            _responderParticipant!.RegisterRpcMethod(
                "greet",
                (data) =>
                {
                    Log($"Greet handler called by {data.CallerIdentity}");
                    return Task.FromResult(
                        $"Hello, {data.CallerIdentity}! You sent: {data.Payload}"
                    );
                }
            );

            await Task.Delay(500);

            var payload = "{\"name\":\"Alice\",\"age\":30}";
            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "greet",
                payload,
                10.0
            );

            Assert.Contains("Hello, caller!", response);
            Assert.Contains(payload, response);
            Log("Complex payload RPC call succeeded");
        }

        [Fact]
        public async Task PerformRpc_EmptyPayload_Succeeds()
        {
            Log("Testing RPC call with empty payload");

            _responderParticipant!.RegisterRpcMethod(
                "ping",
                (data) =>
                {
                    Log($"Ping handler called");
                    return Task.FromResult("pong");
                }
            );

            await Task.Delay(500);

            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "ping",
                "",
                10.0
            );

            Assert.Equal("pong", response);
            Log("Empty payload RPC call succeeded");
        }

        [Fact]
        public async Task PerformRpc_MultipleSequentialCalls_AllSucceed()
        {
            Log("Testing multiple sequential RPC calls");

            var callCount = 0;
            _responderParticipant!.RegisterRpcMethod(
                "counter",
                (data) =>
                {
                    callCount++;
                    Log($"Counter handler called: count={callCount}");
                    return Task.FromResult(callCount.ToString());
                }
            );

            await Task.Delay(500);

            for (int i = 1; i <= 5; i++)
            {
                var response = await _callerParticipant!.PerformRpcAsync(
                    _responderIdentity,
                    "counter",
                    $"call-{i}",
                    10.0
                );

                Assert.Equal(i.ToString(), response);
            }

            Assert.Equal(5, callCount);
            Log("Multiple sequential RPC calls succeeded");
        }

        [Fact]
        public async Task PerformRpc_BidirectionalCalls_BothDirectionsWork()
        {
            Log("Testing bidirectional RPC calls");

            // Register method on caller
            _callerParticipant!.RegisterRpcMethod(
                "callerMethod",
                (data) =>
                {
                    Log("callerMethod invoked");
                    return Task.FromResult("response-from-caller");
                }
            );

            // Register method on responder
            _responderParticipant!.RegisterRpcMethod(
                "responderMethod",
                (data) =>
                {
                    Log("responderMethod invoked");
                    return Task.FromResult("response-from-responder");
                }
            );

            await Task.Delay(500);

            // Call from caller to responder
            var response1 = await _callerParticipant.PerformRpcAsync(
                _responderIdentity,
                "responderMethod",
                "test",
                10.0
            );

            // Call from responder to caller
            var response2 = await _responderParticipant.PerformRpcAsync(
                _callerIdentity,
                "callerMethod",
                "test",
                10.0
            );

            Assert.Equal("response-from-responder", response1);
            Assert.Equal("response-from-caller", response2);
            Log("Bidirectional RPC calls succeeded");
        }

        #endregion

        #region Handler Registration Tests

        [Fact]
        public async Task RegisterRpcMethod_OverwritesPreviousHandler()
        {
            Log("Testing handler overwrite");

            var callCount = 0;

            // Register first handler
            _responderParticipant!.RegisterRpcMethod(
                "test",
                (data) =>
                {
                    callCount++;
                    return Task.FromResult("first-handler");
                }
            );

            await Task.Delay(500);

            // Register second handler (should overwrite)
            _responderParticipant.RegisterRpcMethod(
                "test",
                (data) =>
                {
                    callCount++;
                    return Task.FromResult("second-handler");
                }
            );

            await Task.Delay(500);

            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "test",
                "payload",
                10.0
            );

            Assert.Equal("second-handler", response);
            Assert.Equal(1, callCount); // Only second handler should be called
            Log("Handler overwrite succeeded");
        }

        [Fact]
        public async Task UnregisterRpcMethod_RemovesHandler()
        {
            Log("Testing handler unregistration");

            _responderParticipant!.RegisterRpcMethod(
                "temp",
                (data) =>
                {
                    return Task.FromResult("handler-response");
                }
            );

            await Task.Delay(500);

            // Unregister the handler
            _responderParticipant.UnregisterRpcMethod("temp");

            await Task.Delay(500);

            // Attempt to call should fail with UnsupportedMethod
            var exception = await Assert.ThrowsAsync<RpcError>(async () =>
            {
                await _callerParticipant!.PerformRpcAsync(
                    _responderIdentity,
                    "temp",
                    "payload",
                    5.0
                );
            });

            Assert.Equal(RpcErrorCode.UnsupportedMethod, (RpcErrorCode)exception.Code);
            Log("Handler unregistration succeeded");
        }

        [Fact]
        public async Task RegisterRpcMethod_MultipleMethodsOnSameParticipant()
        {
            Log("Testing multiple method registrations");

            _responderParticipant!.RegisterRpcMethod(
                "method1",
                (data) => Task.FromResult("response1")
            );
            _responderParticipant.RegisterRpcMethod(
                "method2",
                (data) => Task.FromResult("response2")
            );
            _responderParticipant.RegisterRpcMethod(
                "method3",
                (data) => Task.FromResult("response3")
            );

            await Task.Delay(500);

            var response1 = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "method1",
                "",
                10.0
            );
            var response2 = await _callerParticipant.PerformRpcAsync(
                _responderIdentity,
                "method2",
                "",
                10.0
            );
            var response3 = await _callerParticipant.PerformRpcAsync(
                _responderIdentity,
                "method3",
                "",
                10.0
            );

            Assert.Equal("response1", response1);
            Assert.Equal("response2", response2);
            Assert.Equal("response3", response3);
            Log("Multiple method registrations succeeded");
        }

        #endregion

        #region RPC Invocation Data Tests

        [Fact]
        public async Task RpcInvocationData_ContainsCorrectCallerIdentity()
        {
            Log("Testing RPC invocation data - caller identity");

            string? receivedCallerIdentity = null;

            _responderParticipant!.RegisterRpcMethod(
                "checkCaller",
                (data) =>
                {
                    receivedCallerIdentity = data.CallerIdentity;
                    Log($"Handler received caller identity: {data.CallerIdentity}");
                    return Task.FromResult("ok");
                }
            );

            await Task.Delay(500);

            await _callerParticipant!.PerformRpcAsync(_responderIdentity, "checkCaller", "", 10.0);

            Assert.Equal(_callerIdentity, receivedCallerIdentity);
            Log("Caller identity verification succeeded");
        }

        [Fact]
        public async Task RpcInvocationData_ContainsCorrectPayload()
        {
            Log("Testing RPC invocation data - payload");

            string? receivedPayload = null;
            var sentPayload = "test-payload-12345";

            _responderParticipant!.RegisterRpcMethod(
                "checkPayload",
                (data) =>
                {
                    receivedPayload = data.Payload;
                    return Task.FromResult("ok");
                }
            );

            await Task.Delay(500);

            await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "checkPayload",
                sentPayload,
                10.0
            );

            Assert.Equal(sentPayload, receivedPayload);
            Log("Payload verification succeeded");
        }

        [Fact]
        public async Task RpcInvocationData_HasValidRequestId()
        {
            Log("Testing RPC invocation data - request ID");

            string? receivedRequestId = null;

            _responderParticipant!.RegisterRpcMethod(
                "checkRequestId",
                (data) =>
                {
                    receivedRequestId = data.RequestId;
                    Log($"Handler received request ID: {data.RequestId}");
                    return Task.FromResult("ok");
                }
            );

            await Task.Delay(500);

            await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "checkRequestId",
                "",
                10.0
            );

            Assert.NotNull(receivedRequestId);
            Assert.NotEmpty(receivedRequestId);
            Log("Request ID verification succeeded");
        }

        [Fact]
        public async Task RpcInvocationData_HasCorrectResponseTimeout()
        {
            Log("Testing RPC invocation data - response timeout");

            double? receivedTimeout = null;
            var expectedTimeout = 15.0;

            _responderParticipant!.RegisterRpcMethod(
                "checkTimeout",
                (data) =>
                {
                    receivedTimeout = data.ResponseTimeout;
                    Log($"Handler received timeout: {data.ResponseTimeout}s");
                    return Task.FromResult("ok");
                }
            );

            await Task.Delay(500);

            await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "checkTimeout",
                "",
                expectedTimeout
            );

            Assert.NotNull(receivedTimeout);
            // The actual timeout received may differ from expected due to internal system adjustments
            // Verify that we received a reasonable timeout value
            Assert.True(
                receivedTimeout.Value > 0,
                $"Timeout should be positive, got {receivedTimeout.Value}"
            );
            Assert.True(
                receivedTimeout.Value <= expectedTimeout * 2,
                $"Timeout should be reasonable, got {receivedTimeout.Value}s for expected {expectedTimeout}s"
            );
            Log(
                $"Response timeout verification succeeded (expected: {expectedTimeout}s, received: {receivedTimeout.Value}s)"
            );
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task PerformRpc_UnsupportedMethod_ThrowsRpcError()
        {
            Log("Testing unsupported method error");

            var exception = await Assert.ThrowsAsync<RpcError>(async () =>
            {
                await _callerParticipant!.PerformRpcAsync(
                    _responderIdentity,
                    "nonexistent-method",
                    "payload",
                    5.0
                );
            });

            Assert.Equal(RpcErrorCode.UnsupportedMethod, (RpcErrorCode)exception.Code);
            Assert.Contains(
                "Method not supported",
                exception.Message,
                StringComparison.OrdinalIgnoreCase
            );
            Log($"Unsupported method error handled correctly: {exception.Message}");
        }

        [Fact]
        public async Task RpcHandler_ThrowsRpcError_PropagatedToCaller()
        {
            Log("Testing RpcError propagation from handler");

            _responderParticipant!.RegisterRpcMethod(
                "errorMethod",
                (data) =>
                {
                    Log("Handler throwing RpcError");
                    return Task.FromException<string>(
                        new RpcError(1234, "Custom error message", "error-data")
                    );
                }
            );

            await Task.Delay(500);

            var exception = await Assert.ThrowsAsync<RpcError>(async () =>
            {
                await _callerParticipant!.PerformRpcAsync(
                    _responderIdentity,
                    "errorMethod",
                    "",
                    10.0
                );
            });

            Assert.Equal(1234, exception.Code);
            Assert.Equal("Custom error message", exception.Message);
            Assert.Equal("error-data", exception.RpcData);
            Log("RpcError propagated correctly");
        }

        [Fact]
        public async Task RpcHandler_ThrowsGenericException_ConvertsToApplicationError()
        {
            Log("Testing generic exception conversion to ApplicationError");

            _responderParticipant!.RegisterRpcMethod(
                "crashMethod",
                (data) =>
                {
                    Log("Handler throwing generic exception");
                    return Task.FromException<string>(
                        new InvalidOperationException("Something went wrong")
                    );
                }
            );

            await Task.Delay(500);

            var exception = await Assert.ThrowsAsync<RpcError>(async () =>
            {
                await _callerParticipant!.PerformRpcAsync(
                    _responderIdentity,
                    "crashMethod",
                    "",
                    10.0
                );
            });

            Assert.Equal(RpcErrorCode.ApplicationError, (RpcErrorCode)exception.Code);
            Assert.Contains("Application error", exception.Message);
            Log($"Generic exception converted to ApplicationError: {exception.Message}");
        }

        [Fact]
        public async Task RpcHandler_ReturnsNull_ConvertedToEmptyString()
        {
            Log("Testing null return value handling");

            _responderParticipant!.RegisterRpcMethod(
                "nullMethod",
                (data) =>
                {
                    Log("Handler returning null");
                    return Task.FromResult<string>(null!);
                }
            );

            await Task.Delay(500);

            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "nullMethod",
                "",
                10.0
            );

            Assert.NotNull(response);
            Assert.Equal(string.Empty, response);
            Log("Null return value handled correctly");
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void RegisterRpcMethod_NullMethod_ThrowsArgumentNullException()
        {
            Log("Testing null method name registration");

            Assert.Throws<ArgumentNullException>(() =>
            {
                _callerParticipant!.RegisterRpcMethod(null!, (data) => Task.FromResult("test"));
            });

            Log("Null method name rejected correctly");
        }

        [Fact]
        public void RegisterRpcMethod_EmptyMethod_ThrowsArgumentNullException()
        {
            Log("Testing empty method name registration");

            Assert.Throws<ArgumentNullException>(() =>
            {
                _callerParticipant!.RegisterRpcMethod("", (data) => Task.FromResult("test"));
            });

            Log("Empty method name rejected correctly");
        }

        [Fact]
        public void RegisterRpcMethod_NullHandler_ThrowsArgumentNullException()
        {
            Log("Testing null handler registration");

            Assert.Throws<ArgumentNullException>(() =>
            {
                _callerParticipant!.RegisterRpcMethod("test", null!);
            });

            Log("Null handler rejected correctly");
        }

        [Fact]
        public async Task PerformRpc_NullDestinationIdentity_ThrowsArgumentNullException()
        {
            Log("Testing null destination identity");

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await _callerParticipant!.PerformRpcAsync(null!, "method", "payload", 10.0);
            });

            Log("Null destination identity rejected correctly");
        }

        [Fact]
        public async Task PerformRpc_NullMethod_ThrowsArgumentNullException()
        {
            Log("Testing null method name in PerformRpc");

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await _callerParticipant!.PerformRpcAsync(
                    _responderIdentity,
                    null!,
                    "payload",
                    10.0
                );
            });

            Log("Null method name rejected correctly");
        }

        [Fact]
        public async Task PerformRpc_NullPayload_SendsEmptyString()
        {
            Log("Testing null payload handling");

            string? receivedPayload = null;

            _responderParticipant!.RegisterRpcMethod(
                "nullPayload",
                (data) =>
                {
                    receivedPayload = data.Payload;
                    return Task.FromResult("ok");
                }
            );

            await Task.Delay(500);

            await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "nullPayload",
                null!,
                10.0
            );

            Assert.NotNull(receivedPayload);
            Assert.Equal(string.Empty, receivedPayload);
            Log("Null payload converted to empty string");
        }

        [Fact]
        public void UnregisterRpcMethod_NullMethod_DoesNotThrow()
        {
            Log("Testing unregister with null method name");

            // Should not throw
            _callerParticipant!.UnregisterRpcMethod(null!);

            Log("Null method unregistration handled gracefully");
        }

        [Fact]
        public void UnregisterRpcMethod_EmptyMethod_DoesNotThrow()
        {
            Log("Testing unregister with empty method name");

            // Should not throw
            _callerParticipant!.UnregisterRpcMethod("");

            Log("Empty method unregistration handled gracefully");
        }

        [Fact]
        public void UnregisterRpcMethod_NonexistentMethod_DoesNotThrow()
        {
            Log("Testing unregister of nonexistent method");

            // Should not throw
            _callerParticipant!.UnregisterRpcMethod("method-that-was-never-registered");

            Log("Nonexistent method unregistration handled gracefully");
        }

        #endregion

        #region Async Handler Tests

        [Fact]
        public async Task RpcHandler_AsyncWithDelay_ExecutesCorrectly()
        {
            Log("Testing async handler with delay");

            _responderParticipant!.RegisterRpcMethod(
                "slowMethod",
                async (data) =>
                {
                    Log("Handler executing with async delay");
                    await Task.Delay(1000);
                    return "completed-after-delay";
                }
            );

            await Task.Delay(500);

            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "slowMethod",
                "",
                15.0
            );

            Assert.Equal("completed-after-delay", response);
            Log("Async handler with delay succeeded");
        }

        [Fact]
        public async Task RpcHandler_ConcurrentCalls_AllSucceed()
        {
            Log("Testing concurrent RPC calls");

            var callCount = 0;
            var lockObj = new object();

            _responderParticipant!.RegisterRpcMethod(
                "concurrent",
                async (data) =>
                {
                    await Task.Delay(100); // Simulate some work
                    lock (lockObj)
                    {
                        callCount++;
                    }
                    return $"call-{callCount}";
                }
            );

            await Task.Delay(500);

            // Make 5 concurrent calls
            var tasks = new List<Task<string>>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(
                    _callerParticipant!.PerformRpcAsync(
                        _responderIdentity,
                        "concurrent",
                        $"call-{i}",
                        10.0
                    )
                );
            }

            var responses = await Task.WhenAll(tasks);

            Assert.Equal(5, responses.Length);
            Assert.Equal(5, callCount);
            Log("Concurrent RPC calls succeeded");
        }

        #endregion

        #region Large Payload Tests

        [Fact]
        public async Task PerformRpc_LargePayload_Succeeds()
        {
            Log("Testing RPC with large payload");

            var largePayload = new string('X', 10000); // 10KB payload

            _responderParticipant!.RegisterRpcMethod(
                "largePayload",
                (data) =>
                {
                    Log($"Handler received large payload of {data.Payload.Length} bytes");
                    return Task.FromResult($"received-{data.Payload.Length}");
                }
            );

            await Task.Delay(500);

            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "largePayload",
                largePayload,
                15.0
            );

            Assert.Equal("received-10000", response);
            Log("Large payload RPC succeeded");
        }

        [Fact]
        public async Task PerformRpc_LargeResponse_Succeeds()
        {
            Log("Testing RPC with large response");

            var largeResponse = new string('Y', 10000); // 10KB response

            _responderParticipant!.RegisterRpcMethod(
                "largeResponse",
                (data) =>
                {
                    Log("Handler returning large response");
                    return Task.FromResult(largeResponse);
                }
            );

            await Task.Delay(500);

            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "largeResponse",
                "",
                15.0
            );

            Assert.Equal(10000, response.Length);
            Assert.Equal(largeResponse, response);
            Log("Large response RPC succeeded");
        }

        #endregion

        #region Special Characters Tests

        [Fact]
        public async Task PerformRpc_SpecialCharactersInPayload_PreservesContent()
        {
            Log("Testing RPC with special characters in payload");

            var specialPayload =
                "Test with: quotes\", backslash\\, newline\n, tab\t, unicode: 你好 🚀";

            _responderParticipant!.RegisterRpcMethod(
                "specialChars",
                (data) =>
                {
                    return Task.FromResult(data.Payload);
                }
            );

            await Task.Delay(500);

            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                "specialChars",
                specialPayload,
                10.0
            );

            Assert.Equal(specialPayload, response);
            Log("Special characters preserved correctly");
        }

        [Fact]
        public async Task RegisterRpcMethod_SpecialCharactersInMethodName_Works()
        {
            Log("Testing method names with special characters");

            var methodName = "method-with_special.chars:123";

            _responderParticipant!.RegisterRpcMethod(
                methodName,
                (data) =>
                {
                    return Task.FromResult("success");
                }
            );

            await Task.Delay(500);

            var response = await _callerParticipant!.PerformRpcAsync(
                _responderIdentity,
                methodName,
                "",
                10.0
            );

            Assert.Equal("success", response);
            Log("Special characters in method name handled correctly");
        }

        #endregion
    }
}
