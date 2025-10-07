using DiscordRichPresencePlugin.Services;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO;

using TMNS = DiscordRichPresencePlugin.UI;

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
            mappingService.EnsureMappingsStorage();
            logger.Info($"[DRP] Mappings file: {mappingService.GetMappingsFilePath()}");

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

        public GameMappingService GetGameMappingService() => mappingService;
        public ILogger GetLogger() => logger;

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            if (args?.Game != null && settings.EnableRichPresence)
            {
                discordService.UpdateGamePresence(args.Game);
                extendedInfoService?.StartSession(args.Game.Id);
                // Run image preparation in background to avoid blocking play start event.
                var _ = imageManager.PrepareGameImageAsync(args.Game); // Ensure mapping + asset
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

        private void RunMappingAndAssetGeneration(List<Game> games)
        {
            var options = new GlobalProgressOptions("Preparing mappings & assets...", true)
            {
                IsIndeterminate = false
            };

            int addedMappings = 0, processed = 0, errors = 0;

            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                try
                {
                    progress.ProgressMaxValue = games.Count + 1;

                    // PHASE 1: Centrally via the service
                    progress.Text = "Creating mappings...";
                    try
                    {
                        addedMappings = mappingService.GenerateMissingMappings(games);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to generate mappings.");
                    }
                    finally
                    {
                        progress.CurrentProgressValue++;
                    }

                    // ФАЗА 2: assets
                    foreach (var game in games)
                    {
                        if (progress.CancelToken.IsCancellationRequested)
                            break;

                        progress.Text = $"Generating asset: {game.Name}";
                        try
                        {
                            imageManager.PrepareGameImage(game); // EnsureMapping inside
                            processed++;
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
                $"Done.\nAdded mappings: {addedMappings}\nProcessed assets: {processed}\nErrors: {errors}",
                "Discord Rich Presence");

            imageManager.OpenAssetsFolder();
        }
        public void ShowTemplateManagerWindow()
        {
            try
            {
                var pluginUserDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Playnite",
                    "ExtensionsData",
                    "7ad84e05-6c01-4b13-9b12-86af81775396"
                );

                var logger = LogManager.GetLogger();

                var templateService = new TemplateService(pluginUserDataPath, logger);

                var view = new TMNS.TemplateManagerView();
                var vm = new TMNS.TemplateManagerViewModel(templateService);
                view.DataContext = vm;

                var wnd = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowCloseButton = true,
                    ShowMaximizeButton = true,
                    ShowMinimizeButton = true
                });

                wnd.Title = "Template Manager";
                wnd.Width = 1280;
                wnd.Height = 600;
                wnd.ResizeMode = ResizeMode.CanResize;
                wnd.SizeToContent = SizeToContent.Manual;
                wnd.Owner = null;
                wnd.Content = view;
                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error($"ShowTemplateManagerWindow failed: {ex}");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Failed to open Template Manager. See logs for details.",
                    "Discord Rich Presence");
            }
        }


        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            logger.Debug("GetMainMenuItems called (Discord Rich Presence)");

            return new[]
            {
                new MainMenuItem
                {
                    MenuSection = "@Discord Rich Presence",
                    Description = "Generate assets for installed games",
                    Action = _ => GenerateAssetsForInstalledGames()
                },
                new MainMenuItem
                {
                    MenuSection = "@Discord Rich Presence",
                    Description = "Open assets folder",
                    Action = _ => imageManager?.OpenAssetsFolder()

                },
                new MainMenuItem
                {
                MenuSection = "@Discord Rich Presence",
                Description = "Clean unused assets",
                Action = _ =>
                {
                  var removed = imageManager.CleanupOrphanAssets();
                  PlayniteApi?.Dialogs?.ShowMessage($"Removed {removed} orphan asset file(s).", "Discord Rich Presence");
                  imageManager.OpenAssetsFolder();
                 }
                },
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

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new global::DiscordRichPresencePlugin_UI.DiscordRichPresenceSettingsView(settings, imageManager, ShowTemplateManagerWindow);
        }
    }
}
