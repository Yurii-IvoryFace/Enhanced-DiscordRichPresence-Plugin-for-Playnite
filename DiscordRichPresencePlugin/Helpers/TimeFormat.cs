namespace DiscordRichPresencePlugin.Helpers
{
    public static class TimeFormat
    {
        // Outputs "Xh Ym played" або "Ym played"
        public static string FormatPlaytimeSeconds(long totalSeconds)
        {
            var hours = totalSeconds / 3600;
            var minutes = (totalSeconds % 3600) / 60;
            return hours > 0 ? $"{hours}h {minutes}m played" : $"{minutes}m played";
        }
    }
}
