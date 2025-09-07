using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Services;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;


namespace DiscordRichPresencePlugin
{
    public class DiscordRichPresencePlugin : GenericPlugin
    {
        private readonly ILogger logger;
        private readonly DiscordRpcService discordService;
        private readonly GameMappingService mappingService;
        private readonly DiscordRichPresenceSettings settings;
        private readonly TemplateService templateService;
        private readonly ExtendedGameInfoService extendedInfoService;
        private readonly ButtonService buttonService;
        private readonly ImageManagerService imageManager;
        private string currentAppId;

        public override Guid Id { get; } = Guid.Parse("7ad84e05-6c01-4b13-9b12-86af81775396");

        public DiscordRichPresencePlugin(IPlayniteAPI api) : base(api)
        {
            logger = LogManager.GetLogger();
            settings = new DiscordRichPresenceSettings(this);
            Properties = new GenericPluginProperties { HasSettings = true };


            currentAppId = string.IsNullOrWhiteSpace(settings.DiscordAppId)
    ? Constants.DISCORD_APP_ID
    : settings.DiscordAppId.Trim();
            settings.ActiveAppId = currentAppId;
            mappingService = new GameMappingService(GetPluginUserDataPath(), logger);
            templateService = new TemplateService(GetPluginUserDataPath(), logger);
            extendedInfoService = new ExtendedGameInfoService(GetPluginUserDataPath(), logger);
            buttonService = new ButtonService(logger, settings);
            imageManager = new ImageManagerService(PlayniteApi, logger, mappingService, GetPluginUserDataPath());

            discordService = new DiscordRpcService(
                currentAppId,
                logger,
                settings,
                mappingService,
                templateService,
                extendedInfoService,
                buttonService);

            InitializePlugin();
        }

        private void InitializePlugin()
        {
            if (PlayniteApi?.Database?.Games != null)
            {
                PlayniteApi.Database.Games.ItemUpdated += OnGameItemUpdated;
            }

            // Initialize Discord RPC service
            if (settings.EnableRichPresence)
            {
                discordService.Initialize();
            }
        }
        public void ApplyNewDiscordAppId(string newId)
        {
            var target = string.IsNullOrWhiteSpace(newId) ? Constants.DISCORD_APP_ID : newId.Trim();
            if (string.Equals(currentAppId, target, StringComparison.Ordinal))
                return;

            logger.Info($"Discord App ID changed: {currentAppId} -> {target}");
            currentAppId = target;
            discordService?.Reinitialize(currentAppId);
            settings.ActiveAppId = currentAppId;
        }
        // Public method to access mapping service for UI
        public GameMappingService GetGameMappingService() => mappingService;
        public ILogger GetLogger() => logger;

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            if (args?.Game != null && settings.EnableRichPresence)
            {
                discordService.UpdateGamePresence(args.Game);
                extendedInfoService?.StartSession(args.Game.Id);
                var _ = imageManager.PrepareGameImage(args.Game);
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            if (settings.EnableRichPresence)
            {
                extendedInfoService?.EndSession(args.Game.Id);
            }
            discordService.ClearPresence();
        }

        private void OnGameItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            if (!settings.EnableRichPresence) return;

            foreach (var update in e.UpdatedItems)
            {
                if (update.NewData.IsRunning && !update.OldData.IsRunning)
                {
                    discordService.UpdateGamePresence(update.NewData);
                }
                else if (!update.NewData.IsRunning && update.OldData.IsRunning)
                {
                    discordService.ClearPresence();
                }
            }
        }

