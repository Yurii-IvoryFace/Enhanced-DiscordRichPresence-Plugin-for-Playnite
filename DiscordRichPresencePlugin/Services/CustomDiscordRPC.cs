using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Enums;
using Playnite.SDK;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Playnite.SDK.Data;


namespace DiscordRichPresencePlugin.Services
{
    public class CustomDiscordRPC : IDisposable
    {
        private readonly string applicationId;
        private readonly ILogger logger;
        private NamedPipeClientStream pipe;
        private bool isConnected = false;
        private int nonce = 0;

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
            logger.Debug("Forcing Discord RPC reconnect");
            try { Dispose(); } catch { /* ignore */ }
            Initialize();
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
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    //logger.Debug($"Setting presence: {presence?.Details} | {presence?.State}");

                    // Створюємо об'єкт activity з умовним включенням buttons
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

                    // Створюємо payload базовий
                    var payload = new
                    {
                        cmd = "SET_ACTIVITY",
                        args = new
                        {
                            pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                            activity = presence?.Buttons?.Any() == true ?
                                // Якщо є кнопки, додаємо їх до activity
                                new
                                {
                                    activity.details,
                                    activity.state,
                                    activity.timestamps,
                                    activity.assets,
                                    buttons = presence.Buttons.Select(b => new { label = b.Label, url = b.Url }).ToArray()
                                } :
                                // Якщо немає кнопок, не включаємо поле buttons взагалі
                                (object)activity
                        },
                        nonce = (++nonce).ToString()
                    };

                    await SendAsync(OpCode.Frame, payload);

                    // Читаємо відповідь від Discord
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
                        nonce = (++nonce).ToString()
                    };

                    await SendAsync(OpCode.Frame, payload);
                    logger.Debug("Presence cleared successfully");
                }
                catch (Exception ex)
                {
                    logger.Error($"ERROR clearing presence: {ex}");
                    isConnected = false;
                }
            });
        }

        private async Task<string> ReadResponseAsync()
        {
            if (pipe == null || !pipe.IsConnected) return null;

            try
            {
                var header = new byte[8];
                await pipe.ReadAsync(header, 0, 8);

                var opCode = BitConverter.ToInt32(header, 0);
                var length = BitConverter.ToInt32(header, 4);

                //logger.Debug($"Reading response: OpCode={opCode}, Length={length}");

                var data = new byte[length];
                await pipe.ReadAsync(data, 0, length);

                var response = Encoding.UTF8.GetString(data);
                //logger.Debug($"Response received: {response}");

                return response;
            }
            catch (Exception ex)
            {
                logger.Error($"ERROR reading response: {ex}");
                return null;
            }
        }

        private async Task SendAsync(OpCode opCode, object payload)
        {
            if (pipe == null || !pipe.IsConnected)
            {
                logger.Debug("Cannot send - pipe is null or not connected");
                return;
            }

            try
            {
                var json = Serialization.ToJson(payload, false);
                //logger.Debug($"Sending payload: {json}");

                var data = Encoding.UTF8.GetBytes(json);

                var header = new byte[8];
                BitConverter.GetBytes((int)opCode).CopyTo(header, 0);
                BitConverter.GetBytes(data.Length).CopyTo(header, 4);

                await pipe.WriteAsync(header, 0, header.Length);
                await pipe.WriteAsync(data, 0, data.Length);
                await pipe.FlushAsync();

                //logger.Debug("Data sent successfully");
            }
            catch (Exception ex)
            {
                logger.Error($"ERROR sending data to Discord: {ex}");
                isConnected = false;
            }
        }

        public void Dispose()
        {
            try
            {
                isConnected = false;
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