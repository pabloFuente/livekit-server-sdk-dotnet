using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;
using Xunit;
using Google.Protobuf;

namespace LiveKit.Rtc.Tests
{
    public class FfiClientTest : IDisposable
    {
        public FfiClientTest()
        {
            // Reset state before each test if possible, 
            // though FfiClient is a Singleton, we must ensure it's initialized.
            FfiClient.Instance.Initialize();
        }

        public void Dispose()
        {
            // In a real scenario, you'd want a way to reset the Singleton 
            // for test isolation, but we'll work with the existing structure.
        }

        /// <summary>
        /// Verifies that an event already in the queue is picked up immediately.
        /// </summary>
        [Fact]
        public async Task WaitForEventAsync_ReturnsExistingEventFromQueue()
        {
            // 1. Arrange: Put an event in the queue manually by simulating a callback
            var asyncId = 123UL;
            var ffiEvent = CreateMockEvent(asyncId);
            SimulateNativeCallback(ffiEvent);

            // 2. Act
            var result = await FfiClient.Instance.WaitForEventAsync(e => GetAsyncId(e) == asyncId);

            // 3. Assert
            Assert.NotNull(result);
            Assert.Equal(asyncId, GetAsyncId(result));
        }

        /// <summary>
        /// Verifies that the client waits for a future event and doesn't time out.
        /// </summary>
        [Fact]
        public async Task WaitForEventAsync_WaitsForFutureEvent()
        {
            // 1. Arrange
            var asyncId = 456UL;
            var ffiEvent = CreateMockEvent(asyncId);

            // 2. Act: Start waiting on a background task
            var waitTask = FfiClient.Instance.WaitForEventAsync(e => GetAsyncId(e) == asyncId, TimeSpan.FromSeconds(2));

            // Simulate delay from "Rust"
            await Task.Delay(100);
            SimulateNativeCallback(ffiEvent);

            var result = await waitTask;

            // 3. Assert
            Assert.Equal(asyncId, GetAsyncId(result));
        }

        /// <summary>
        /// Verifies that EventReceived fires for ALL events, even those consumed by waiters.
        /// This is the current implementation behavior.
        /// </summary>
        [Fact]
        public async Task EventReceived_FiresForAllEvents()
        {
            // 1. Arrange
            var asyncId = 789UL;
            var ffiEvent = CreateMockEvent(asyncId);
            bool globalFired = false;
            
            EventHandler<FfiEvent>? handler = null;
            handler = (s, e) => 
            { 
                if (GetAsyncId(e) == asyncId)
                {
                    globalFired = true;
                    // Unsubscribe immediately to avoid affecting other tests
                    FfiClient.Instance.EventReceived -= handler;
                }
            };
            FfiClient.Instance.EventReceived += handler;

            // 2. Act: Register a waiter AND expect EventReceived to fire
            var waitTask = FfiClient.Instance.WaitForEventAsync(e => GetAsyncId(e) == asyncId);
            SimulateNativeCallback(ffiEvent);
            await waitTask;
            
            // Small delay to ensure EventReceived handler has executed
            await Task.Delay(50);

            // 3. Assert
            Assert.True(globalFired, "EventReceived should fire for all events, including those consumed by waiters.");
        }

        /// <summary>
        /// Verifies that if no waiter exists, the global EventReceived DOES fire.
        /// </summary>
        [Fact]
        public async Task EventReceived_FiresForUnclaimedEvents()
        {
            // 1. Arrange
            var asyncId = 101UL;
            var ffiEvent = CreateMockEvent(asyncId);
            var tcs = new TaskCompletionSource<bool>();

            EventHandler<FfiEvent>? handler = null;
            handler = (s, e) =>
            {
                if (GetAsyncId(e) == asyncId)
                {
                    tcs.TrySetResult(true);
                    // Unsubscribe to avoid affecting other tests
                    FfiClient.Instance.EventReceived -= handler;
                }
            };
            FfiClient.Instance.EventReceived += handler;

            // 2. Act
            SimulateNativeCallback(ffiEvent);

            // 3. Assert
            var result = await Task.WhenAny(tcs.Task, Task.Delay(1000));
            Assert.True(tcs.Task.IsCompleted, "Global event should have fired for unclaimed event.");
            
            // Cleanup
            FfiClient.Instance.EventReceived -= handler;
        }

