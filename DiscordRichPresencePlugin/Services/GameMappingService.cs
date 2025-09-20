using DiscordRichPresencePlugin.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordRichPresencePlugin.Services
{
    public class GameMappingService
    {
        private readonly string mappingsFilePath;
        private readonly string backupDirectory;
        private readonly ILogger logger;
        private GameMappings mappings;
        private readonly object lockObject = new object();

        public GameMappingService(string pluginUserDataPath, ILogger logger = null)
        {
            mappingsFilePath = Path.Combine(pluginUserDataPath, "game_mappings.json");
            backupDirectory = Path.Combine(pluginUserDataPath, "backups");
            this.logger = logger;

            EnsureMappingsStorage();
            LoadMappings();
            EnsureMappingsFileExists();
        }

        private void LoadMappings()
        {
            lock (lockObject)
            {
                if (File.Exists(mappingsFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(mappingsFilePath);
                        mappings = Serialization.FromJson<GameMappings>(json);

                        if (mappings == null)
                        {
                            mappings = new GameMappings { PlayniteLogo = Constants.DEFAULT_FALLBACK_IMAGE, Games = new List<GameMapping>() };
                        }

                        // back-compat для старого ключа playnite_logo
                        if (string.IsNullOrWhiteSpace(mappings.PlayniteLogo))
                        {
                            var compat = Serialization.FromJson<CompatV1>(json);
                            if (!string.IsNullOrWhiteSpace(compat?.playnite_logo))
                            {
                                mappings.PlayniteLogo = compat.playnite_logo;
                            }
                        }

                        if (mappings.Games == null)
                        {
                            mappings.Games = new List<GameMapping>();
                        }

                        if (string.IsNullOrWhiteSpace(mappings.PlayniteLogo))
                        {
                            mappings.PlayniteLogo = Constants.DEFAULT_FALLBACK_IMAGE;
                        }

                        logger?.Debug($"Loaded {mappings.Games.Count} game mappings from file");
                    }
                    catch (Exception ex)
                    {
                        logger?.Error($"Failed to load mappings: {ex.Message}");
                        CreateDefaultMappings();
                        SaveMappings();
                    }
                }
                else
                {
                    CreateDefaultMappings();
                    SaveMappings();
                }
            }
        }

        // DTO лише для читання snake_case поля
        private class CompatV1 { public string playnite_logo { get; set; } }

        public string GetMappingsFilePath() => mappingsFilePath;

        public bool EnsureMappingsStorage(bool createIfMissing = true)

        {
            lock (lockObject)
            {
                try
                {
                    var dataDir = Path.GetDirectoryName(mappingsFilePath);
                    if (string.IsNullOrWhiteSpace(dataDir))
                    {
                        throw new InvalidOperationException("Mappings file path has no directory.");
                    }

                    // Idempotent: CreateDirectory не падає, якщо папка вже існує
                    Directory.CreateDirectory(dataDir);
                    Directory.CreateDirectory(backupDirectory);

                    if (!File.Exists(mappingsFilePath))
                    {
                        if (!createIfMissing)
                        {
                            // повертай false, якщо файл відсутній і ми не маємо його створювати
                            return false;
                        }

                        // Створюємо порожній/дефолтний файл мапінгів
                        SaveMappings();
                        logger?.Info($"Created mappings file at: {mappingsFilePath}");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    logger?.Error($"EnsureMappingsStorage failed: {ex.Message}");
                    return false;
                }
            }
        }

        private void CreateDefaultMappings()
        {
            mappings = new GameMappings
            {
                PlayniteLogo = Constants.DEFAULT_FALLBACK_IMAGE,
                Games = new List<GameMapping>()
            };
            SaveMappings();
        }

        public string GetImageKeyForGame(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return mappings?.PlayniteLogo ?? Constants.DEFAULT_FALLBACK_IMAGE;

            lock (lockObject)
            {
                var mapping = mappings?.Games?.FirstOrDefault(g =>
                    string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));

                return mapping?.Image ?? mappings?.PlayniteLogo ?? Constants.DEFAULT_FALLBACK_IMAGE;
            }
        }

        /// <summary>
        /// Гарантує наявність мапінгу для заданої гри; якщо відсутній — створить slug і збереже.
        /// Повертає ключ зображення.
        /// </summary>
        public string EnsureMappingForName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return Constants.DEFAULT_FALLBACK_IMAGE;

            lock (lockObject)
            {
                var existing = mappings.Games.FirstOrDefault(g =>
                    string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                    return existing.Image;

                var existingKeys = new HashSet<string>(mappings.Games.Select(m => m.Image ?? ""), StringComparer.OrdinalIgnoreCase);
                var key = SlugifyImageKey(gameName, existingKeys);

                mappings.Games.Add(new GameMapping { Name = gameName, Image = key });
                SaveMappings();
                logger?.Debug($"Added mapping: '{gameName}' -> '{key}'");
                return key;
            }
        }

        /// <summary>
        /// Згенерувати відсутні мапінги для набору ігор. Повертає кількість доданих.
        /// </summary>
        public int GenerateMissingMappings(IEnumerable<Game> games)
        {
            if (games == null) return 0;
            EnsureMappingsStorage();

            int added = 0;
            lock (lockObject)
            {
                var existingNames = new HashSet<string>(mappings.Games.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
                var existingKeys = new HashSet<string>(mappings.Games.Select(m => m.Image ?? ""), StringComparer.OrdinalIgnoreCase);

                foreach (var game in games)
                {
                    if (game == null || string.IsNullOrWhiteSpace(game.Name)) continue;
                    if (existingNames.Contains(game.Name)) continue;

                    var key = SlugifyImageKey(game.Name, existingKeys);
                    existingNames.Add(game.Name);
                    existingKeys.Add(key);
                    mappings.Games.Add(new GameMapping { Name = game.Name, Image = key });
                    added++;
                }

                if (added > 0)
                {
                    SaveMappings();
                    logger?.Info($"Generated {added} missing mappings.");
                }
            }

            return added;
        }

        public void AddOrUpdateMapping(string gameName, string imageKey)
        {
            if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(imageKey))
                return;

            lock (lockObject)
            {
                var mapping = mappings.Games.FirstOrDefault(g =>
                    string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));

                if (mapping != null)
                {
                    var oldImageKey = mapping.Image;
                    mapping.Image = imageKey;
                    logger?.Debug($"Updated mapping: '{gameName}' {oldImageKey} -> {imageKey}");
                }
                else
                {
                    mappings.Games.Add(new GameMapping { Name = gameName, Image = imageKey });
                    logger?.Debug($"Added mapping: '{gameName}' -> {imageKey}");
                }
            }

            SaveMappings();
        }

        public void RemoveMapping(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return;

            lock (lockObject)
            {
                var mapping = mappings.Games.FirstOrDefault(g =>
                    string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));

                if (mapping != null)
                {
                    mappings.Games.Remove(mapping);
                    logger?.Debug($"Removed mapping for '{gameName}'");
                    SaveMappings();
                }
            }
        }

        public async Task BulkAddOrUpdateMappingsAsync(IEnumerable<GameMapping> newMappings)
        {
            if (newMappings?.Any() != true)
                return;

            await Task.Run(() =>
            {
                lock (lockObject)
                {
                    foreach (var newMapping in newMappings)
                    {
                        if (string.IsNullOrWhiteSpace(newMapping.Name) ||
                            string.IsNullOrWhiteSpace(newMapping.Image))
                            continue;

                        var existing = mappings.Games.FirstOrDefault(g =>
                            string.Equals(g.Name, newMapping.Name, StringComparison.OrdinalIgnoreCase));

                        if (existing != null)
                        {
                            existing.Image = newMapping.Image;
                        }
                        else
                        {
                            mappings.Games.Add(new GameMapping
                            {
                                Name = newMapping.Name,
                                Image = newMapping.Image
                            });
                        }
                    }
                }

                SaveMappings();
                logger?.Info($"Bulk updated {newMappings.Count()} mappings");
            });
        }

        public List<GameMapping> GetAllMappings()
        {
            lock (lockObject)
            {
                return mappings?.Games?.ToList() ?? new List<GameMapping>();
            }
        }

        public int GetMappingsCount()
        {
            lock (lockObject)
            {
                return mappings?.Games?.Count ?? 0;
            }
        }

        public bool HasMapping(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return false;

            lock (lockObject)
            {
                return mappings?.Games?.Any(g =>
                    string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase)) ?? false;
            }
        }

        public async Task<bool> ExportMappingsAsync(string exportPath)
        {
            try
            {
                await Task.Run(() =>
                {
                    lock (lockObject)
                    {
                        var json = Serialization.ToJson(mappings, true);
                        File.WriteAllText(exportPath, json);
                    }
                });

                logger?.Info($"Exported mappings to: {exportPath}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to export mappings: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ImportMappingsAsync(string importPath, bool overwriteExisting = false)
        {
            try
            {
                if (!File.Exists(importPath))
                {
                    logger?.Error($"Import file not found: {importPath}");
                    return false;
                }

                var importedMappings = await Task.Run(() =>
                {
                    var json = File.ReadAllText(importPath);
                    return Serialization.FromJson<GameMappings>(json);
                });

                if (importedMappings?.Games == null)
                {
                    logger?.Error("Invalid mappings file format");
                    return false;
                }

                await CreateBackupAsync();

                lock (lockObject)
                {
                    if (overwriteExisting)
                    {
                        mappings = importedMappings;
                    }
                    else
                    {
                        // Merge
                        foreach (var importedMapping in importedMappings.Games)
                        {
                            var existing = mappings.Games.FirstOrDefault(g =>
                                string.Equals(g.Name, importedMapping.Name, StringComparison.OrdinalIgnoreCase));

                            if (existing == null)
                            {
                                mappings.Games.Add(importedMapping);
                            }
                        }
                    }
                }

                SaveMappings();
                logger?.Info($"Imported {importedMappings.Games.Count} mappings from: {importPath}");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to import mappings: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CreateBackupAsync()
        {
            try
            {
                var backupFileName = $"game_mappings_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var backupPath = Path.Combine(backupDirectory, backupFileName);

                return await ExportMappingsAsync(backupPath);
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to create backup: {ex.Message}");
                return false;
            }
        }

        public List<string> GetBackupFiles()
        {
            try
            {
                if (!Directory.Exists(backupDirectory))
                    return new List<string>();

                return Directory.GetFiles(backupDirectory, "*.json")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to get backup files: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task CleanupOldBackupsAsync(int keepCount = 5)
        {
            try
            {
                var backupFiles = GetBackupFiles();
                if (backupFiles.Count <= keepCount)
                    return;

                var filesToDelete = backupFiles.Skip(keepCount);

                await Task.Run(() =>
                {
                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            File.Delete(file);
                            logger?.Debug($"Deleted old backup: {Path.GetFileName(file)}");
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn($"Failed to delete backup {file}: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to cleanup old backups: {ex.Message}");
            }
        }

        private void SaveMappings()
        {
            try
            {
                lock (lockObject)
                {
                    var json = Serialization.ToJson(mappings, true);
                    File.WriteAllText(mappingsFilePath, json);
                }

                logger?.Debug("Game mappings saved successfully");
            }
            catch (Exception ex)
            {
                logger?.Error($"Failed to save mappings: {ex.Message}");
            }
        }

        // ---- helpers ----

        private static string SlugifyImageKey(string name, HashSet<string> existingKeys)
        {
            // нижній регістр, лише [a-z0-9_], обрізання до 64, унікалізація
            var slug = System.Text.RegularExpressions.Regex
                .Replace((name ?? "").ToLowerInvariant(), "[^a-z0-9]+", "_")
                .Trim('_');
            if (string.IsNullOrWhiteSpace(slug))
                slug = "game";

            if (slug.Length > 64)
                slug = slug.Substring(0, 64).Trim('_');

            var baseSlug = slug;
            var i = 1;
            while (existingKeys.Contains(slug))
            {
                slug = $"{baseSlug}_{i++}";
                if (slug.Length > 64)
                {
                    var trimLen = Math.Max(0, 64 - ("_" + i.ToString()).Length);
                    baseSlug = baseSlug.Length > trimLen ? baseSlug.Substring(0, trimLen).Trim('_') : baseSlug;
                    slug = $"{baseSlug}_{i}";
                }
            }
            return slug;
        }

        public class MappingStatistics
        {
            public int TotalMappings { get; set; }
            public int CustomImageMappings { get; set; }
            public int DefaultImageMappings { get; set; }
            public DateTime LastUpdated { get; set; } = DateTime.Now;
        }
    }
}
