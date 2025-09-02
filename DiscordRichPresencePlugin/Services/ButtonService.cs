using System;
using System.Collections.Generic;
using System.Linq;
using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Enums;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace DiscordRichPresencePlugin.Services
{
    /// <summary>
    /// Service for creating and managing Discord Rich Presence buttons
    /// </summary>
    public class ButtonService
    {
        private readonly ILogger logger;
        private readonly DiscordRichPresenceSettings settings;

        public ButtonService(ILogger logger, DiscordRichPresenceSettings settings)
        {
            this.logger = logger;
            this.settings = settings;
        }

        /// <summary>
        /// Creates buttons based on game info and settings
        /// </summary>
        public DiscordButton[] CreateButtons(Game game, ExtendedGameInfo extendedInfo)
        {
            if (!settings.ShowButtons || game == null)
                return null;

            var buttons = new List<DiscordButton>();

            // Priority 1: Join Game button for multiplayer
            if (extendedInfo?.SupportsMultiplayer == true)
            {
                var joinButton = CreateJoinGameButton(game, extendedInfo);
                if (joinButton != null)
                    buttons.Add(joinButton);
            }

            // Priority 2: Store page button
            var storeButton = CreateStoreButton(game, extendedInfo);
            if (storeButton != null && buttons.Count < 2)
                buttons.Add(storeButton);

            // Priority 3: Achievement/Stats button
            if (buttons.Count < 2 && extendedInfo?.TotalAchievements > 0)
            {
                var achievementButton = CreateAchievementButton(game, extendedInfo);
                if (achievementButton != null)
                    buttons.Add(achievementButton);
            }

            // Priority 4: Community/Social button
            if (buttons.Count < 2)
            {
                var communityButton = CreateCommunityButton(game, extendedInfo);
                if (communityButton != null)
                    buttons.Add(communityButton);
            }

            // Fallback: Generic game info button
            if (buttons.Count == 0)
            {
                var defaultButton = CreateDefaultButton(game);
                if (defaultButton != null)
                    buttons.Add(defaultButton);
            }

            // Discord supports maximum 2 buttons
            return buttons.Take(2).ToArray();
        }

        /// <summary>
        /// Creates a Join Game button for multiplayer games
        /// </summary>
        private DiscordButton CreateJoinGameButton(Game game, ExtendedGameInfo info)
        {
            // Check for Steam multiplayer link
            if (game.GameId != null && game.Source?.Name?.ToLower() == "steam")
            {
                return new DiscordButton
                {
                    Label = "Join Game",
                    Url = $"steam://run/{game.GameId}"
                };
            }

            // Check for other multiplayer links in game links
            var multiplayerLink = game.Links?.FirstOrDefault(l =>
                l.Name?.ToLower().Contains("multiplayer") == true ||
                l.Name?.ToLower().Contains("server") == true);

            if (multiplayerLink != null)
            {
                return new DiscordButton
                {
                    Label = "Join Server",
                    Url = multiplayerLink.Url
                };
            }

            return null;
        }

        /// <summary>
        /// Creates a store page button
        /// </summary>
        private DiscordButton CreateStoreButton(Game game, ExtendedGameInfo info)
        {
            // Priority: Steam > Epic > GOG > Other stores
            string storeUrl = null;
            string storeName = "View in Store";

            // Check extended info store links first
            if (info?.StoreLinks?.Any() == true)
            {
                if (info.StoreLinks.TryGetValue("Steam", out storeUrl))
                    storeName = "View on Steam";
                else if (info.StoreLinks.TryGetValue("Epic Games", out storeUrl))
                    storeName = "View on Epic";
                else if (info.StoreLinks.TryGetValue("GOG", out storeUrl))
                    storeName = "View on GOG";
                else
                    storeUrl = info.StoreLinks.First().Value;
            }

            // Fallback to game links
            if (string.IsNullOrEmpty(storeUrl))
            {
                var storeLink = game.Links?.FirstOrDefault(l =>
                    l.Name?.ToLower().Contains("store") == true ||
                    l.Name?.ToLower().Contains("steam") == true ||
                    l.Name?.ToLower().Contains("epic") == true ||
                    l.Name?.ToLower().Contains("gog") == true);

                if (storeLink != null)
                {
                    storeUrl = storeLink.Url;
                    if (storeLink.Name.ToLower().Contains("steam"))
                        storeName = "View on Steam";
                    else if (storeLink.Name.ToLower().Contains("epic"))
                        storeName = "View on Epic";
                    else if (storeLink.Name.ToLower().Contains("gog"))
                        storeName = "View on GOG";
                }
            }

            if (!string.IsNullOrEmpty(storeUrl))
            {
                return new DiscordButton
                {
                    Label = storeName,
                    Url = storeUrl
                };
            }

            return null;
        }

        /// <summary>
        /// Creates an achievement tracking button
        /// </summary>
        private DiscordButton CreateAchievementButton(Game game, ExtendedGameInfo info)
        {
            string label = $"Achievements ({info.AchievementsEarned}/{info.TotalAchievements})";

            // Try to find achievement tracking link
            var achievementLink = game.Links?.FirstOrDefault(l =>
                l.Name?.ToLower().Contains("achievement") == true ||
                l.Name?.ToLower().Contains("trophy") == true);

            if (achievementLink != null)
            {
                return new DiscordButton
                {
                    Label = label,
                    Url = achievementLink.Url
                };
            }

            // Fallback to Steam achievements if it's a Steam game
            if (game.GameId != null && game.Source?.Name?.ToLower() == "steam")
            {
                return new DiscordButton
                {
                    Label = label,
                    Url = $"https://steamcommunity.com/stats/{game.GameId}/achievements"
                };
            }

            return null;
        }

        /// <summary>
        /// Creates a community/social button
        /// </summary>
        private DiscordButton CreateCommunityButton(Game game, ExtendedGameInfo info)
        {
            // Check extended info social links
            if (info?.SocialLinks?.Any() == true)
            {
                // Priority: Discord > Reddit > Forums > Other
                string url = null;
                string label = "Community";

                if (info.SocialLinks.TryGetValue("Discord", out url))
                    label = "Join Discord";
                else if (info.SocialLinks.TryGetValue("Reddit", out url))
                    label = "Reddit Community";
                else if (info.SocialLinks.TryGetValue("Forum", out url))
                    label = "Game Forum";
                else
                {
                    var first = info.SocialLinks.First();
                    url = first.Value;
                    label = first.Key;
                }

                return new DiscordButton { Label = label, Url = url };
            }

            // Check game links for community
            var communityLink = game.Links?.FirstOrDefault(l =>
                l.Name?.ToLower().Contains("discord") == true ||
                l.Name?.ToLower().Contains("reddit") == true ||
                l.Name?.ToLower().Contains("forum") == true ||
                l.Name?.ToLower().Contains("community") == true);

            if (communityLink != null)
            {
                return new DiscordButton
                {
                    Label = communityLink.Name,
                    Url = communityLink.Url
                };
            }

            return null;
        }

        /// <summary>
        /// Creates a default fallback button
        /// </summary>
        private DiscordButton CreateDefaultButton(Game game)
        {
            // If game has any links, use the first one
            if (game.Links?.Any() == true)
            {
                var firstLink = game.Links.First();
                return new DiscordButton
                {
                    Label = firstLink.Name ?? "Game Info",
                    Url = firstLink.Url
                };
            }

            // Ultimate fallback - Playnite game link
            return new DiscordButton
            {
                Label = "View in Playnite",
                Url = $"playnite://playnite/game/{game.Id}"
            };
        }

        /// <summary>
        /// Creates custom buttons from user configuration
        /// </summary>
        public DiscordButton[] CreateCustomButtons(List<CustomButtonConfig> configs, Game game, ExtendedGameInfo info)
        {
            if (configs == null || !configs.Any())
                return null;

            var buttons = new List<DiscordButton>();

            foreach (var config in configs.Where(c => c.IsEnabled))
            {
                var button = CreateCustomButton(config, game, info);
                if (button != null)
                    buttons.Add(button);

                if (buttons.Count >= 2) // Discord limit
                    break;
            }

            return buttons.ToArray();
        }

        private DiscordButton CreateCustomButton(CustomButtonConfig config, Game game, ExtendedGameInfo info)
        {
            if (string.IsNullOrEmpty(config.Label) || string.IsNullOrEmpty(config.UrlTemplate))
                return null;

            try
            {
                // Replace variables in URL template
                var url = config.UrlTemplate
                    .Replace("{gameId}", game.Id.ToString())
                    .Replace("{gameName}", Uri.EscapeDataString(game.Name))
                    .Replace("{platform}", game.Platforms?.FirstOrDefault()?.Name ?? "")
                    .Replace("{source}", game.Source?.Name ?? "");

                // Replace variables in label
                var label = config.Label
                    .Replace("{gameName}", game.Name)
                    .Replace("{completion}", info?.CompletionPercentage.ToString() ?? "0")
                    .Replace("{achievements}", $"{info?.AchievementsEarned ?? 0}/{info?.TotalAchievements ?? 0}");

                return new DiscordButton { Label = label, Url = url };
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to create custom button: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Configuration for custom buttons
    /// </summary>
    public class CustomButtonConfig
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string UrlTemplate { get; set; }
        public ButtonActionType ActionType { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int Priority { get; set; } = 0;
    }
}