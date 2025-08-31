using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Playnite.SDK;

namespace DiscordRichPresencePlugin
{
    /// <summary>
    /// Відображає налаштування Discord Rich Presence.
    /// </summary>
    public partial class DiscordRichPresenceSettingsView : UserControl
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI playniteApi;

        public DiscordRichPresenceSettingsView()
        {
            InitializeComponent();
            playniteApi = API.Instance;
        }

        private string GetPluginFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Playnite",
                "ExtensionsData",
                "7ad84e05-6c01-4b13-9b12-86af81775396"
            );
        }

        private void ButtonOpenMappingsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pluginFolder = GetPluginFolderPath();
                if (!Directory.Exists(pluginFolder))
                {
                    Directory.CreateDirectory(pluginFolder);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = pluginFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to open mappings folder: {ex.Message}");
                playniteApi?.Dialogs.ShowErrorMessage(
                    "Не вдалося відкрити папку зі схеми. Будь ласка, перевірте журнали для отримання додаткової інформації.",
                    "Помилка"
                );
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                logger.Error($"Не вдалося відкрити URL-адресу: {ex.Message}");
                playniteApi?.Dialogs.ShowErrorMessage(
                    "Не вдалося відкрити URL-адресу. Будь ласка, перевірте журнали для отримання додаткової інформації.",
                    "Помилка"
                );
            }
        }
    }
}