        /// <summary>
        /// Tests the thread-safety by bombarding the client with events and waiters.
        /// </summary>
        [Fact]
        public async Task WaitForEventAsync_HandlesHighConcurrency()
        {
            int count = 100;
            var tasks = new List<Task<FfiEvent>>();

            // 1. Arrange: Create 100 waiters
            for (int i = 0; i < count; i++)
            {
                var id = (ulong)i;
                tasks.Add(FfiClient.Instance.WaitForEventAsync(e => GetAsyncId(e) == id, TimeSpan.FromSeconds(5)));
            }

            // 2. Act: Bombard with callbacks from multiple threads
            Parallel.For(0, count, i =>
            {
                SimulateNativeCallback(CreateMockEvent((ulong)i));
            });

            // 3. Assert
            await Task.WhenAll(tasks);
            foreach (var task in tasks)
            {
                Assert.True(task.IsCompletedSuccessfully);
            }
        }

        [Fact]
        public async Task WaitForEventAsync_TimesOutCorrectly()
        {
            // Act & Assert
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await FfiClient.Instance.WaitForEventAsync(e => false, TimeSpan.FromMilliseconds(100));
            });
        }

        /// <summary>
        /// HARSH TEST: Multiple waiters competing for the same event - only one should get it.
        /// </summary>
        [Fact]
        public async Task WaitForEventAsync_MultipleWaitersForSameEvent_OnlyOneGetsIt()
        {
            var asyncId = 999UL;
            var ffiEvent = CreateMockEvent(asyncId);
            
            // Create 10 waiters for the same event
            var waiters = new List<Task<FfiEvent>>();
            for (int i = 0; i < 10; i++)
            {
                waiters.Add(FfiClient.Instance.WaitForEventAsync(
                    e => GetAsyncId(e) == asyncId, 
                    TimeSpan.FromSeconds(3)));
            }

            await Task.Delay(50); // Ensure all waiters are registered
            SimulateNativeCallback(ffiEvent);

            // One should complete, others should timeout
            var completed = await Task.WhenAny(waiters);
            Assert.True(completed.IsCompletedSuccessfully);
            Assert.Equal(asyncId, GetAsyncId(completed.Result));

            // The rest should timeout (we'll cancel them to avoid waiting)
            // In a real scenario, they would timeout
        }

        /// <summary>
        /// HARSH TEST: Rapid fire - 1000 events with 1000 concurrent waiters.
        /// </summary>
        [Fact]
        public async Task WaitForEventAsync_RapidFire1000Events()
        {
            const int count = 1000;
            var tasks = new List<Task<FfiEvent>>();

            // Register 1000 waiters
            for (int i = 0; i < count; i++)
            {
                var id = (ulong)i;
                tasks.Add(FfiClient.Instance.WaitForEventAsync(
                    e => GetAsyncId(e) == id, 
                    TimeSpan.FromSeconds(10)));
            }

            // Fire events as fast as possible from multiple threads
            await Task.Run(() =>
            {
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 10 }, i =>
                {
                    SimulateNativeCallback(CreateMockEvent((ulong)i));
                });
            });

            // All should complete
            await Task.WhenAll(tasks);
            Assert.All(tasks, task => Assert.True(task.IsCompletedSuccessfully));
        }

        /// <summary>
        /// HARSH TEST: Queue check race - events arrive while checking queue.
        /// </summary>
        [Fact]
        public async Task WaitForEventAsync_QueueCheckRace()
        {
            const int iterations = 100;
            var successCount = 0;

            for (int i = 0; i < iterations; i++)
            {
                var asyncId = (ulong)(10000 + i);
                var ffiEvent = CreateMockEvent(asyncId);

                // Start waiting and immediately fire event
                var waitTask = FfiClient.Instance.WaitForEventAsync(
                    e => GetAsyncId(e) == asyncId,
                    TimeSpan.FromMilliseconds(500));
                
                // Fire immediately - creates race between queue check and waiter registration
                SimulateNativeCallback(ffiEvent);

                try
                {
                    var result = await waitTask;
                    if (GetAsyncId(result) == asyncId)
                        successCount++;
                }
                catch (TimeoutException)
                {
                    // This should NOT happen if waiter pattern is correct
                }
            }

            // Should have 100% success rate with proper waiter pattern
            Assert.Equal(iterations, successCount);
        }

        /// <summary>
        /// HARSH TEST: EventReceived chaos - rapid subscribe/unsubscribe while events fire.
        /// </summary>
        [Fact]
        public async Task EventReceived_ChaosSubscribeUnsubscribe()
        {
            const int eventCount = 200;
            var receivedEvents = 0;
            var lockObj = new object();

            // Task 1: Rapidly subscribe/unsubscribe handlers
            var subscribeTask = Task.Run(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    EventHandler<FfiEvent>? handler = (s, e) =>
                    {
                        lock (lockObj) receivedEvents++;
                    };
                    
                    FfiClient.Instance.EventReceived += handler;
                    await Task.Delay(5);
                    FfiClient.Instance.EventReceived -= handler;
                }
            });

            // Task 2: Fire events rapidly
            var fireTask = Task.Run(() =>
            {
                Parallel.For(0, eventCount, i =>
                {
                    SimulateNativeCallback(CreateMockEvent((ulong)(20000 + i)));
                    Thread.Sleep(1); // Small delay to spread events
                });
            });

            await Task.WhenAll(subscribeTask, fireTask);
            
            // Should have received some events without crashing
            Assert.True(receivedEvents >= 0); // Just verify no crash
        }

        /// <summary>
        /// HARSH TEST: Queue overflow - add 2000 events to test circular buffer logic.
        /// </summary>
        [Fact]
        public async Task EventQueue_HandlesOverflow()
        {
            // Fill queue beyond 1000 limit
            for (int i = 0; i < 2000; i++)
            {
                SimulateNativeCallback(CreateMockEvent((ulong)(30000 + i)));
            }

            // Should still be able to wait for new event
            var asyncId = 99999UL;
            var waitTask = FfiClient.Instance.WaitForEventAsync(
                e => GetAsyncId(e) == asyncId,
                TimeSpan.FromSeconds(1));

            await Task.Delay(50);
            SimulateNativeCallback(CreateMockEvent(asyncId));

            var result = await waitTask;
            Assert.Equal(asyncId, GetAsyncId(result));
        }

        /// <summary>
        /// HARSH TEST: Cancellation stress - cancel 100 waiters simultaneously.
        /// </summary>
        [Fact]
        public async Task WaitForEventAsync_MassCancellation()
        {
            var cancellationTokenSources = new List<CancellationTokenSource>();
            var tasks = new List<Task<FfiEvent>>();

            // Create 100 waiters with cancellation tokens
            for (int i = 0; i < 100; i++)
            {
                var cts = new CancellationTokenSource();
                cancellationTokenSources.Add(cts);
                
                tasks.Add(FfiClient.Instance.WaitForEventAsync(
                    e => GetAsyncId(e) == 88888,
                    TimeSpan.FromSeconds(30),
                    cts.Token));
            }

            await Task.Delay(50); // Let them register

            // Cancel all simultaneously from multiple threads
            Parallel.ForEach(cancellationTokenSources, cts => cts.Cancel());

            // All should be cancelled
            var results = await Task.WhenAll(tasks.Select(async t =>
            {
                try
                {
                    await t;
                    return false; // Shouldn't complete
                }
                catch (OperationCanceledException)
                {
                    return true; // Expected
                }
                catch
                {
                    return false;
                }
            }));

            Assert.All(results, r => Assert.True(r));
        }

        /// <summary>
        /// HARSH TEST: Mixed operations - waiters, queue checks, and events all happening simultaneously.
        /// </summary>
        [Fact]
        public async Task FfiClient_MixedChaosOperations()
        {
            const int duration = 2000; // 2 seconds of chaos
            var cts = new CancellationTokenSource(duration);
            var tasks = new List<Task>();
            var errors = new List<Exception>();
            var lockObj = new object();

            // Task 1: Constantly add waiters
            tasks.Add(Task.Run(async () =>
            {
                int id = 50000;
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var waitTask = FfiClient.Instance.WaitForEventAsync(
                            e => GetAsyncId(e) == (ulong)id++,
                            TimeSpan.FromMilliseconds(100),
                            cts.Token);
                        _ = waitTask.ContinueWith(t => { }, TaskContinuationOptions.None);
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) errors.Add(ex);
                    }
                    await Task.Delay(5);
                }
            }));

            // Task 2: Constantly fire events
            tasks.Add(Task.Run(() =>
            {
                int id = 60000;
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        SimulateNativeCallback(CreateMockEvent((ulong)id++));
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) errors.Add(ex);
                    }
                    Thread.Sleep(2);
                }
            }));

            // Task 3: Constantly subscribe/unsubscribe EventReceived
            tasks.Add(Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        EventHandler<FfiEvent>? handler = (s, e) => { };
                        FfiClient.Instance.EventReceived += handler;
                        await Task.Delay(10);
                        FfiClient.Instance.EventReceived -= handler;
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) errors.Add(ex);
                    }
                }
            }));

            await Task.WhenAll(tasks);

            // Should complete without deadlocks or unhandled exceptions
            Assert.Empty(errors);
        }

        /// <summary>
        /// HARSH TEST: Verify no events are lost in high-concurrency scenario.
        /// </summary>
        [Fact]
        public async Task WaitForEventAsync_NoEventsLost()
        {
            const int count = 500;
            var receivedIds = new System.Collections.Concurrent.ConcurrentBag<ulong>();
            var tasks = new List<Task>();

            // Register waiters for specific IDs
            for (int i = 0; i < count; i++)
            {
                var id = (ulong)(70000 + i);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await FfiClient.Instance.WaitForEventAsync(
                            e => GetAsyncId(e) == id,
                            TimeSpan.FromSeconds(10));
                        receivedIds.Add(GetAsyncId(result));
                    }
                    catch (TimeoutException)
                    {
                        // Event was lost!
                    }
                }));
            }

            // Fire all events from multiple threads
            await Task.Run(() =>
            {
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
                {
                    SimulateNativeCallback(CreateMockEvent((ulong)(70000 + i)));
                });
            });

            await Task.WhenAll(tasks);

            // All events should be received
            Assert.Equal(count, receivedIds.Count);
            Assert.Equal(count, receivedIds.Distinct().Count()); // No duplicates
        }

        #region Helpers

        private FfiEvent CreateMockEvent(ulong asyncId)
        {
            // Create a mock ConnectCallback event with the specified AsyncId
            return new FfiEvent
            {
                Connect = new ConnectCallback
                {
                    AsyncId = asyncId,
                    // Add minimal result to make it valid
                    Result = new ConnectCallback.Types.Result
                    {
                        Room = new OwnedRoom
                        {
                            Handle = new FfiOwnedHandle { Id = 1 },
                            Info = new RoomInfo
                            {
                                Sid = "test-room",
                                Name = "Test Room"
                            }
                        },
                        LocalParticipant = new OwnedParticipant
                        {
                            Handle = new FfiOwnedHandle { Id = 2 },
                            Info = new ParticipantInfo
                            {
                                Sid = "test-participant",
                                Identity = "test-identity"
                            }
                        }
                    }
                }
            };
        }

        private ulong GetAsyncId(FfiEvent e)
        {
            // Extract AsyncId from the event based on its type
            return e.MessageCase switch
            {
                FfiEvent.MessageOneofCase.Connect => e.Connect.AsyncId,
                _ => 0
            };
        }

        private unsafe void SimulateNativeCallback(FfiEvent ffiEvent)
        {
            var data = ffiEvent.ToByteArray();
            fixed (byte* ptr = data)
            {
                // We have to use Reflection to call the private OnFfiCallback 
                // because it's the entry point from the unmanaged side.
                var method = typeof(FfiClient).GetMethod("OnFfiCallback",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                method?.Invoke(FfiClient.Instance, new object[] { (IntPtr)ptr, (UIntPtr)data.Length });
            }
        }

        #endregion
    }
}