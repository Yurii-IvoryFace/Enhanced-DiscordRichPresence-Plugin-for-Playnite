using System;
using System.Linq;
using System.Timers;
using Playnite.SDK;
using Playnite.SDK.Models;
using DiscordRichPresencePlugin.Enums;
using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Services;

namespace DiscordRichPresencePlugin.Services
{
    public class DiscordRpcService : IDisposable
    {
        private readonly CustomDiscordRPC discordRPC;
        private readonly ILogger logger;
        private readonly DiscordRichPresenceSettings settings;
        private readonly GameMappingService mappingService;
        private readonly TemplateService templateService;
        private readonly ExtendedGameInfoService extendedInfoService;
        private readonly ButtonService buttonService;

        private Timer presenceUpdateTimer;
        private Game currentGame;
        private DateTime gameStartTime;
        private ExtendedGameInfo currentExtendedInfo;

        public DiscordRpcService(
            string appId,
            ILogger logger,
            DiscordRichPresenceSettings settings,
            GameMappingService mappingService,
            TemplateService templateService,
            ExtendedGameInfoService extendedInfoService,
            ButtonService buttonService)
        {
            this.logger = logger;
            this.settings = settings;
            this.mappingService = mappingService;
            this.templateService = templateService;
            this.extendedInfoService = extendedInfoService;
            this.buttonService = buttonService;

            discordRPC = new CustomDiscordRPC(appId, logger);
        }

        public void Initialize()
        {
            logger.Debug("Initializing DiscordRpcService");
            discordRPC.Initialize();
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

            // hydrate/refresh extended info and mark session start
            currentExtendedInfo = extendedInfoService?.GetOrCreateGameInfo(game);
            extendedInfoService?.StartSession(game.Id);

            UpdatePresence();
            StartUpdateTimer();
        }

        private void StartUpdateTimer()
        {
            presenceUpdateTimer?.Stop();
            presenceUpdateTimer?.Dispose();

            var interval = Math.Max(Constants.MIN_UPDATE_INTERVAL, Math.Min(Constants.MAX_UPDATE_INTERVAL, settings.UpdateInterval));
            presenceUpdateTimer = new Timer(interval * 1000);
            presenceUpdateTimer.Elapsed += (_, __) => UpdatePresence();
            presenceUpdateTimer.AutoReset = true;
            presenceUpdateTimer.Start();

            logger.Debug($"Presence update timer started with interval: {interval}s");
        }

        private void UpdatePresence()
        {
            if (currentGame == null || !settings.EnableRichPresence)
            {
                return;
            }

            try
            {
                var startTimestamp = settings.ShowElapsedTime
                    ? ((DateTimeOffset)gameStartTime).ToUnixTimeSeconds()
                    : 0;

                logger.Debug($"Game start time: {gameStartTime}, Unix timestamp: {startTimestamp}");

                var buttons = BuildButtons();

                var presence = new DiscordPresence
                {
                    Details = FormatGameDetails(),
                    State = FormatGameState(),
                    StartTimestamp = startTimestamp,
                    LargeImageKey = GetGameImageKey(),
                    LargeImageText = currentGame.Name,
                    SmallImageKey = "playnite_logo",
                    SmallImageText = "via Playnite",
                    Buttons = buttons
                };

                discordRPC.UpdatePresence(presence);
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to update Discord presence: {ex}");
            }
        }

        private string FormatGameDetails()
        {
            if (currentGame == null)
                return string.Empty;

            // Template-based Details
            if (settings.UseTemplates && templateService != null)
            {
                var t = templateService.SelectTemplate(currentGame, currentExtendedInfo, gameStartTime);
                var formatted = templateService.FormatTemplateString(t?.DetailsFormat, currentGame, currentExtendedInfo, gameStartTime);
                if (!string.IsNullOrWhiteSpace(formatted))
                    return formatted;
            }

            // Fallback to simple custom format
            var template = string.IsNullOrEmpty(settings.CustomStatus)
                ? Constants.DEFAULT_STATUS_FORMAT
                : settings.CustomStatus;

            return template.Replace("{game}", currentGame.Name);
        }

        private string FormatGameState()
        {
            if (currentGame == null)
                return string.Empty;

            // Template-based State
            if (settings.UseTemplates && templateService != null)
            {
                var t = templateService.SelectTemplate(currentGame, currentExtendedInfo, gameStartTime);
                var formatted = templateService.FormatTemplateString(t?.StateFormat, currentGame, currentExtendedInfo, gameStartTime);
                if (!string.IsNullOrWhiteSpace(formatted))
                    return formatted;
            }

            // Legacy/manual construction with optional extras
            var parts = new System.Collections.Generic.List<string>();

            // Platforms
            if (settings.ShowPlatform && currentGame.Platforms?.Any() == true)
            {
                parts.Add(string.Join(", ", currentGame.Platforms.Select(p => p.Name)));
            }

            // Source
            if (settings.ShowSource && currentGame.Source != null)
            {
                parts.Add(currentGame.Source.Name);
            }

            // Genres
            if (settings.ShowGenre && currentGame.Genres?.Any() == true)
            {
                parts.Add(string.Join(", ", currentGame.Genres.Select(g => g.Name)));
            }

            // Total playtime
            if (settings.ShowPlaytime && currentGame.Playtime > 0)
            {
                var totalSeconds = (long)currentGame.Playtime;
                var hours = totalSeconds / 3600;
                var minutes = (totalSeconds % 3600) / 60;
                if (hours > 0)
                {
                    parts.Add($"{hours}h {minutes}m played");
                }
                else
                {
                    parts.Add($"{minutes}m played");
                }
            }

            // Progress (from ExtendedGameInfo)
            if (settings.ShowCompletionPercentage && currentExtendedInfo != null)
            {
                parts.Add($"{currentExtendedInfo.CompletionPercentage}% complete");
            }

            if (settings.ShowAchievements && currentExtendedInfo != null && currentExtendedInfo.TotalAchievements > 0)
            {
                parts.Add($"🏆 {currentExtendedInfo.AchievementsEarned}/{currentExtendedInfo.TotalAchievements}");
            }

            return string.Join(" | ", parts);
        }

        private string GetGameImageKey()
        {
            // Prefer explicit mapping; fallback to default logo
            var finalImageKey = mappingService?.GetImageKeyForGame(currentGame?.Name)
                                ?? Constants.DEFAULT_FALLBACK_IMAGE;

            if (string.IsNullOrWhiteSpace(finalImageKey))
            {
                finalImageKey = Constants.DEFAULT_FALLBACK_IMAGE;
            }

            return finalImageKey;
        }

        private DiscordButton[] BuildButtons()
        {
            if (!settings.ShowButtons || currentGame == null)
                return null;

            if (settings.ButtonMode == ButtonDisplayMode.Off)
                return null;

            // Auto mode – let ButtonService decide best two buttons based on game/extended info
            if (buttonService != null)
            {
                return buttonService.CreateButtons(currentGame, currentExtendedInfo);
            }

            // Minimal fallback if service is unavailable
            if (currentGame.Links?.Any() == true)
            {
                var first = currentGame.Links.First();
                return new[] { new DiscordButton { Label = "Game Info", Url = first.Url } };
            }

            return new[]
            {
                new DiscordButton
                {
                    Label = "View Game",
                    Url = $"playnite://playnite/game/{currentGame.Id}"
                }
            };
        }

        public void ClearPresence()
        {
            logger.Debug("Clearing Discord presence");
            currentGame = null;
            currentExtendedInfo = null;
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
