using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        public IReadOnlyList<StatusTemplate> GetAllTemplates()
        {
            lock (lockObject) return templates.OrderBy(t => t.Priority).ToList();
        }

        /// <summary>
        /// Replace all templates with provided list and persist.
        /// </summary>
        public void ReplaceAllTemplates(IEnumerable<StatusTemplate> newTemplates)
        {
            if (newTemplates == null) return;

            lock (lockObject)
            {
                var list = newTemplates.ToList();

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in list)
                {
                    if (string.IsNullOrWhiteSpace(t.Id) || !seen.Add(t.Id))
                    {
                        t.Id = Guid.NewGuid().ToString();
                        seen.Add(t.Id);
                    }
                }

                templates = list;
                NormalizePriorities(templates);
                SaveTemplatesAsync();
            }
        }

        /// <summary>
        /// Export current templates to a JSON file.
        /// </summary>
        public bool ExportTemplates(string exportPath)
        {
            try
            {
                var json = Serialization.ToJson(GetAllTemplates(), true);
                File.WriteAllText(exportPath, json);
                logger?.Info($"Exported templates to: {exportPath}");
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
                var incoming = Serialization.FromJson<List<StatusTemplate>>(json) ?? new List<StatusTemplate>();

                lock (lockObject)
                {
                    if (!merge)
                    {
                        templates = incoming;
                    }
                    else
                    {
                        var byId = templates.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
                        foreach (var t in incoming)
                        {
                            if (!string.IsNullOrEmpty(t.Id) && byId.ContainsKey(t.Id))
                            {
                                var dst = byId[t.Id];
                                CopyInto(dst, t);
                            }
                            else
                            {
                                var sameName = templates.FirstOrDefault(x =>
                                    !string.IsNullOrEmpty(x.Name) &&
                                    x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase));

                                if (sameName != null)
                                {
                                    CopyInto(sameName, t);
                                }
                                else
                                {
                                    if (string.IsNullOrWhiteSpace(t.Id))
                                        t.Id = Guid.NewGuid().ToString(); // ensure valid id
                                    templates.Add(t);
                                }
                            }
                        }
                    }

                    NormalizePriorities(templates);
                    SaveTemplatesAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to import templates: {ex.Message}");
                return false;
            }
        }

        private static void CopyInto(StatusTemplate dst, StatusTemplate src)
        {
            dst.Name = src.Name;
            dst.Description = src.Description;
            dst.DetailsFormat = src.DetailsFormat;
            dst.StateFormat = src.StateFormat;
            dst.IsEnabled = src.IsEnabled;
            dst.Priority = src.Priority;
            dst.Conditions = src.Conditions;
        }

        /// <summary>
        /// Loads templates from file or creates defaults
        /// </summary>
        private void LoadTemplates()
        {
            try
            {
                if (!File.Exists(templatesFilePath))
                {
                    templates = new List<StatusTemplate>();
                    SaveTemplates();
                    return;
                }

                var json = File.ReadAllText(templatesFilePath);
                var list = Serialization.FromJson<List<StatusTemplate>>(json) ?? new List<StatusTemplate>();
                NormalizePriorities(list);
                lock (lockObject) templates = list;
                logger?.Debug($"Loaded {templates.Count} templates");
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to load templates: {ex.Message}");
                lock (lockObject) templates = new List<StatusTemplate>();
            }
        }

        /// <summary>
        /// Selects the best matching template for current game state
        /// </summary>
        public StatusTemplate SelectTemplate(Game game, ExtendedGameInfo extendedInfo, DateTime sessionStart)
        {
            if (game == null)
                return null;

            List<StatusTemplate> enabled;
            lock (lockObject)
            {
                logger?.Debug($"[Templates] Total templates: {templates.Count}");
                logger?.Debug($"[Templates] Enabled templates: {templates.Count(t => t.IsEnabled)}");
                if (templates.Any())
                {
                    foreach (var t in templates.Take(3))
                    {
                        logger?.Debug($"  - {t.Name}: Enabled={t.IsEnabled}, Priority={t.Priority}");
                    }
                }

                enabled = templates.Where(t => t.IsEnabled).ToList();
            }
            logger?.Debug($"[Templates] Game: {game.Name}, Genres: {string.Join(", ", game.Genres?.Select(g => g.Name) ?? new[] { "none" })}");
            if (enabled.Count == 0)
                return null;

            var candidates = enabled
                .Where(t => MatchesConditions(t.Conditions, game, extendedInfo, sessionStart))
                .ToList();

            if (candidates.Count == 0)
                return null;

            // Найбільш специфічний → далі пріоритет зростання (де 1 — найвищий)
            return candidates
                .OrderByDescending(t => GetSpecificityScore(t.Conditions))
                .ThenBy(t => t.Priority)
                .First();
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
        private static bool MatchesConditions(TemplateConditions c, Game game, ExtendedGameInfo ex, DateTime sessionStart)
        {
            if (c == null) return true;

            bool platOk = true, genreOk = true, hoursOk = true, daysOk = true;

            // Platforms
            if (c.Platforms != null && c.Platforms.Count > 0)
            {
                platOk = (game?.Platforms?.Any(p => c.Platforms.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) == true);
            }
            // Genres
            if (c.Genres != null && c.Genres.Count > 0)
            {
                genreOk = (game?.Genres?.Any(g => c.Genres.Contains(g.Name, StringComparer.OrdinalIgnoreCase)) == true);
            }

            // Time of day (22-2)
            if (c.TimeOfDay != null && (c.TimeOfDay.StartHour.HasValue || c.TimeOfDay.EndHour.HasValue))
            {
                var hour = DateTime.Now.Hour;
                int a = Clamp(c.TimeOfDay.StartHour ?? 0, 0, 23);
                int b = Clamp(c.TimeOfDay.EndHour ?? 23, 0, 23);
                hoursOk = (a <= b) ? (hour >= a && hour <= b) : (hour >= a || hour <= b);
            }

            // Days of week
            if (c.DaysOfWeek != null && c.DaysOfWeek.Count > 0)
            {
                daysOk = c.DaysOfWeek.Contains(DateTime.Now.DayOfWeek);
            }

            return platOk && genreOk && hoursOk && daysOk;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);


        /// <summary>
        /// Formats template string with actual values
        /// </summary>
        public string FormatTemplateString(string template, Game game, ExtendedGameInfo info, DateTime sessionStart)
        {
            if (string.IsNullOrEmpty(template)) return "";

            var result = template;

            // Basic game info
            result = result.Replace(TemplateVariables.GameName, game?.Name ?? "Unknown");
            //result = result.Replace(TemplateVariables.Platform, game?.Platforms?.FirstOrDefault()?.Name ?? "PC");
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

        public void ApplyEnabledSet(IEnumerable<string> enabledIds, bool persist = false)
        {
            if (enabledIds == null || !enabledIds.Any())
            {
                logger?.Debug("[Templates] EnabledIds is empty, keeping templates as-is");
                return;
            }

            var set = new HashSet<string>(enabledIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                                          StringComparer.OrdinalIgnoreCase);

            lock (lockObject)
            {
                foreach (var t in templates)
                {
                    if (string.IsNullOrWhiteSpace(t.Id))
                    {
                        t.Id = Guid.NewGuid().ToString();
                    }
                    t.IsEnabled = set.Contains(t.Id);
                }

                if (persist)
                {
                    SaveTemplates();
                }
            }
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


        private void SaveTemplates()
        {
            try
            {
                var json = Serialization.ToJson(templates.OrderBy(t => t.Priority).ToList(), true);
                File.WriteAllText(templatesFilePath, json);
                logger?.Debug("Templates saved successfully");
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to save templates: {ex.Message}");
            }
        }

        private void SaveTemplatesAsync()
        {
            // Fire-and-forget async save to avoid blocking the caller
            _ = Task.Run(async () =>
            {
                try
                {
                    List<StatusTemplate> toWrite;
                    lock (lockObject)
                    {
                        toWrite = templates.OrderBy(t => t.Priority).ToList();
                    }
                    var json = Serialization.ToJson(toWrite, true);
                    var dir = Path.GetDirectoryName(templatesFilePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    await Helpers.IOAsyncUtils.WriteAllTextAsync(templatesFilePath, json).ConfigureAwait(false);
                    logger?.Debug("Templates saved successfully (async)");
                }
                catch (Exception ex)
                {
                    logger?.Error($"Failed to save templates (async): {ex.Message}");
                }
            });
        }

        private static void NormalizePriorities(List<StatusTemplate> list)
        {
            int p = 1;
            foreach (var t in list.OrderBy(x => x.Priority))
            {
                t.Priority = p++;
            }
            // Normalize priorities to be 1..N based on current order.
            var ordered = list.OrderBy(x => x.Priority).ToList();
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].Priority = i + 1;
        }
    }
}