        /// <summary>
        /// Робить стабільний ключ-синонім: нижній регістр, лише [a-z0-9_], без повторів, з урахуванням колізій.
        /// </summary>
        private string SlugifyImageKey(string name, HashSet<string> existingKeys)
        {
            var slug = System.Text.RegularExpressions.Regex
                .Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_")
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

        private void GenerateAssetsForInstalledGames()
        {
            if (imageManager == null)
            {
                PlayniteApi.Dialogs.ShowErrorMessage("ImageManagerService is not initialized.", "Discord Rich Presence");
                return;
            }

            var games = PlayniteApi.Database.Games
                .Where(g => g.IsInstalled == true)
                .OrderBy(g => g.Name)
                .ToList();

            if (games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("No installed games found.", "Discord Rich Presence");
                return;
            }

            RunMappingAndAssetGeneration(games);
        }

        private void RunMappingAndAssetGeneration(List<Playnite.SDK.Models.Game> games)
        {
            // один прогрес на все: спершу створення mappings, потім — ассети
            var options = new GlobalProgressOptions("Preparing mappings & assets...", true)
            {
                IsIndeterminate = false
            };

            int addedMappings = 0, processed = 0, skippedNoMapping = 0, errors = 0;

            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                try
                {
                    // Оцінимо загальний прогрес: 1 "крок" на фазу mappings + по 1 на кожну гру для ассетів
                    progress.ProgressMaxValue = games.Count + 1;

                    // === ФАЗА 1: створення/додавання відсутніх mappings (у фоні, без UI-контексту) ===
                    progress.Text = "Creating mappings...";
                    try
                    {
                        var existing = mappingService.GetAllMappings();
                        var existingNames = new HashSet<string>(existing.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
                        var existingKeys = new HashSet<string>(existing.Select(m => m.Image ?? ""), StringComparer.OrdinalIgnoreCase);

                        var toAdd = new List<GameMapping>();
                        foreach (var game in games)
                        {
                            if (progress.CancelToken.IsCancellationRequested) break;
                            if (game == null || string.IsNullOrWhiteSpace(game.Name)) continue;
                            if (existingNames.Contains(game.Name)) continue;

                            var key = SlugifyImageKey(game.Name, existingKeys);
                            existingKeys.Add(key);

                            toAdd.Add(new GameMapping
                            {
                                Name = game.Name,
                                Image = key
                            });
                        }

                        if (toAdd.Count > 0)
                        {
                            // ⚠️ важливо: виконуємо на фоні, тому `.GetAwaiter().GetResult()` тут безпечний
                            mappingService.BulkAddOrUpdateMappingsAsync(toAdd).ConfigureAwait(false).GetAwaiter().GetResult();
                            addedMappings = toAdd.Count;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to generate mappings.");
                    }
                    finally
                    {
                        progress.CurrentProgressValue++; // завершили фазу mappings
                    }

                    // === ФАЗА 2: підготовка ассетів ===
                    foreach (var game in games)
                    {
                        if (progress.CancelToken.IsCancellationRequested)
                            break;

                        progress.Text = $"Generating asset: {game.Name}";
                        try
                        {
                            var assetKey = mappingService?.GetImageKeyForGame(game.Name);
                            if (string.IsNullOrWhiteSpace(assetKey))
                            {
                                skippedNoMapping++;
                            }
                            else
                            {
                                imageManager.PrepareGameImage(game);
                                processed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            logger.Error(ex, $"Generate asset failed for {game?.Name}");
                        }
                        finally
                        {
                            progress.CurrentProgressValue++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "RunMappingAndAssetGeneration fatal error.");
                }
            }, options);

            PlayniteApi.Dialogs.ShowMessage(
                $"Done.\nAdded mappings: {addedMappings}\nProcessed assets: {processed}\nNo mapping: {skippedNoMapping}\nErrors: {errors}",
                "Discord Rich Presence");

            imageManager.OpenAssetsFolder();
        }


        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            logger.Debug("GetMainMenuItems called (Discord Rich Presence)");

            return new[]
            {
        new MainMenuItem
        {
            // Top-level підменю під Extensions
            MenuSection = "@Discord Rich Presence",
            Description = "Generate assets for installed games",
            Action = _ => GenerateAssetsForInstalledGames()
        },
        new MainMenuItem
        {
            MenuSection = "@Discord Rich Presence",
            Description = "Open assets folder",
            Action = _ => imageManager?.OpenAssetsFolder()
        }
    };
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            if (PlayniteApi?.Database?.Games != null)
            {
                PlayniteApi.Database.Games.ItemUpdated -= OnGameItemUpdated;
            }
            discordService?.Dispose();
        }

        public override ISettings GetSettings(bool firstRunSettings) => settings;


        public override UserControl GetSettingsView(bool firstRunSettings) => new DiscordRichPresenceSettingsView(imageManager);
    }
}