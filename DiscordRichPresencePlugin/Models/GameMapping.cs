using System.Collections.Generic;

namespace DiscordRichPresencePlugin.Models
{
    public class GameMappings
    {
        public string PlayniteLogo { get; set; }
        public List<GameMapping> Games { get; set; }
    }

    public class GameMapping
    {
        public string Name { get; set; }
        public string Image { get; set; }
    }
}