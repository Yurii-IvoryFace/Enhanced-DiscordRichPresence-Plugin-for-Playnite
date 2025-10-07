using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Win32;
using Playnite.SDK;
using DiscordRichPresencePlugin.Services;
using PluginNS = DiscordRichPresencePlugin;
using EnumsNS = DiscordRichPresencePlugin.Enums;

namespace DiscordRichPresencePlugin_UI
{
    public partial class DiscordRichPresenceSettingsView : UserControl
    {
        private readonly Action openTemplateManagerAction;
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI playniteApi;
        private readonly ImageManagerService imageManager;

        public Array ButtonModes { get; } = Enum.GetValues(typeof(EnumsNS.ButtonDisplayMode));

        private static readonly Regex AppIdStrict = new Regex(@"^[0-9]{17,19}$");
        private static readonly Regex DigitsOnly = new Regex(@"^[0-9]+$");

        public DiscordRichPresenceSettingsView(
    PluginNS.DiscordRichPresenceSettings settings,
    ImageManagerService imageManager,
    Action openTemplateManagerAction)
        {
            InitializeComponent();
            playniteApi = API.Instance;
            this.imageManager = imageManager;
            this.openTemplateManagerAction = openTemplateManagerAction; // ← зберігаємо делегат
            DataContext = settings;
        }

        // обгортки НЕ створюються в рантаймі, але хай будуть для дизайнера
        public DiscordRichPresenceSettingsView(PluginNS.DiscordRichPresenceSettings settings, ImageManagerService imageManager)
            : this(settings, imageManager, null) { }

        public DiscordRichPresenceSettingsView(PluginNS.DiscordRichPresenceSettings settings)
            : this(settings, null, null) { }

        public DiscordRichPresenceSettingsView()
            : this(null, null, null) { }

        // -------- App ID / інші дії --------

        private void Reconnect_Click(object sender, RoutedEventArgs e)
        {
            var settings = this.DataContext as PluginNS.DiscordRichPresenceSettings;
            if (settings == null) return;

            var id = (settings.DiscordAppId ?? string.Empty).Trim();
            if (id.Length > 0 && !AppIdStrict.IsMatch(id))
            {
                API.Instance.Dialogs.ShowErrorMessage(
                    "Invalid Discord Application ID. It must contain 17–19 digits.",
                    "Discord Rich Presence");
                return;
            }

            try { settings.RequestReconnect(); }
            catch (Exception ex) { logger.Error("Reconnect failed: " + ex.Message); }
        }

        private void ResetAppId_Click(object sender, RoutedEventArgs e)
        {
            var settings = this.DataContext as PluginNS.DiscordRichPresenceSettings;
            if (settings == null) return;

            settings.DiscordAppId = PluginNS.Constants.DISCORD_APP_ID;
            try { settings.RequestReconnect(); } catch { }
        }

        // -------- numeric input helpers --------

        private void AppIdTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !DigitsOnly.IsMatch(e.Text);
        }

        private void AppIdTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                e.CancelCommand();
                return;
            }

            var paste = (string)e.DataObject.GetData(DataFormats.UnicodeText);
            var box = sender as TextBox;
            if (box == null) return;

            var proposed = box.Text.Remove(box.SelectionStart, box.SelectionLength)
                                   .Insert(box.SelectionStart, paste);

            if (!Regex.IsMatch(proposed, @"^[0-9]{0,19}$"))
            {
                e.CancelCommand();
            }
        }

        private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) => AppIdTextBox_PreviewTextInput(sender, e);
        private void DigitsOnly_Pasting(object sender, DataObjectPastingEventArgs e) => AppIdTextBox_Pasting(sender, e);

        // -------- папки / посилання --------

        private string GetPluginUserDataPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Playnite", "ExtensionsData",
                "7ad84e05-6c01-4b13-9b12-86af81775396" // якщо інший PluginId — заміни на свій
            );
        }

        private void OpenAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (imageManager != null)
                {
                    imageManager.OpenAssetsFolder();
                }
                else
                {
                    var path = Path.Combine(GetPluginUserDataPath(), "assets");
                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                logger.Error("Failed to open assets folder: " + ex.Message);
                API.Instance.Dialogs.ShowErrorMessage("Failed to open assets folder. See logs for details.", "Discord Rich Presence");
            }
        }

        private void OpenMappingsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = GetPluginUserDataPath();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Error("Failed to open plugin data folder: " + ex.Message);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                logger.Error("Failed to open URL: " + ex.Message);
            }
        }

        // -------- Templates: Open / Export / Import --------

        private string GetTemplatesFilePath()
        {
            return Path.Combine(GetPluginUserDataPath(), "templates.json");
        }

        private void OpenTemplateManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                logger.Info("SettingsView ctor: delegate is " + (openTemplateManagerAction == null ? "NULL" : "SET"));

                if (openTemplateManagerAction != null)
                {
                    openTemplateManagerAction.Invoke();
                }
                else
                {
                    API.Instance.Dialogs.ShowMessage(
                        "Template Manager is not wired from the plugin. Please open it from the main menu.",
                        "Discord Rich Presence");
                }
            }
            catch (Exception ex)
            {
                logger.Error("OpenTemplateManager_Click failed: " + ex);
                API.Instance.Dialogs.ShowErrorMessage(
                    "Failed to open Template Manager. See logs for details.",
                    "Discord Rich Presence");
            }
        }

        private void ExportTemplates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var src = GetTemplatesFilePath();
                if (!File.Exists(src))
                {
                    API.Instance.Dialogs.ShowErrorMessage("No templates file found to export (templates.json).", "Discord Rich Presence");
                    return;
                }

                var sfd = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = "status_templates.json",
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (sfd.ShowDialog() != true) return;

                File.Copy(src, sfd.FileName, true);
                API.Instance.Dialogs.ShowMessage("Templates exported:\n" + sfd.FileName, "Discord Rich Presence");
            }
            catch (Exception ex)
            {
                logger.Error("ExportTemplates_Click failed: " + ex);
                API.Instance.Dialogs.ShowErrorMessage("Failed to export templates. See logs for details.", "Discord Rich Presence");
            }
        }

        private void ImportTemplates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    Multiselect = false
                };
                if (ofd.ShowDialog() != true) return;

                var destDir = GetPluginUserDataPath();
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                var dest = GetTemplatesFilePath();

                // backup існуючого
                if (File.Exists(dest))
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    File.Copy(dest, Path.Combine(destDir, $"status_templates.{stamp}.json.bak"), false);
                }

                File.Copy(ofd.FileName, dest, true);
                API.Instance.Dialogs.ShowMessage("Templates imported:\n" + ofd.FileName, "Discord Rich Presence");
            }
            catch (Exception ex)
            {
                logger.Error("ImportTemplates_Click failed: " + ex);
                API.Instance.Dialogs.ShowErrorMessage("Failed to import templates. See logs for details.", "Discord Rich Presence");
            }
        }
    }
}
