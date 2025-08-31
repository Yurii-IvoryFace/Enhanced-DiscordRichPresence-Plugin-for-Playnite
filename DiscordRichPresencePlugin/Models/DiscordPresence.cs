namespace DiscordRichPresencePlugin.Models
{
    public class DiscordPresence
    {
        public string Details { get; set; }
        public string State { get; set; }
        public long StartTimestamp { get; set; }
        public string LargeImageKey { get; set; }
        public string LargeImageText { get; set; }
        public string SmallImageKey { get; set; }
        public string SmallImageText { get; set; }
        public DiscordButton[] Buttons { get; set; }
    }

    public class DiscordButton
    {
        public string Label { get; set; }
        public string Url { get; set; }
    }
}