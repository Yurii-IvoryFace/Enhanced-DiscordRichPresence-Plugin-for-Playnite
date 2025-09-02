using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscordRichPresencePlugin.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace DiscordRichPresencePlugin.Services
{
    /// <summary>
    /// Service for managing extended game information
    /// </summary>
    public class ExtendedGameInfoService
    {
        private readonly string dataFilePath;
        private readonly ILogger logger;
        private Dictionary<Guid, ExtendedGameInfo> gameInfoCache;
        private readonly object lockObject = new object();

        public ExtendedGameInfoService(string pluginUserDataPath, ILogger logger)
        {
            this.logger = logger;
            dataFilePath = Path.Combine(pluginUserDataPath, "extended_game_info.json");
            LoadData();
        }

        private void LoadData()
        {
            lock (lockObject)
            {
                if (File.Exists(dataFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(dataFilePath);
                        gameInfoCache = Serialization.FromJson<Dictionary<Guid, ExtendedGameInfo>>(json)
                            ?? new Dictionary<Guid, ExtendedGameInfo>();
                        logger?.Debug($"Loaded extended info for {gameInfoCache.Count} games");
                    }
                    catch (Exception ex)
                    {
                        logger?.Error($"Failed to load extended game info: {ex.Message}");
                        gameInfoCache = new Dictionary<Guid, ExtendedGameInfo>();
                    }
                }
                else
                {
                    gameInfoCache = new Dictionary<Guid, ExtendedGameInfo>();
                }
            }
        }

        /// <summary>
        /// Gets or creates extended info for a game
        /// </summary>
        public ExtendedGameInfo GetOrCreateGameInfo(Game game)
        {
            if (game == null) return null;

            lock (lockObject)
            {
                if (gameInfoCache.ContainsKey(game.Id))
                {
                    var existing = gameInfoCache[game.Id];
                    // Update basic info that might have changed
                    UpdateBasicInfo(existing, game);
                    return existing;
                }

                var newInfo = ExtendedGameInfo.FromGame(game);
                gameInfoCache[game.Id] = newInfo;
                SaveData();
                return newInfo;
            }
        }

        /// <summary>
        /// Updates basic information from game object
        /// </summary>
        private void UpdateBasicInfo(ExtendedGameInfo info, Game game)
        {
            if (info == null || game == null) return;

            info.GameName = game.Name;

            // Update completion
            if (game.CompletionStatus != null)
            {
                var newCompletion = EstimateCompletionPercentage(game.CompletionStatus);
                if (newCompletion != info.CompletionPercentage)
                {
                    info.CompletionPercentage = newCompletion;
                    logger?.Debug($"Updated completion for {game.Name}: {newCompletion}%");
                }
            }

            // Update ratings
            if (game.UserScore.HasValue)
                info.UserRating = game.UserScore.Value;
            if (game.CommunityScore.HasValue)
                info.CommunityScore = game.CommunityScore.Value;
            if (game.CriticScore.HasValue)
                info.CriticScore = game.CriticScore.Value;

            info.LastUpdated = DateTime.UtcNow;
        }

        /// <summary>
        /// Updates session start time
        /// </summary>
        public void StartSession(Guid gameId)
        {
            lock (lockObject)
            {
                if (gameInfoCache.ContainsKey(gameId))
                {
                    var info = gameInfoCache[gameId];
                    info.CurrentSessionStart = DateTime.UtcNow;
                    info.SessionCount++;
                    SaveData();
                    logger?.Debug($"Started session #{info.SessionCount} for game {info.GameName}");
                }
            }
        }

        /// <summary>
        /// Updates session statistics on stop
        /// </summary>
        public void EndSession(Guid gameId)
        {
            lock (lockObject)
            {
                if (gameInfoCache.ContainsKey(gameId))
                {
                    var info = gameInfoCache[gameId];
                    info.UpdateSessionStats();
                    SaveData();
                    logger?.Debug($"Ended session for {info.GameName}, avg session: {info.AverageSessionLength}");
                }
            }
        }

        /// <summary>
        /// Updates achievement progress
        /// </summary>
        public void UpdateAchievements(Guid gameId, int earned, int total)
        {
            lock (lockObject)
            {
                if (gameInfoCache.ContainsKey(gameId))
                {
                    var info = gameInfoCache[gameId];
                    info.AchievementsEarned = earned;
                    info.TotalAchievements = total;

                    // Auto-update completion based on achievements if not manually set
                    if (total > 0 && info.CompletionPercentage == 0)
                    {
                        info.CompletionPercentage = (earned * 100) / total;
                    }

                    SaveData();
                    logger?.Debug($"Updated achievements for {info.GameName}: {earned}/{total}");
                }
            }
        }

        /// <summary>
        /// Updates multiplayer information
        /// </summary>
        public void UpdateMultiplayerInfo(Guid gameId, string mode, int? currentPlayers, int? maxPlayers)
        {
            lock (lockObject)
            {
                if (gameInfoCache.ContainsKey(gameId))
                {
                    var info = gameInfoCache[gameId];
                    info.MultiplayerMode = mode;
                    info.CurrentPlayerCount = currentPlayers;
                    info.MaxPlayerCount = maxPlayers;
                    SaveData();
                }
            }
        }

        /// <summary>
        /// Adds or updates a store link
        /// </summary>
        public void AddStoreLink(Guid gameId, string storeName, string url)
        {
            lock (lockObject)
            {
                if (gameInfoCache.ContainsKey(gameId) && !string.IsNullOrEmpty(storeName) && !string.IsNullOrEmpty(url))
                {
                    var info = gameInfoCache[gameId];
                    info.StoreLinks[storeName] = url;
                    SaveData();
                }
            }
        }

        /// <summary>
        /// Adds or updates custom metadata
        /// </summary>
        public void SetCustomMetadata(Guid gameId, string key, string value)
        {
            lock (lockObject)
            {
                if (gameInfoCache.ContainsKey(gameId) && !string.IsNullOrEmpty(key))
                {
                    var info = gameInfoCache[gameId];
                    if (string.IsNullOrEmpty(value))
                        info.CustomMetadata.Remove(key);
                    else
                        info.CustomMetadata[key] = value;
                    SaveData();
                }
            }
        }

        /// <summary>
        /// Gets statistics for all games
        /// </summary>
        public Dictionary<string, object> GetStatistics()
        {
            lock (lockObject)
            {
                var stats = new Dictionary<string, object>
                {
                    ["TotalGames"] = gameInfoCache.Count,
                    ["GamesWithAchievements"] = gameInfoCache.Count(g => g.Value.TotalAchievements > 0),
                    ["CompletedGames"] = gameInfoCache.Count(g => g.Value.CompletionPercentage >= 100),
                    ["MultiplayerGames"] = gameInfoCache.Count(g => g.Value.SupportsMultiplayer),
                    ["TotalSessions"] = gameInfoCache.Sum(g => g.Value.SessionCount),
                    ["AverageCompletion"] = gameInfoCache.Any() ?
                        gameInfoCache.Average(g => g.Value.CompletionPercentage) : 0
                };
                return stats;
            }
        }

        private int EstimateCompletionPercentage(CompletionStatus status)
        {
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

        private void SaveData()
        {
            try
            {
                var json = Serialization.ToJson(gameInfoCache, true);
                File.WriteAllText(dataFilePath, json);
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to save extended game info: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears data for games not in library
        /// </summary>
        public void CleanupOldData(IEnumerable<Guid> currentGameIds)
        {
            lock (lockObject)
            {
                var toRemove = gameInfoCache.Keys.Except(currentGameIds).ToList();
                foreach (var id in toRemove)
                {
                    gameInfoCache.Remove(id);
                }
                if (toRemove.Any())
                {
                    SaveData();
                    logger?.Info($"Cleaned up data for {toRemove.Count} removed games");
                }
            }
        }
    }
}