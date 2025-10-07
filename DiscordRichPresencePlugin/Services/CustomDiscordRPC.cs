using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Enums;
using Playnite.SDK;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Playnite.SDK.Data;


namespace DiscordRichPresencePlugin.Services
{
    public class CustomDiscordRPC : IDisposable
    {
        private readonly string applicationId;
        private readonly ILogger logger;
        private NamedPipeClientStream pipe;
        private volatile bool isConnected = false;
        private int nonce = 0;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private Timer _heartbeatTimer;
        private DateTime _lastPongUtc = DateTime.MinValue;
        private volatile bool _disposed;
        private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _pongTimeout = TimeSpan.FromSeconds(15);

        public CustomDiscordRPC(string appId, ILogger logger)
        {
            this.applicationId = appId;
            this.logger = logger;
        }

        public void Initialize()
        {
            Task.Run(async () =>
            {
                try
                {
                    logger.Debug("Starting Discord RPC initialization");
                    await ConnectAsync();
                    StartHeartbeat();
                }
                catch (Exception ex)
                {
                    logger.Error($"ERROR initializing Discord RPC: {ex}");
                }
            });
        }

        private async Task ConnectAsync()
        {
            logger.Debug("Attempting to connect to Discord...");

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    logger.Debug($"Trying Discord pipe {i}");
                    pipe?.Dispose();
                    pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut);

                    var connectTask = pipe.ConnectAsync(1000);
                    await connectTask;

