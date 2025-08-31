using DiscordRichPresencePlugin.Models;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiscordRichPresencePlugin.Services
{
    public class GameMappingService
    {
        private readonly string mappingsFilePath;
        private GameMappings mappings;

        public GameMappingService(string pluginUserDataPath)
        {
            mappingsFilePath = Path.Combine(pluginUserDataPath, "game_mappings.json");
            LoadMappings();
        }

        private void LoadMappings()
        {
            if (File.Exists(mappingsFilePath))
            {
                try
                {
                    mappings = Serialization.FromJsonFile<GameMappings>(mappingsFilePath);
                }
                catch
                {
                    mappings = new GameMappings { Games = new List<GameMapping>() };
                }
            }
            else
            {
                mappings = new GameMappings { Games = new List<GameMapping>() };
            }
        }

        public string GetImageKeyForGame(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return null;

            var mapping = mappings.Games?.FirstOrDefault(g => string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
            return mapping?.Image ?? mappings.PlayniteLogo;
        }

        public void AddOrUpdateMapping(string gameName, string imageKey)
        {
            if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(imageKey))
                return;

            var mapping = mappings.Games.FirstOrDefault(g => string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
            {
                mapping.Image = imageKey;
            }
            else
            {
                mappings.Games.Add(new GameMapping { Name = gameName, Image = imageKey });
            }
            SaveMappings();
        }

        private void SaveMappings()
        {
            // Replace Serialization.ToJsonFile with Serialization.ToJson and File.WriteAllText
            var json = Serialization.ToJson(mappings);
            File.WriteAllText(mappingsFilePath, json);
        }
    }
}
