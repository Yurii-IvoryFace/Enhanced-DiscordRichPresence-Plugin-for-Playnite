using System;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using DiscordRichPresencePlugin.Services;
using DiscordRichPresencePlugin.Models;

namespace DiscordRichPresencePlugin
{
    public class DiscordRichPresencePlugin : GenericPlugin
    {
        private readonly ILogger logger;
        private readonly DiscordRpcService discordService;
        private readonly GameMappingService mappingService;
        private readonly DiscordRichPresenceSettings settings;

        public override Guid Id { get; } = Guid.Parse("7ad84e05-6c01-4b13-9b12-86af81775396");

        public DiscordRichPresencePlugin(IPlayniteAPI api) : base(api)
        {
            logger = LogManager.GetLogger();
            settings = new DiscordRichPresenceSettings(this);

            Properties = new GenericPluginProperties { HasSettings = true };

            mappingService = new GameMappingService(GetPluginUserDataPath(), logger);
            discordService = new DiscordRpcService(Constants.DISCORD_APP_ID, logger, settings, mappingService);

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

        // Public method to access scanner service for UI

        // Public method to access mapping service for UI
        public GameMappingService GetGameMappingService() => mappingService;

        // Public method to access logger for UI
        public ILogger GetLogger() => logger;

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            if (args?.Game != null && settings.EnableRichPresence)
            {
                discordService.UpdateGamePresence(args.Game);
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            if (settings.EnableRichPresence)
            {
                discordService.ClearPresence();
            }
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

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            if (PlayniteApi?.Database?.Games != null)
            {
                PlayniteApi.Database.Games.ItemUpdated -= OnGameItemUpdated;
            }
            discordService?.Dispose();
        }

        public override ISettings GetSettings(bool firstRunSettings) => settings;

        public override UserControl GetSettingsView(bool firstRunSettings) => new DiscordRichPresenceSettingsView();
    }
}