                    if (pipe.IsConnected)
                    {
                        logger.Debug($"Connected to Discord pipe {i}");
                        await HandshakeAsync();
                        isConnected = true;
                        logger.Debug("Discord RPC connection successful");
                        return;
                    }
                }
                catch (TimeoutException)
                {
                    logger.Debug($"Timeout connecting to pipe {i}");
                    pipe?.Dispose();
                    pipe = null;
                }
                catch (Exception ex)
                {
                    logger.Debug($"Failed to connect to pipe {i}: {ex.Message}");
                    pipe?.Dispose();
                    pipe = null;
                }
            }

            logger.Error("Failed to connect to Discord - make sure Discord is running and the application is registered");
            throw new Exception("Could not connect to Discord IPC");
        }
        public void Reconnect()
        {
            // Fire-and-forget safe reconnect
            _ = ReconnectAsync();
        }

        private async Task ReconnectAsync()
        {
            if (_disposed) return;
            logger.Debug("Forcing Discord RPC reconnect");

            // Reset connection state
            isConnected = false;
            StopHeartbeat();
            pipe?.Dispose();
            pipe = null;

            // Exponential backoff for reconnection
            var delay = TimeSpan.FromMilliseconds(200);
            var maxDelay = TimeSpan.FromSeconds(5);
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    logger.Debug($"Reconnect attempt #{attempt}...");
                    await ConnectAsync();
                    StartHeartbeat(); // Restart heartbeat after reconnect
                    return; // Successfully reconnected
                }
                catch (Exception ex)
                {
                    logger.Warn($"Reconnect failed attempt #{attempt}: {ex.Message}");
                    if (attempt < 5)
                    {
                        // Increase delay for next attempt (exponential backoff)
                        delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                        if (delay > maxDelay) delay = maxDelay;
                        await Task.Delay(delay);
                    }
                    else
                    {
                        logger.Error("Reconnect attempts exhausted.");
                    }
                }
            }
        }

        private void StopHeartbeat()
        {
            try { _heartbeatTimer?.Dispose(); } catch { }
            _heartbeatTimer = null;
        }

        private async Task PingDiscordAsync()
        {
            if (!isConnected) return;
            await SendPingAsync();
            _lastPongUtc = DateTime.UtcNow; // Reset pong time after each ping
        }

        private void StartHeartbeat()
        {
            try
            {
                // Start heartbeat every 10 seconds
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = new Timer(async _ =>
                {
                    await PingDiscordAsync();
                    // Check for ping timeouts (if no pong received within 15 seconds)
                    if (DateTime.UtcNow - _lastPongUtc > _pongTimeout)
                    {
                        logger.Warn("Discord RPC heartbeat timed out; reconnecting...");
                        await ReconnectAsync();
                    }
                }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10)); // Start immediately and repeat every 10 seconds
            }
            catch (Exception ex)
            {
                logger.Error($"Heartbeat error: {ex.Message}");
            }
        }


        private async Task SendPingAsync()
        {
            // Discord expects opcode Ping with any payload; we'll send empty object
            await SendAsync(OpCode.Ping, new { });
            // After sending ping, we'll attempt to read a single frame (non-blocking read is handled by ReadResponseAsync call during normal traffic)
            // Note: We rely on UpdatePresence/handshake responses too; _lastPongUtc is updated in ReadResponseAsync when OpCode.Pong arrives.
        }
        private async Task HandshakeAsync()
        {
            var handshake = new
            {
                v = 1,
                client_id = applicationId
            };

            logger.Debug($"Sending handshake with client_id: {applicationId}");
            await SendAsync(OpCode.Handshake, handshake);

            try
            {
                var response = await ReadResponseAsync();
                logger.Debug($"Handshake response received: {response}");

                if (string.IsNullOrEmpty(response))
                {
                    throw new Exception("Empty handshake response from Discord");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"ERROR reading handshake response: {ex}");
                throw;
            }
        }

        public void UpdatePresence(DiscordPresence presence)
        {
            if (!isConnected)
            {
                logger.Debug("Cannot set presence - not connected to Discord");
                Reconnect(); // Auto-reconnect if not connected
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    //logger.Debug($"Setting presence: {presence?.Details} | {presence?.State}");

                    // Create an activity object with conditional inclusion of buttons
                    var activity = new
                    {
                        details = presence?.Details,
                        state = presence?.State,
                        timestamps = presence?.StartTimestamp > 0 ? new { start = presence.StartTimestamp } : null,
                        assets = new
                        {
                            large_image = presence?.LargeImageKey,
                            large_text = presence?.LargeImageText,
                            small_image = presence?.SmallImageKey,
                            small_text = presence?.SmallImageText
                        }
                    };

                    // Creating a basic payload
                    var payload = new
                    {
                        cmd = "SET_ACTIVITY",
                        args = new
                        {
                            pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                            activity = presence?.Buttons?.Any() == true ?
                                // If there are buttons, add them to the activity
                                new
                                {
                                    activity.details,
                                    activity.state,
                                    activity.timestamps,
                                    activity.assets,
                                    buttons = presence.Buttons.Select(b => new { label = b.Label, url = b.Url }).ToArray()
                                } :
                                // If there are no buttons, do not include the buttons field at all.
                                (object)activity
                        },
                        nonce = GetNextNonce().ToString()
                    };

                    await SendAsync(OpCode.Frame, payload);

                    // Reading the response from Discord
                    var response = await ReadResponseAsync();
                    if (!string.IsNullOrEmpty(response))
                    {
                        //logger.Debug($"Discord response: {response}");
                    }
                    else
                    {
                        logger.Warn("No response received from Discord after setting presence");
                    }

                    //logger.Debug("Presence update sent successfully");
                }
                catch (Exception ex)
                {
                    logger.Error($"ERROR setting presence: {ex}");
                    isConnected = false;
                    await ReconnectAsync();
                }
            });
        }

        public void ClearPresence()
        {
            if (!isConnected)
            {
                logger.Debug("Cannot clear presence - not connected to Discord");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var payload = new
                    {
                        cmd = "SET_ACTIVITY",
                        args = new { pid = System.Diagnostics.Process.GetCurrentProcess().Id },
                        nonce = GetNextNonce().ToString()
                    };

                    await SendAsync(OpCode.Frame, payload);
                    logger.Debug("Presence cleared successfully");
                }
                catch (Exception ex)
                {
                    logger.Error($"ERROR clearing presence: {ex}");
                    isConnected = false;
                    await ReconnectAsync();
                }
            });
        }

        private static async Task ReadExactAsync(Stream s, byte[] buffer, int length)
        {
            var read = 0;
            while (read < length)
            {
                var n = await s.ReadAsync(buffer, read, length - read).ConfigureAwait(false);
                if (n == 0)
                {
                    throw new EndOfStreamException("Discord IPC pipe closed while reading.");
                }
                read += n;
            }
        }


        private async Task<string> ReadResponseAsync()
        {
            if (pipe == null || !pipe.IsConnected)
            {
                return null;
            }

            try
            {
                // Discord IPC header: 8 bytes → [0..3] OpCode (int32 LE), [4..7] length (int32 LE)
                var header = new byte[8];
                await ReadExactAsync(pipe, header, 8).ConfigureAwait(false);

                var opCode = BitConverter.ToInt32(header, 0);
                var length = BitConverter.ToInt32(header, 4);

                // Simple length validation (prevents OOM/errors)
                const int MaxPayloadBytes = 1 << 20; // 1 MiB
                if (length < 0 || length > MaxPayloadBytes)
                {
                    throw new InvalidDataException($"Invalid Discord IPC payload length: {length}");
                }

                var payload = new byte[length];
                if (length > 0)
                {
                    await ReadExactAsync(pipe, payload, length).ConfigureAwait(false);
                }

                if (opCode == (int)OpCode.Pong) { _lastPongUtc = DateTime.UtcNow; }

                return length > 0 ? Encoding.UTF8.GetString(payload) : string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error($"ERROR reading response: {ex}");
                isConnected = false;
                _ = ReconnectAsync();
                return null;
            }
        }

        private async Task SendAsync(OpCode opCode, object payload)
        {
            if (pipe == null || !pipe.IsConnected)
            {
                logger.Debug("Cannot send - pipe is null or not connected");
                await ReconnectAsync();
                return;
            }

            try
            {
                var json = Serialization.ToJson(payload, false);
                var data = Encoding.UTF8.GetBytes(json);
                var header = new byte[8];
                BitConverter.GetBytes((int)opCode).CopyTo(header, 0);
                BitConverter.GetBytes(data.Length).CopyTo(header, 4);

                // Ensure the pipe is ready before writing
                if (!pipe.IsConnected)
                {
                    logger.Warn("Pipe disconnected, attempting to reconnect...");
                    await ReconnectAsync();
                    return;
                }

                await pipe.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                if (data?.Length > 0)
                {
                    await pipe.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                }
                await pipe.FlushAsync().ConfigureAwait(false);

            }
            catch (Exception ex)
            {
                logger.Error($"ERROR sending data to Discord: {ex.Message}");
                isConnected = false;
                await ReconnectAsync(); // Attempt to reconnect if sending fails
            }
        }

        private int GetNextNonce() => Interlocked.Increment(ref nonce);

        public void Dispose()
        {
            try
            {
                _disposed = true;
                isConnected = false;
                StopHeartbeat();
                pipe?.Dispose();
                pipe = null;
                logger.Debug("Discord RPC disposed");
            }
            catch (Exception ex)
            {
                logger.Error($"ERROR disposing Discord RPC: {ex}");
            }
        }
    }
}
