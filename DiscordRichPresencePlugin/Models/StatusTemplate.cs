using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DiscordRichPresencePlugin.Models
{
    /// <summary>
    /// Represents a status template for Discord Rich Presence
    /// </summary>
    public class StatusTemplate
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("detailsFormat")]
        public string DetailsFormat { get; set; }

        [JsonProperty("stateFormat")]
        public string StateFormat { get; set; }

        [JsonProperty("conditions")]
        public TemplateConditions Conditions { get; set; } = new TemplateConditions();

        [JsonProperty("priority")]
        public int Priority { get; set; } = 0;

        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonProperty("isUserDefined")]
        public bool IsUserDefined { get; set; } = false;
    }

    /// <summary>
    /// Conditions for when a template should be applied
    /// </summary>
    public class TemplateConditions
    {
        [JsonProperty("minPlaytime")]
        public int? MinPlaytimeMinutes { get; set; }

        [JsonProperty("maxPlaytime")]
        public int? MaxPlaytimeMinutes { get; set; }

        [JsonProperty("minSessionTime")]
        public int? MinSessionTimeMinutes { get; set; }

        [JsonProperty("maxSessionTime")]
        public int? MaxSessionTimeMinutes { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; } = new List<string>();

        [JsonProperty("platforms")]
        public List<string> Platforms { get; set; } = new List<string>();

        [JsonProperty("sources")]
        public List<string> Sources { get; set; } = new List<string>();

        [JsonProperty("completionPercentage")]
        public CompletionRange CompletionPercentage { get; set; }

        [JsonProperty("timeOfDay")]
        public TimeOfDayCondition TimeOfDay { get; set; }

        [JsonProperty("hasMultiplayer")]
        public bool? HasMultiplayer { get; set; }

        [JsonProperty("hasCoop")]
        public bool? HasCoop { get; set; }
    }

    public class CompletionRange
    {
        [JsonProperty("min")]
        public int Min { get; set; } = 0;

        [JsonProperty("max")]
        public int Max { get; set; } = 100;
    }

    public class TimeOfDayCondition
    {
        [JsonProperty("startHour")]
        public int? StartHour { get; set; }

        [JsonProperty("endHour")]
        public int? EndHour { get; set; }
    }

    /// <summary>
    /// Collection of available template variables
    /// </summary>
    public static class TemplateVariables
    {
        // Basic game info
        public const string GameName = "{game}";
        public const string Platform = "{platform}";
        public const string Source = "{source}";
        public const string Genre = "{genre}";

        // Time-related
        public const string TotalPlaytime = "{totalPlaytime}";
        public const string SessionTime = "{sessionTime}";
        public const string TimeOfDay = "{timeOfDay}";
        public const string DayOfWeek = "{dayOfWeek}";

        // Progress
        public const string CompletionPercentage = "{completion}";
        public const string AchievementProgress = "{achievements}";
        public const string LastPlayed = "{lastPlayed}";

        // Statistics
        public const string PlayCount = "{playCount}";
        public const string Rating = "{rating}";
        public const string UserScore = "{userScore}";
        public const string CriticScore = "{criticScore}";

        // Multiplayer
        public const string MultiplayerStatus = "{multiplayerStatus}";
        public const string CoopIndicator = "{coopIndicator}";

        // Session mood
        public const string SessionMood = "{mood}";
        public const string SessionPhrase = "{phrase}";
    }
}