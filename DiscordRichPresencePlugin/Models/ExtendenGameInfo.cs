using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Playnite.SDK.Models;

namespace DiscordRichPresencePlugin.Models
{
    /// <summary>
    /// Extended game information for rich presence display
    /// </summary>
    public class ExtendedGameInfo
    {
        [JsonProperty("gameId")]
        public Guid GameId { get; set; }

        [JsonProperty("gameName")]
        public string GameName { get; set; }

        // Progress tracking
        [JsonProperty("completionPercentage")]
        public int CompletionPercentage { get; set; } = 0;

        [JsonProperty("achievementsEarned")]
        public int AchievementsEarned { get; set; } = 0;

        [JsonProperty("totalAchievements")]
        public int TotalAchievements { get; set; } = 0;

        // Session statistics
        [JsonProperty("currentSessionStart")]
        public DateTime CurrentSessionStart { get; set; }

        [JsonProperty("sessionCount")]
        public int SessionCount { get; set; } = 0;

        [JsonProperty("averageSessionLength")]
        public TimeSpan AverageSessionLength { get; set; }

        [JsonProperty("longestSession")]
        public TimeSpan LongestSession { get; set; }

        // Ratings and scores
        [JsonProperty("userRating")]
        public int? UserRating { get; set; }

        [JsonProperty("communityScore")]
        public double? CommunityScore { get; set; }

        [JsonProperty("criticScore")]
        public int? CriticScore { get; set; }

        // Multiplayer information
        [JsonProperty("supportsMultiplayer")]
        public bool SupportsMultiplayer { get; set; } = false;

        [JsonProperty("supportsCoop")]
        public bool SupportsCoop { get; set; } = false;

        [JsonProperty("currentPlayerCount")]
        public int? CurrentPlayerCount { get; set; }

        [JsonProperty("maxPlayerCount")]
        public int? MaxPlayerCount { get; set; }

        [JsonProperty("multiplayerMode")]
        public string MultiplayerMode { get; set; }

        // Store links
        [JsonProperty("storeLinks")]
        public Dictionary<string, string> StoreLinks { get; set; } = new Dictionary<string, string>();

        // Social features
        [JsonProperty("socialLinks")]
        public Dictionary<string, string> SocialLinks { get; set; } = new Dictionary<string, string>();

        // Custom metadata
        [JsonProperty("customMetadata")]
        public Dictionary<string, string> CustomMetadata { get; set; } = new Dictionary<string, string>();

        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Creates ExtendedGameInfo from Playnite Game object
        /// </summary>
        public static ExtendedGameInfo FromGame(Game game)
        {
            if (game == null) return null;

            var info = new ExtendedGameInfo
            {
                GameId = game.Id,
                GameName = game.Name,
                CurrentSessionStart = DateTime.UtcNow
            };

            // Extract basic progress info if available
            if (game.CompletionStatus != null)
            {
                info.CompletionPercentage = EstimateCompletionPercentage(game.CompletionStatus);
            }

            // Extract ratings
            if (game.UserScore.HasValue)
            {
                info.UserRating = game.UserScore.Value;
            }

            if (game.CommunityScore.HasValue)
            {
                info.CommunityScore = game.CommunityScore.Value;
            }

            if (game.CriticScore.HasValue)
            {
                info.CriticScore = game.CriticScore.Value;
            }

            // Extract multiplayer info from features
            if (game.Features != null)
            {
                foreach (var feature in game.Features)
                {
                    var featureName = feature.Name?.ToLower() ?? "";
                    if (featureName.Contains("multiplayer") || featureName.Contains("online"))
                    {
                        info.SupportsMultiplayer = true;
                    }
                    if (featureName.Contains("co-op") || featureName.Contains("coop"))
                    {
                        info.SupportsCoop = true;
                    }
                }
            }

            // Extract links
            if (game.Links != null)
            {
                foreach (var link in game.Links)
                {
                    if (!string.IsNullOrEmpty(link.Name) && !string.IsNullOrEmpty(link.Url))
                    {
                        // Try to categorize links
                        var linkName = link.Name.ToLower();
                        if (linkName.Contains("store") || linkName.Contains("steam") ||
                            linkName.Contains("epic") || linkName.Contains("gog"))
                        {
                            info.StoreLinks[link.Name] = link.Url;
                        }
                        else if (linkName.Contains("forum") || linkName.Contains("discord") ||
                                 linkName.Contains("reddit") || linkName.Contains("twitter"))
                        {
                            info.SocialLinks[link.Name] = link.Url;
                        }
                    }
                }
            }

            return info;
        }

        private static int EstimateCompletionPercentage(CompletionStatus status)
        {
            // Map completion status to percentage
            switch (status.Name?.ToLower())
            {
                case "not played":
                case "unplayed":
                    return 0;
                case "playing":
                case "in progress":
                    return 25;
                case "beaten":
                case "main story":
                    return 75;
                case "completed":
                case "100%":
                case "completionist":
                    return 100;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Updates session statistics
        /// </summary>
        public void UpdateSessionStats()
        {
            var currentSessionLength = DateTime.UtcNow - CurrentSessionStart;

            if (currentSessionLength > LongestSession)
            {
                LongestSession = currentSessionLength;
            }

            // Update average session length (simple moving average)
            if (SessionCount > 0)
            {
                var totalSeconds = (AverageSessionLength.TotalSeconds * SessionCount + currentSessionLength.TotalSeconds) / (SessionCount + 1);
                AverageSessionLength = TimeSpan.FromSeconds(totalSeconds);
            }
            else
            {
                AverageSessionLength = currentSessionLength;
            }

            LastUpdated = DateTime.UtcNow;
        }
    }
}