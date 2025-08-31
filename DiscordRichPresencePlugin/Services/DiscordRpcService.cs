using DiscordRichPresencePlugin.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Linq;
using System.Timers;

namespace DiscordRichPresencePlugin.Services
{
    public class DiscordRpcService : IDisposable
    {
        private readonly CustomDiscordRPC discordRPC;
        private readonly ILogger logger;
        private readonly DiscordRichPresenceSettings settings;
        private readonly GameMappingService mappingService;
        private Timer presenceUpdateTimer;
        private Game currentGame;
        private DateTime gameStartTime;

        public DiscordRpcService(string appId, ILogger logger, DiscordRichPresenceSettings settings, GameMappingService mappingService)
        {
            this.logger = logger;
            this.settings = settings;
            this.mappingService = mappingService;
            discordRPC = new CustomDiscordRPC(appId, logger);
        }

        public void Initialize()
        {
            if (settings.EnableRichPresence)
            {
                logger.Debug("Initializing Discord RPC service");
                discordRPC.Initialize();
            }
        }

        public void UpdateGamePresence(Game game)
        {
            if (game == null)
            {
                logger.Debug("Attempted to update presence with null game");
                return;
            }

            logger.Debug($"Updating game presence for: {game.Name}");
            currentGame = game;
            gameStartTime = DateTime.UtcNow;

            UpdatePresence();
            StartUpdateTimer();
        }

        private void UpdatePresence()
        {
            if (currentGame == null || !settings.EnableRichPresence)
            {
                return;
            }

            try
            {
                var startTimestamp = settings.ShowElapsedTime ?
                    ((DateTimeOffset)gameStartTime).ToUnixTimeSeconds() : 0;

                logger.Debug($"Game start time: {gameStartTime}, Unix timestamp: {startTimestamp}");

                var presence = new DiscordPresence
                {
                    Details = FormatGameDetails(),
                    State = FormatGameState(),
                    StartTimestamp = startTimestamp,
                    LargeImageKey = GetGameImageKey(),
                    LargeImageText = currentGame.Name,
                    SmallImageKey = "playnite_logo",
                    SmallImageText = "via Playnite",
                    Buttons = settings.ShowButtons ? CreateButtons() : null
                };

                logger.Debug($"Created presence - Details: '{presence.Details}', State: '{presence.State}', StartTimestamp: {presence.StartTimestamp}");
                discordRPC.UpdatePresence(presence);
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to update Discord presence: {ex.Message}");
            }
        }

        private string FormatGameDetails()
        {
            if (currentGame == null)
                return string.Empty;

            var template = !string.IsNullOrEmpty(settings.CustomStatus) ?
                settings.CustomStatus : Constants.DEFAULT_STATUS_FORMAT;

            return template.Replace("{game}", currentGame.Name);
        }

        private string FormatGameState()
        {
            if (currentGame == null)
                return string.Empty;

            var stateComponents = new System.Collections.Generic.List<string>();

            // Fix: Use Platforms collection instead of Platform property
            if (settings.ShowPlatform && currentGame.Platforms?.Any() == true)
            {
                stateComponents.Add(string.Join(", ", currentGame.Platforms.Select(p => p.Name)));
            }

            if (settings.ShowSource && currentGame.Source != null)
            {
                stateComponents.Add(currentGame.Source.Name);
            }

            if (settings.ShowGenre && currentGame.Genres?.Any() == true)
            {
                stateComponents.Add(string.Join(", ", currentGame.Genres.Select(g => g.Name)));
            }

            if (settings.ShowPlaytime && currentGame.Playtime > 0)
            {
                var hours = currentGame.Playtime / 60;
                var minutes = currentGame.Playtime % 60;
                if (hours > 0)
                {
                    stateComponents.Add($"{hours}h {minutes}m played");
                }
                else
                {
                    stateComponents.Add($"{minutes}m played");
                }
            }

            return string.Join(" | ", stateComponents);
        }

        private string GetGameImageKey()
        {
            if (currentGame == null)
                return settings.FallbackImageKey ?? Constants.DEFAULT_FALLBACK_IMAGE;

            var imageKey = mappingService.GetImageKeyForGame(currentGame.Name) ??
                          settings.FallbackImageKey ??
                          Constants.DEFAULT_FALLBACK_IMAGE;

            logger.Debug($"Using image key: {imageKey} for game: {currentGame.Name}");
            return imageKey;
        }

        private DiscordButton[] CreateButtons()
        {
            if (currentGame?.Links?.Any() == true)
            {
                var firstLink = currentGame.Links.First();
                return new[]
                {
                    new DiscordButton
                    {
                        Label = "Game Info",
                        Url = firstLink.Url
                    }
                };
            }

            // Fallback button
            return new[]
            {
                new DiscordButton
                {
                    Label = "View Game",
                    Url = $"playnite://playnite/game/{currentGame.Id}"
                }
            };
        }

        private void StartUpdateTimer()
        {
            if (presenceUpdateTimer != null)
            {
                presenceUpdateTimer.Stop();
                presenceUpdateTimer.Elapsed -= OnPresenceTimerElapsed;
                presenceUpdateTimer.Dispose();
            }

            var interval = TimeSpan.FromSeconds(settings.UpdateInterval);
            presenceUpdateTimer = new Timer(interval.TotalMilliseconds)
            {
                AutoReset = true
            };
            presenceUpdateTimer.Elapsed += OnPresenceTimerElapsed;
            presenceUpdateTimer.Start();

            logger.Debug($"Started presence update timer with {settings.UpdateInterval}s interval");
        }

        private void OnPresenceTimerElapsed(object sender, ElapsedEventArgs e)
        {
            UpdatePresence();
        }

        public void ClearPresence()
        {
            logger.Debug("Clearing Discord presence");
            currentGame = null;
            presenceUpdateTimer?.Dispose();
            presenceUpdateTimer = null;
            discordRPC?.ClearPresence();
        }

        public void Dispose()
        {
            logger.Debug("Disposing Discord RPC service");
            presenceUpdateTimer?.Dispose();
            discordRPC?.Dispose();
        }
    }
}