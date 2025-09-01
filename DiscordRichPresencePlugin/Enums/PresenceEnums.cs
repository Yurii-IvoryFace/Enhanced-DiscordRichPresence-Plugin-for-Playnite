namespace DiscordRichPresencePlugin.Enums
{
    /// <summary>
    /// Types of presence templates
    /// </summary>
    public enum TemplateType
    {
        Default,
        JustStarted,
        ShortSession,
        LongSession,
        Marathon,
        Achievement,
        Completion,
        Multiplayer,
        Coop,
        Morning,
        Evening,
        Night,
        Weekend,
        Custom
    }

    /// <summary>
    /// Session mood types
    /// </summary>
    public enum SessionMood
    {
        Casual,
        Focused,
        Competitive,
        Relaxed,
        Grinding,
        Exploring,
        Speedrunning,
        Completing,
        Social,
        Learning
    }

    /// <summary>
    /// Button action types
    /// </summary>
    public enum ButtonActionType
    {
        GameInfo,
        StorePage,
        JoinGame,
        ViewAchievements,
        ShareProgress,
        ViewStats,
        Community,
        Custom
    }

    /// <summary>
    /// Display priority for status elements
    /// </summary>
    public enum DisplayPriority
    {
        Critical = 0,
        High = 1,
        Normal = 2,
        Low = 3,
        Optional = 4
    }

    /// <summary>
    /// Time display format
    /// </summary>
    public enum TimeDisplayFormat
    {
        Elapsed,      // Shows time since start
        Remaining,    // Shows estimated time remaining
        Total,        // Shows total playtime
        Session,      // Shows current session time
        None          // No time display
    }

    /// <summary>
    /// Status update triggers
    /// </summary>
    public enum UpdateTrigger
    {
        GameStart,
        GameStop,
        AchievementUnlocked,
        ProgressUpdate,
        TimeInterval,
        Manual,
        SessionMilestone,
        CompletionChange
    }
}