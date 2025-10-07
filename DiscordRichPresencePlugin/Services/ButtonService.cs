// INPROGRESS


using System;
using System.Collections.Generic;
using System.Linq;
using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Enums;
using Playnite.SDK;
using Playnite.SDK.Models;


// INPROGRESS

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
        private static bool IsHttps(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            return string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a Join Game button for multiplayer games
        /// </summary>
        private DiscordButton CreateJoinGameButton(Game game, ExtendedGameInfo info)
        {
            // 1) HTTPS-join in Game.Links
            var httpJoin = game.Links?
                .FirstOrDefault(l =>
                    !string.IsNullOrWhiteSpace(l?.Url) &&
                    IsHttps(l.Url) &&
                    (
                        (l.Name?.IndexOf("join", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                        (l.Name?.IndexOf("server", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                        (l.Name?.IndexOf("multiplayer", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                    ));

            if (httpJoin != null)
            {
                return new DiscordButton
                {
                    Label = string.IsNullOrWhiteSpace(httpJoin.Name) ? "Join Server" : httpJoin.Name,
                    Url = httpJoin.Url
                };
            }

            // 2) HTTPS із ExtendedInfo.SocialLinks (whithout ?.)
            if (info?.SocialLinks != null)
            {
                var kv = info.SocialLinks
                    .FirstOrDefault(p =>
                        !string.IsNullOrWhiteSpace(p.Value) &&
                        IsHttps(p.Value) &&
                        (
                            p.Key?.Equals("Server", StringComparison.OrdinalIgnoreCase) == true ||
                            p.Key?.Equals("Multiplayer", StringComparison.OrdinalIgnoreCase) == true
                        ));

                if (!string.IsNullOrEmpty(kv.Value))
                {
                    var label = kv.Key != null && kv.Key.Equals("Server", StringComparison.OrdinalIgnoreCase)
                        ? "Join Server"
                        : "Join Multiplayer";

                    return new DiscordButton { Label = label, Url = kv.Value };
                }
            }

            // No valid HTTPS → null
            return null;
        }


        /// <summary>
        /// Creates a store page button
        /// </summary>
        private DiscordButton CreateStoreButton(Game game, ExtendedGameInfo info)
        {
            string storeUrl = null;
            string storeName = "View in Store";

            // a) Extended info (priority Steam/Epic/GOG)
            if (info?.StoreLinks != null && info.StoreLinks.Any())
            {
                if (info.StoreLinks.TryGetValue("Steam", out var steamUrl) && IsHttps(steamUrl))
                {
                    storeUrl = steamUrl; storeName = "View on Steam";
                }
                else if (info.StoreLinks.TryGetValue("Epic Games", out var epicUrl) && IsHttps(epicUrl))
                {
                    storeUrl = epicUrl; storeName = "View on Epic";
                }
                else if (info.StoreLinks.TryGetValue("GOG", out var gogUrl) && IsHttps(gogUrl))
                {
                    storeUrl = gogUrl; storeName = "View on GOG";
                }
                else
                {
                    // first HTTPS in storage
                    var firstHttps = info.StoreLinks.FirstOrDefault(kv => IsHttps(kv.Value));
                    if (!string.IsNullOrEmpty(firstHttps.Value))
                    {
                        storeUrl = firstHttps.Value;
                        if (!string.IsNullOrWhiteSpace(firstHttps.Key))
                            storeName = $"View on {firstHttps.Key}";
                    }
                }
            }

            // b) If not found in ExtendedInfo look in Game.Links
            if (string.IsNullOrEmpty(storeUrl) && game.Links?.Any() == true)
            {
                var storeLink = game.Links.FirstOrDefault(l =>
                    !string.IsNullOrWhiteSpace(l?.Url) &&
                    IsHttps(l.Url) &&
                    (
                        (l.Name?.IndexOf("store", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                        (l.Name?.IndexOf("steam", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                        (l.Name?.IndexOf("epic", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                        (l.Name?.IndexOf("gog", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                    ));

                if (storeLink != null)
                {
                    storeUrl = storeLink.Url;
                    if ((storeLink.Name?.IndexOf("steam", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0) storeName = "View on Steam";
                    else if ((storeLink.Name?.IndexOf("epic", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0) storeName = "View on Epic";
                    else if ((storeLink.Name?.IndexOf("gog", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0) storeName = "View on GOG";
                }
            }

            // c) fallback on Steam for GameId (HTTPS)
            if (string.IsNullOrEmpty(storeUrl) &&
                game.Source?.Name?.Equals("steam", StringComparison.OrdinalIgnoreCase) == true &&
                !string.IsNullOrEmpty(game.GameId) &&
                game.GameId.All(char.IsDigit))
            {
                storeUrl = $"https://store.steampowered.com/app/{game.GameId}/";
                storeName = "View on Steam";
            }

            return IsHttps(storeUrl) ? new DiscordButton { Label = storeName, Url = storeUrl } : null;
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
            // a) Extended info
            if (info?.SocialLinks != null && info.SocialLinks.Any())
            {
                foreach (var key in new[] { "Discord", "Reddit", "Forum", "Community" })
                {
                    if (info.SocialLinks.TryGetValue(key, out var url) && IsHttps(url))
                    {
                        var label = key.Equals("Discord", StringComparison.OrdinalIgnoreCase) ? "Join Discord"
                                  : key.Equals("Reddit", StringComparison.OrdinalIgnoreCase) ? "Reddit Community"
                                  : key.Equals("Forum", StringComparison.OrdinalIgnoreCase) ? "Game Forum"
                                  : "Community";
                        return new DiscordButton { Label = label, Url = url };
                    }
                }
            }

            // b) Game.Links
            var communityLink = game.Links?.FirstOrDefault(l =>
                !string.IsNullOrWhiteSpace(l?.Url) &&
                IsHttps(l.Url) &&
                (
                    (l.Name?.IndexOf("discord", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (l.Name?.IndexOf("reddit", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (l.Name?.IndexOf("forum", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (l.Name?.IndexOf("community", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                ));

            return communityLink != null
                ? new DiscordButton
                {
                    Label = string.IsNullOrWhiteSpace(communityLink.Name) ? "Community" : communityLink.Name,
                    Url = communityLink.Url
                }
                : null;
        }


        /// <summary>
        /// Creates a default fallback button
        /// </summary>
        private DiscordButton CreateDefaultButton(Game game)
        {
            var firstHttps = game.Links?.FirstOrDefault(l => IsHttps(l?.Url));
            if (firstHttps != null)
            {
                return new DiscordButton
                {
                    Label = string.IsNullOrWhiteSpace(firstHttps.Name) ? "Game Info" : firstHttps.Name,
                    Url = firstHttps.Url
                };
            }
            var q = Uri.EscapeDataString(game?.Name ?? "game");
            return new DiscordButton
            {
                Label = "Search the game",
                Url = $"https://www.google.com/search?q={q}"
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
