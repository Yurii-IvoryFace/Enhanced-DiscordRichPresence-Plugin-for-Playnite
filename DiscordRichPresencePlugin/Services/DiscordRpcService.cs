using System;
using System.Linq;
using System.Timers;
using Playnite.SDK;
using Playnite.SDK.Models;
using DiscordRichPresencePlugin.Enums;
using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Services;
using DiscordRichPresencePlugin.Helpers;

namespace DiscordRichPresencePlugin.Services
{
    public class DiscordRpcService : IDisposable
    {
        private CustomDiscordRPC discordRPC; // <- no readonly
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

        private string appId; // <- track current app id

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

            this.appId = appId;
            discordRPC = new CustomDiscordRPC(this.appId, logger);
        }

        public void Initialize()
        {
            if (settings.EnableRichPresence)
            {
                logger.Debug("Initializing Discord RPC service");
                discordRPC.Initialize();
            }
        }

        /// <summary>
        /// Повна переініціалізація з новим App ID (викликати після зміни налаштувань).
        /// </summary>
        public void Reinitialize(string newAppId)
        {
            var target = string.IsNullOrWhiteSpace(newAppId) ? Constants.DISCORD_APP_ID : newAppId.Trim();
            if (string.Equals(appId, target, StringComparison.Ordinal))
            {
                logger.Debug("Reinitialize called with the same App ID, skipping.");
                return;
            }

            logger.Info($"Reinitializing Discord RPC: {appId} -> {target}");

            // зупинити таймер, прибрати старий RPC
            try { presenceUpdateTimer?.Stop(); } catch { }
            presenceUpdateTimer?.Dispose();
            presenceUpdateTimer = null;

            try { discordRPC?.Dispose(); } catch { }

            // створити новий RPC і ініціалізувати
            appId = target;
            discordRPC = new CustomDiscordRPC(appId, logger);
            discordRPC.Initialize();

            // якщо гра активна — відновити presence і таймер
            if (currentGame != null)
            {
                UpdatePresence();
                StartUpdateTimer();
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

                var buttons = BuildButtons();

                var presence = new DiscordPresence
                {
                    Details = FormatGameDetails(),
                    State = FormatGameState(),
                    StartTimestamp = startTimestamp,
                    LargeImageKey = GetGameImageKey(),
                    LargeImageText = currentGame.Name,
                    SmallImageKey = Constants.DEFAULT_FALLBACK_IMAGE,
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

            // Total playtime (seconds -> H/M)
            if (settings.ShowPlaytime && currentGame.Playtime > 0)
            {
                parts.Add(TimeFormat.FormatPlaytimeSeconds((long)currentGame.Playtime));
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

            DiscordButton[] raw = null;

            if (buttonService != null)
            {
                raw = buttonService.CreateButtons(currentGame, currentExtendedInfo);
            }
            else
            {
                raw = currentGame?.Links?
                    .Where(l => IsSupportedUrl(l?.Url))
                    .Take(2)
                    .Select(l => new DiscordButton { Label = string.IsNullOrWhiteSpace(l.Name) ? "Open link" : l.Name, Url = l.Url })
                    .ToArray();
            }

            var filtered = raw?
                .Where(b => IsSupportedUrl(b?.Url) && !string.IsNullOrWhiteSpace(b?.Label))
                .Take(2)
                .ToArray();

            if (filtered?.Any() == true)
            {
                // logger.Debug($"Buttons prepared: {string.Join(", ", filtered.Select(b => $"{b.Label} => {b.Url}"))}");
                return filtered;
            }

            logger.Debug("No valid https buttons available; sending presence without buttons.");
            return null;
        }

        private static bool IsSupportedUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            // Discord зазвичай приймає тільки HTTPS для кнопок
            return string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
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

        public void Reconnect()
        {
            discordRPC?.Reconnect();
        }
        public void Dispose()
        {
            logger.Debug("Disposing Discord RPC service");
            presenceUpdateTimer?.Dispose();
            discordRPC?.Dispose();
        }
    }
}
