using Newtonsoft.Json;
using System.Collections.Generic;

namespace DiscordRichPresencePlugin.Models
{
    public class GameMappings
    {
        [JsonProperty("playnite_logo")]
        public string PlayniteLogo { get; set; }

        [JsonProperty("games")] 
        public List<GameMapping> Games { get; set; }
    }

    public class GameMapping
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("image")]
        public string Image { get; set; }
    }
}