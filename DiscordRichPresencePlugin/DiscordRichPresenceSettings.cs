using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;

namespace DiscordRichPresencePlugin
{
    public class DiscordRichPresenceSettings : ObservableObject, ISettings
    {
        private readonly DiscordRichPresencePlugin plugin;
        private DiscordRichPresenceSettings editingClone;

        #region Properties
        private bool enableRichPresence = true;
        private bool showGenre = true;
        private bool showButtons = false;
        private bool showPlaytime = true;
        private bool showPlatform = true;
        private bool showSource = false;
        private string customStatus = Constants.DEFAULT_STATUS_FORMAT;
        private int updateInterval = Constants.MIN_UPDATE_INTERVAL;
        private bool showElapsedTime = true;
        private string fallbackImageKey = Constants.DEFAULT_FALLBACK_IMAGE;
        private bool useTemplates = false;
        private List<string> enabledTemplateIds = new List<string>();
        private bool showCompletionPercentage = true;
        private bool showAchievements = true;
        private Enums.ButtonDisplayMode buttonMode = Enums.ButtonDisplayMode.Auto;
        private string discordAppId = Constants.DISCORD_APP_ID;
        private string activeAppId;

        public string DiscordAppId
        {
            get => discordAppId;
            set => SetValue(ref discordAppId, value);
        }
        public string ActiveAppId
        {
            get => activeAppId;
            set => SetValue(ref activeAppId, value);
        }
        public bool EnableRichPresence { get => enableRichPresence; set => SetValue(ref enableRichPresence, value); }
        public bool ShowGenre { get => showGenre; set => SetValue(ref showGenre, value); }
        public bool ShowButtons { get => showButtons; set => SetValue(ref showButtons, value); }
        public bool ShowPlaytime { get => showPlaytime; set => SetValue(ref showPlaytime, value); }
        public bool ShowPlatform { get => showPlatform; set => SetValue(ref showPlatform, value); }
        public bool ShowSource { get => showSource; set => SetValue(ref showSource, value); }
        public string CustomStatus { get => customStatus; set => SetValue(ref customStatus, value); }
        public int UpdateInterval
        {
            get => updateInterval;
            set => SetValue(ref updateInterval,
                          value < Constants.MIN_UPDATE_INTERVAL ? Constants.MIN_UPDATE_INTERVAL :
                          value > Constants.MAX_UPDATE_INTERVAL ? Constants.MAX_UPDATE_INTERVAL : value);
        }
        public bool ShowElapsedTime { get => showElapsedTime; set => SetValue(ref showElapsedTime, value); }
        public string FallbackImageKey { get => fallbackImageKey; set => SetValue(ref fallbackImageKey, value); }

        public bool UseTemplates { get => useTemplates; set => SetValue(ref useTemplates, value); }
        public List<string> EnabledTemplateIds { get => enabledTemplateIds; set => SetValue(ref enabledTemplateIds, value); }
        public bool ShowCompletionPercentage { get => showCompletionPercentage; set => SetValue(ref showCompletionPercentage, value); }
        public bool ShowAchievements { get => showAchievements; set => SetValue(ref showAchievements, value); }
        public Enums.ButtonDisplayMode ButtonMode { get => buttonMode; set => SetValue(ref buttonMode, value); }


        #endregion

        #region Constructors
        public DiscordRichPresenceSettings() { }

        public DiscordRichPresenceSettings(DiscordRichPresencePlugin plugin)
        {
            this.plugin = plugin;
            LoadSavedSettings();
        }
        #endregion

        #region ISettings Implementation
        public void BeginEdit() => editingClone = Serialization.GetClone(this);

        public void CancelEdit() => LoadValues(editingClone);

        public void EndEdit()
        {
            var oldId = editingClone?.DiscordAppId?.Trim() ?? string.Empty;
            var newId = DiscordAppId?.Trim() ?? string.Empty;

            SaveSettings(); // will be saved in config.json, because now the property is

            if (!string.Equals(oldId, newId, StringComparison.Ordinal))
            {
                try { plugin?.ApplyNewDiscordAppId(newId); } catch { }
            }
        }
        #endregion

        #region Private Methods

        public void RequestReconnect()
        {
            try { plugin?.ApplyNewDiscordAppId(DiscordAppId?.Trim()); } catch { }
        }
        private void LoadSavedSettings()
        {
            if (plugin == null) return;

            var savedSettings = LoadSettings();
            if (savedSettings != null)
            {
                LoadValues(savedSettings);
            }
        }

        private void LoadValues(DiscordRichPresenceSettings source)
        {
            EnableRichPresence = source.EnableRichPresence;
            ShowGenre = source.ShowGenre;
            ShowButtons = source.ShowButtons;
            ShowPlaytime = source.ShowPlaytime;
            ShowPlatform = source.ShowPlatform;
            ShowSource = source.ShowSource;
            CustomStatus = source.CustomStatus ?? Constants.DEFAULT_STATUS_FORMAT;
            UpdateInterval = source.UpdateInterval;
            ShowElapsedTime = source.ShowElapsedTime;
            FallbackImageKey = source.FallbackImageKey ?? Constants.DEFAULT_FALLBACK_IMAGE;
            UseTemplates = source.UseTemplates;
            EnabledTemplateIds = source.EnabledTemplateIds ?? new List<string>();
            ShowCompletionPercentage = source.ShowCompletionPercentage;
            ShowAchievements = source.ShowAchievements;
            ButtonMode = source.ButtonMode;
            DiscordAppId = string.IsNullOrWhiteSpace(source.DiscordAppId)
        ? Constants.DISCORD_APP_ID
        : source.DiscordAppId;
        }

        private DiscordRichPresenceSettings LoadSettings()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Playnite",
                    "ExtensionsData",
                    plugin.Id.ToString(),
                    "config.json"
                );
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path);
                    return Serialization.FromJson<DiscordRichPresenceSettings>(content);
                }
            }
            catch
            {
                // Ignore errors and return default settings
            }
            return null;
        }

        private void SaveSettings()
        {
            if (plugin == null) return;

            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Playnite",
                    "ExtensionsData",
                    plugin.Id.ToString(),
                    "config.json"
                );

                var directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = Serialization.ToJson(this);
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
        #endregion

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (UpdateInterval < Constants.MIN_UPDATE_INTERVAL || UpdateInterval > Constants.MAX_UPDATE_INTERVAL)
            {
                errors.Add($"Update interval must be between {Constants.MIN_UPDATE_INTERVAL} and {Constants.MAX_UPDATE_INTERVAL} seconds");
            }

            if (string.IsNullOrWhiteSpace(CustomStatus))
            {
                errors.Add("Custom status cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(FallbackImageKey))
            {
                errors.Add("Fallback image key cannot be empty");
            }

            return errors.Count == 0;
        }
    }
}
