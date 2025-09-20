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
                templates = newTemplates.ToList();
                NormalizePriorities(templates);
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
                        // змерджити по Id/Name
                        var byId = templates.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
                        foreach (var t in incoming)
                        {
                            if (!string.IsNullOrEmpty(t.Id) && byId.ContainsKey(t.Id))
                            {
                                // оновити існуючий
                                var dst = byId[t.Id];
                                CopyInto(dst, t);
                            }
                            else
                            {
                                // спроба по імені
                                var sameName = templates.FirstOrDefault(x =>
                                    !string.IsNullOrEmpty(x.Name) &&
                                    x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase));

                                if (sameName != null)
                                {
                                    CopyInto(sameName, t);
                                }
                                else
                                {
                                    templates.Add(t);
                                }
                            }
                        }
                    }

                    NormalizePriorities(templates);
                    SaveTemplates();
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
                    SaveTemplates(); // створити пустий файл, щоб користувач бачив де він
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
                return null; // Більше НЕ повертаємо дефолт тут

            List<StatusTemplate> snapshot;
            lock (lockObject)
            {
                snapshot = templates.Where(t => t.IsEnabled).ToList();
            }

            var candidates = snapshot
                .Where(t => MatchesConditions(t.Conditions, game, extendedInfo, sessionStart))
                .ToList();

            if (candidates.Count == 0)
                return null; // Ніяких “зашитих” дефолтів — фолбек робимо в DiscordRpcService

            // Чим більш специфічні умови — тим вище; за рівних умов — нижчий Priority кращий (1 найвищий)
            return candidates
                .OrderByDescending(t => GetSpecificityScore(t.Conditions))
                .ThenBy(t => t.Priority)
                .First();
        }


        private static int GetSpecificityScore(TemplateConditions c)
        {
            if (c == null) return 0;
            int score = 0;
            if (c.Platforms != null && c.Platforms.Any()) score += 2;
            if (c.Genres != null && c.Genres.Any()) score += 2;
            if (c.DaysOfWeek != null && c.DaysOfWeek.Any()) score += 1;
            if (c.TimeOfDay != null && (c.TimeOfDay.StartHour.HasValue || c.TimeOfDay.EndHour.HasValue)) score += 1;
            return score;
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

        public void ApplyEnabledSet(IEnumerable<string> enabledIds, bool persist = false)
        {
            if (enabledIds == null) return;
            var set = new HashSet<string>(enabledIds, StringComparer.OrdinalIgnoreCase);

            lock (lockObject)
            {
                foreach (var t in templates)
                {
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

        private static void NormalizePriorities(List<StatusTemplate> list)
        {
            int p = 1;
            foreach (var t in list.OrderBy(x => x.Priority))
            {
                t.Priority = p++;
            }
        }
    }
}
