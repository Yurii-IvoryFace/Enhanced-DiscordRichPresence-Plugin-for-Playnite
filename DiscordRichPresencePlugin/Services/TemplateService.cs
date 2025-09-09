using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Enums;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace DiscordRichPresencePlugin.Services
{
    /// <summary>
    /// Service for managing and applying status templates
    /// </summary>
    public class TemplateService
    {
        private readonly string templatesFilePath;
        private readonly ILogger logger;
        private List<StatusTemplate> templates;
        private readonly object lockObject = new object();
        private readonly Random random = new Random();

        // Predefined template phrases for different moods
        private readonly Dictionary<SessionMood, string[]> moodPhrases = new Dictionary<SessionMood, string[]>
        {
            [SessionMood.Casual] = new[] { "Chilling with", "Enjoying", "Playing" },
            [SessionMood.Focused] = new[] { "Mastering", "Focused on", "Deep into" },
            [SessionMood.Competitive] = new[] { "Competing in", "Ranking up in", "Dominating" },
            [SessionMood.Grinding] = new[] { "Grinding", "Farming in", "Working through" },
            [SessionMood.Exploring] = new[] { "Exploring", "Discovering", "Wandering through" },
            [SessionMood.Completing] = new[] { "Completing", "Finishing up", "100%'ing" }
        };

        public TemplateService(string pluginUserDataPath, ILogger logger)
        {
            this.logger = logger;
            templatesFilePath = Path.Combine(pluginUserDataPath, "status_templates.json");
            LoadTemplates();
        }

        /// <summary>
        /// Replace all templates with provided list and persist.
        /// </summary>
        public void ReplaceAllTemplates(IEnumerable<StatusTemplate> newTemplates)
        {
            if (newTemplates == null) return;
            lock (lockObject)
            {
                templates = newTemplates.ToList();
                SaveTemplates();
            }
        }

        /// <summary>
        /// Export current templates to a JSON file.
        /// </summary>
        public bool ExportTemplates(string exportPath)
        {
            try
            {
                List<StatusTemplate> snapshot;
                lock (lockObject)
                {
                    snapshot = templates.ToList();
                }
                var json = Playnite.SDK.Data.Serialization.ToJson(snapshot, true);
                File.WriteAllText(exportPath, json);
                logger?.Info($"Exported {snapshot.Count} templates to: {exportPath}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to export templates: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Import templates from JSON file. If merge=true, merge by Id or Name; otherwise replace all.
        /// </summary>
        public bool ImportTemplates(string importPath, bool merge = true)
        {
            try
            {
                if (!File.Exists(importPath)) return false;
                var json = File.ReadAllText(importPath);
                var incoming = Playnite.SDK.Data.Serialization.FromJson<List<StatusTemplate>>(json) ?? new List<StatusTemplate>();

                lock (lockObject)
                {
                    if (!merge)
                    {
                        templates = incoming;
                    }
                    else
                    {
                        // merge by Id, fallback by Name
                        foreach (var t in incoming)
                        {
                            var existing = templates.FirstOrDefault(x => x.Id == t.Id)
                                           ?? templates.FirstOrDefault(x => !string.IsNullOrEmpty(x.Name) &&
                                                                            string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));
                            if (existing == null)
                            {
                                templates.Add(t);
                            }
                            else
                            {
                                // update fields
                                existing.Name = t.Name;
                                existing.Description = t.Description;
                                existing.DetailsFormat = t.DetailsFormat;
                                existing.StateFormat = t.StateFormat;
                                existing.Priority = t.Priority;
                                existing.IsEnabled = t.IsEnabled;
                                existing.Conditions = t.Conditions ?? existing.Conditions;
                            }
                        }
                    }

                    SaveTemplates();
                }

                logger?.Info($"Imported {incoming.Count} templates from: {importPath}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to import templates: {ex.Message}");
                return false;
            }
        }



        /// <summary>
        /// Loads templates from file or creates defaults
        /// </summary>
        private void LoadTemplates()
        {
            lock (lockObject)
            {
                if (File.Exists(templatesFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(templatesFilePath);
                        templates = Serialization.FromJson<List<StatusTemplate>>(json) ?? new List<StatusTemplate>();
                        logger?.Debug($"Loaded {templates.Count} templates");
                    }
                    catch (Exception ex)
                    {
                        logger?.Error($"Failed to load templates: {ex.Message}");
                        CreateDefaultTemplates();
                    }
                }
                else
                {
                    CreateDefaultTemplates();
                }
            }
        }

        /// <summary>
        /// Creates default built-in templates
        /// </summary>
        private void CreateDefaultTemplates()
        {
            templates = new List<StatusTemplate>
            {
                // Just Started template
                new StatusTemplate
                {
                    Name = "Just Started",
                    Description = "Shows when just starting a game",
                    DetailsFormat = "Just started {game}",
                    StateFormat = "Getting ready | {platform}",
                    Priority = 1,
                    Conditions = new TemplateConditions
                    {
                        MaxSessionTimeMinutes = 5
                    }
                },

                // Short Session template
                new StatusTemplate
                {
                    Name = "Short Session",
                    Description = "For brief gaming sessions",
                    DetailsFormat = "Playing {game}",
                    StateFormat = "{genre} | Quick session",
                    Priority = 2,
                    Conditions = new TemplateConditions
                    {
                        MinSessionTimeMinutes = 5,
                        MaxSessionTimeMinutes = 30
                    }
                },

                // Long Session template
                new StatusTemplate
                {
                    Name = "Long Session",
                    Description = "For extended play sessions",
                    DetailsFormat = "{phrase} {game}",
                    StateFormat = "{sessionTime} | {completion}% complete",
                    Priority = 2,
                    Conditions = new TemplateConditions
                    {
                        MinSessionTimeMinutes = 30,
                        MaxSessionTimeMinutes = 180
                    }
                },

                // Marathon template
                new StatusTemplate
                {
                    Name = "Marathon",
                    Description = "For very long sessions",
                    DetailsFormat = "Marathon session: {game}",
                    StateFormat = "Playing for {sessionTime} | {mood}",
                    Priority = 1,
                    Conditions = new TemplateConditions
                    {
                        MinSessionTimeMinutes = 180
                    }
                },

                // Achievement Hunter template
                new StatusTemplate
                {
                    Name = "Achievement Hunter",
                    Description = "Focus on achievements",
                    DetailsFormat = "Hunting achievements in {game}",
                    StateFormat = "{achievements} | {completion}% complete",
                    Priority = 3,
                    Conditions = new TemplateConditions
                    {
                        CompletionPercentage = new CompletionRange { Min = 50, Max = 99 }
                    }
                },

                // Completionist template
                new StatusTemplate
                {
                    Name = "Completionist",
                    Description = "Near or at 100% completion",
                    DetailsFormat = "Completing {game}",
                    StateFormat = "🏆 {completion}% | {totalPlaytime}",
                    Priority = 1,
                    Conditions = new TemplateConditions
                    {
                        CompletionPercentage = new CompletionRange { Min = 90, Max = 100 }
                    }
                },

                // Multiplayer template
                new StatusTemplate
                {
                    Name = "Multiplayer",
                    Description = "For multiplayer games",
                    DetailsFormat = "{multiplayerStatus} {game}",
                    StateFormat = "Online | {platform}",
                    Priority = 2,
                    Conditions = new TemplateConditions
                    {
                        HasMultiplayer = true
                    }
                },

                // Co-op template
                new StatusTemplate
                {
                    Name = "Co-op",
                    Description = "For cooperative games",
                    DetailsFormat = "Co-op: {game}",
                    StateFormat = "{coopIndicator} | {sessionTime}",
                    Priority = 2,
                    Conditions = new TemplateConditions
                    {
                        HasCoop = true
                    }
                },

                // Night Gaming template
                new StatusTemplate
                {
                    Name = "Night Owl",
                    Description = "Late night gaming",
                    DetailsFormat = "Late night {game}",
                    StateFormat = "🌙 {timeOfDay} gaming",
                    Priority = 3,
                    Conditions = new TemplateConditions
                    {
                        TimeOfDay = new TimeOfDayCondition { StartHour = 22, EndHour = 4 }
                    }
                },

                // Weekend template
                new StatusTemplate
                {
                    Name = "Weekend Warrior",
                    Description = "Weekend gaming sessions",
                    DetailsFormat = "Weekend gaming: {game}",
                    StateFormat = "{dayOfWeek} | {sessionTime}",
                    Priority = 4,
                    Conditions = new TemplateConditions()
                }
            };

            SaveTemplates();
        }

        /// <summary>
        /// Selects the best matching template for current game state
        /// </summary>
        public StatusTemplate SelectTemplate(Game game, ExtendedGameInfo extendedInfo, DateTime sessionStart)
        {
            if (game == null) return GetDefaultTemplate();

            lock (lockObject)
            {
                var enabledTemplates = templates.Where(t => t.IsEnabled).ToList();
                var matchingTemplates = new List<StatusTemplate>();

                foreach (var template in enabledTemplates)
                {
                    if (MatchesConditions(template.Conditions, game, extendedInfo, sessionStart))
                    {
                        matchingTemplates.Add(template);
                    }
                }

                if (matchingTemplates.Any())
                {
                    return matchingTemplates
                        .OrderByDescending(t => t.Priority)
                        .ThenByDescending(t => GetSpecificityScore(t.Conditions))
                        .First();
                }

                return GetDefaultTemplate();
            }
        }

        private static int GetSpecificityScore(TemplateConditions c)
        {
            if (c == null) return 0;
            int s = 0;
            if (c.MinPlaytimeMinutes.HasValue || c.MaxPlaytimeMinutes.HasValue) s++;
            if (c.MinSessionTimeMinutes.HasValue || c.MaxSessionTimeMinutes.HasValue) s++;
            if (c.Genres?.Count > 0) s += 2;
            if (c.Platforms?.Count > 0) s += 2;
            if (c.Sources?.Count > 0) s++;
            if (c.CompletionPercentage != null) s++;
            if (c.TimeOfDay?.StartHour != null || c.TimeOfDay?.EndHour != null) s++;
            if (c.HasMultiplayer.HasValue) s++;
            if (c.HasCoop.HasValue) s++;
            return s;
        }


        /// <summary>
        /// Checks if conditions match current game state
        /// </summary>
        private bool MatchesConditions(TemplateConditions conditions, Game game, ExtendedGameInfo info, DateTime sessionStart)
        {
            if (conditions == null) return true;

            var sessionMinutes = (int)(DateTime.UtcNow - sessionStart).TotalMinutes;

            // session time
            if (conditions.MinSessionTimeMinutes.HasValue && sessionMinutes < conditions.MinSessionTimeMinutes) return false;
            if (conditions.MaxSessionTimeMinutes.HasValue && sessionMinutes > conditions.MaxSessionTimeMinutes) return false;

            // total playtime (seconds → minutes)
            var totalMinutes = (int)((game?.Playtime ?? 0) / 60);
            if (conditions.MinPlaytimeMinutes.HasValue && totalMinutes < conditions.MinPlaytimeMinutes.Value) return false;
            if (conditions.MaxPlaytimeMinutes.HasValue && totalMinutes > conditions.MaxPlaytimeMinutes.Value) return false;

            // completion
            if (conditions.CompletionPercentage != null && info != null)
            {
                if (info.CompletionPercentage < conditions.CompletionPercentage.Min ||
                    info.CompletionPercentage > conditions.CompletionPercentage.Max)
                    return false;
            }

            // time of day
            if (conditions.TimeOfDay?.StartHour != null)
            {
                var currentHour = DateTime.Now.Hour;
                var start = conditions.TimeOfDay.StartHour.Value;
                var end = conditions.TimeOfDay.EndHour ?? start;

                bool inRange = start <= end
                    ? (currentHour >= start && currentHour <= end)
                    : (currentHour >= start || currentHour <= end);

                if (!inRange) return false;
            }

            // genres
            if (conditions.Genres?.Any() == true)
            {
                if (game.Genres?.Any() == true)
                {
                    if (!game.Genres.Any(g => conditions.Genres.Contains(g.Name, StringComparer.OrdinalIgnoreCase)))
                        return false;
                }
                else return false;
            }

            // platforms
            if (conditions.Platforms?.Any() == true)
            {
                if (game.Platforms?.Any() == true)
                {
                    if (!game.Platforms.Any(p => conditions.Platforms.Contains(p.Name, StringComparer.OrdinalIgnoreCase)))
                        return false;
                }
                else return false;
            }

            // multiplayer/coop
            if (conditions.HasMultiplayer.HasValue && info != null)
            {
                if (conditions.HasMultiplayer.Value != info.SupportsMultiplayer)
                    return false;
            }
            if (conditions.HasCoop.HasValue && info != null)
            {
                if (conditions.HasCoop.Value != info.SupportsCoop)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Formats template string with actual values
        /// </summary>
        public string FormatTemplateString(string template, Game game, ExtendedGameInfo info, DateTime sessionStart)
        {
            if (string.IsNullOrEmpty(template)) return "";

            var result = template;

            // Basic game info
            result = result.Replace(TemplateVariables.GameName, game?.Name ?? "Unknown");
            result = result.Replace(TemplateVariables.Platform, game?.Platforms?.FirstOrDefault()?.Name ?? "PC");
            result = result.Replace(TemplateVariables.Source, game?.Source?.Name ?? "");
            result = result.Replace(TemplateVariables.Genre, game?.Genres?.FirstOrDefault()?.Name ?? "");

            // Time-related
            var sessionTime = DateTime.UtcNow - sessionStart;
            result = result.Replace(TemplateVariables.SessionTime, FormatTimeSpan(sessionTime));
            result = result.Replace(TemplateVariables.TotalPlaytime, FormatPlaytimeFromSeconds((long)(game?.Playtime ?? 0)));
            result = result.Replace(TemplateVariables.TimeOfDay, GetTimeOfDay());
            result = result.Replace(TemplateVariables.DayOfWeek, DateTime.Now.DayOfWeek.ToString());

            // Progress
            if (info != null)
            {
                result = result.Replace(TemplateVariables.CompletionPercentage, info.CompletionPercentage.ToString());
                result = result.Replace(TemplateVariables.AchievementProgress,
                    $"{info.AchievementsEarned}/{info.TotalAchievements}");

                // Ratings
                result = result.Replace(TemplateVariables.Rating, info.UserRating?.ToString() ?? "");
                result = result.Replace(TemplateVariables.UserScore, info.UserRating?.ToString() ?? "");
                result = result.Replace(TemplateVariables.CriticScore, info.CriticScore?.ToString() ?? "");

                // Multiplayer
                result = result.Replace(TemplateVariables.MultiplayerStatus,
                    info.SupportsMultiplayer ? "Playing online" : "Playing solo");
                result = result.Replace(TemplateVariables.CoopIndicator,
                    info.SupportsCoop ? "Co-op mode" : "Single player");
            }

            // Mood and phrases
            var mood = GetSessionMood(sessionTime);
            result = result.Replace(TemplateVariables.SessionMood, mood.ToString());
            result = result.Replace(TemplateVariables.SessionPhrase, GetMoodPhrase(mood));

            return result;
        }

        private SessionMood GetSessionMood(TimeSpan sessionTime)
        {
            if (sessionTime.TotalMinutes < 30) return SessionMood.Casual;
            if (sessionTime.TotalHours < 2) return SessionMood.Focused;
            if (sessionTime.TotalHours < 4) return SessionMood.Grinding;
            return SessionMood.Competitive;
        }

        private string GetMoodPhrase(SessionMood mood)
        {
            if (moodPhrases.ContainsKey(mood))
            {
                var phrases = moodPhrases[mood];
                return phrases[random.Next(phrases.Length)];
            }
            return "Playing";
        }

        private string FormatTimeSpan(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return $"{(int)time.TotalHours}h {time.Minutes}m";
            return $"{time.Minutes}m";
        }

        private string FormatPlaytimeFromSeconds(long seconds)
        {
            if (seconds <= 0) return "0m";
           var hours = seconds / 3600;
           var minutes = (seconds % 3600) / 60;
            return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        }

        private string GetTimeOfDay()
        {
            var hour = DateTime.Now.Hour;
            if (hour >= 5 && hour < 12) return "Morning";
            if (hour >= 12 && hour < 17) return "Afternoon";
            if (hour >= 17 && hour < 21) return "Evening";
            return "Night";
        }

        private StatusTemplate GetDefaultTemplate()
        {
            return new StatusTemplate
            {
                Name = "Default",
                DetailsFormat = "Playing {game}",
                StateFormat = "{platform} | {genre}"
            };
        }

        /// <summary>
        /// Adds a custom user template
        /// </summary>
        public void AddCustomTemplate(StatusTemplate template)
        {
            if (template == null) return;

            lock (lockObject)
            {
                template.IsUserDefined = true;
                templates.Add(template);
                SaveTemplates();
            }
        }

        /// <summary>
        /// Removes a template by ID
        /// </summary>
        public void RemoveTemplate(string templateId)
        {
            lock (lockObject)
            {
                templates.RemoveAll(t => t.Id == templateId && t.IsUserDefined);
                SaveTemplates();
            }
        }

        /// <summary>
        /// Gets all templates
        /// </summary>
        public List<StatusTemplate> GetAllTemplates()
        {
            lock (lockObject)
            {
                return templates.ToList();
            }
        }

        private void SaveTemplates()
        {
            try
            {
                var json = Serialization.ToJson(templates, true);
                File.WriteAllText(templatesFilePath, json);
                logger?.Debug("Templates saved successfully");
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to save templates: {ex.Message}");
            }
        }
    }
}