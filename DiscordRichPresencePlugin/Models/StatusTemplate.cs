using System;
using System.Collections.Generic;
namespace DiscordRichPresencePlugin.Models
{
    /// <summary>
    /// Represents a status template for Discord Rich Presence
    /// </summary>
    public class StatusTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Description { get; set; }

        // formatting
        public string DetailsFormat { get; set; }
        public string StateFormat { get; set; }

        public TemplateConditions Conditions { get; set; } = new TemplateConditions();

        /// <summary>Higher value → higher priority (if you use such logic in TemplateService)</summary>
        public int Priority { get; set; } = 0;

        public bool IsEnabled { get; set; } = true;
        public bool IsUserDefined { get; set; } = false;
    }

    /// <summary>
    /// Conditions for when a template should be applied
    /// </summary>
    public class TemplateConditions
    {
        // minutes (Playnite Playtime is seconds; convert in TemplateService)
        public int? MinPlaytimeMinutes { get; set; }
        public int? MaxPlaytimeMinutes { get; set; }

        // session minutes
        public int? MinSessionTimeMinutes { get; set; }
        public int? MaxSessionTimeMinutes { get; set; }

        public List<string> Genres { get; set; } = new List<string>();
        public List<string> Platforms { get; set; } = new List<string>();
        public List<string> Sources { get; set; } = new List<string>();

        public CompletionRange CompletionPercentage { get; set; }
        public TimeOfDayCondition TimeOfDay { get; set; }

        public List<DayOfWeek> DaysOfWeek { get; set; } = new List<DayOfWeek>();
        public bool? HasMultiplayer { get; set; }
        public bool? HasCoop { get; set; }
    }

    public class CompletionRange
    {
        public int Min { get; set; } = 0;
        public int Max { get; set; } = 100;
    }

    public class TimeOfDayCondition
    {
        public int? StartHour { get; set; }
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
