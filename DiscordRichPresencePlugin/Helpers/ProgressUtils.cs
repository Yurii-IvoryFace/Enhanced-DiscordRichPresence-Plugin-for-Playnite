using Playnite.SDK.Models;

namespace DiscordRichPresencePlugin.Helpers
{
    public static class ProgressUtils
    {
        public static int EstimateCompletionPercentage(CompletionStatus status)
        {
            switch (status?.Name?.ToLower())
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
    }
}
