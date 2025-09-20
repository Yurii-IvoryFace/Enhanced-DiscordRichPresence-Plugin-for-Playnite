using DiscordRichPresencePlugin.Helpers;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace DiscordRichPresencePlugin.Models
{
    /// <summary>
    /// Extended game information for rich presence display
    /// </summary>
    public class ExtendedGameInfo
    {
        public Guid GameId { get; set; }

        public string GameName { get; set; }

        // Progress tracking
        public int CompletionPercentage { get; set; } = 0;

        public int AchievementsEarned { get; set; } = 0;
        public int TotalAchievements { get; set; } = 0;

        // Session statistics
        public DateTime CurrentSessionStart { get; set; }

        public int SessionCount { get; set; } = 0;

        public TimeSpan AverageSessionLength { get; set; }
        public TimeSpan LongestSession { get; set; }

        // Ratings and scores
        public int? UserRating { get; set; }
        public double? CommunityScore { get; set; }
        public int? CriticScore { get; set; }

        // Multiplayer information
        public bool SupportsMultiplayer { get; set; } = false;

        public bool SupportsCoop { get; set; } = false;

        public int? CurrentPlayerCount { get; set; }

  
        public int? MaxPlayerCount { get; set; }


        public string MultiplayerMode { get; set; }

        // Store links
        public Dictionary<string, string> StoreLinks { get; set; } = new Dictionary<string, string>();

        // Social features
        public Dictionary<string, string> SocialLinks { get; set; } = new Dictionary<string, string>();

        // Custom metadata
        public Dictionary<string, string> CustomMetadata { get; set; } = new Dictionary<string, string>();

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
                info.CompletionPercentage = ProgressUtils.EstimateCompletionPercentage(game.CompletionStatus);
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
