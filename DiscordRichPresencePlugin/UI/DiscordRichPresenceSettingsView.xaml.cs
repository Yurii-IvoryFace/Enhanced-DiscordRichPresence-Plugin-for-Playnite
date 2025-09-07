using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Playnite.SDK;
using System.Windows.Media;
using DiscordRichPresencePlugin.Services;
using DiscordRichPresencePlugin.Views;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace DiscordRichPresencePlugin
{
    /// <summary>
    /// Відображає налаштування Discord Rich Presence.
    /// </summary>
    public partial class DiscordRichPresenceSettingsView : UserControl
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI playniteApi;
        private readonly ImageManagerService imageManager;
        private static readonly Regex AppIdStrict = new Regex(@"^[0-9]{17,19}$");
        private static readonly Regex DigitsOnly = new Regex(@"^[0-9]+$");

        public DiscordRichPresenceSettingsView()
        {
            InitializeComponent();
            playniteApi = API.Instance;
            this.Loaded += OnLoadedAttachTemplateManager;
        }
        public DiscordRichPresenceSettingsView(ImageManagerService imageManager) : this()
        {
            this.imageManager = imageManager;
        }


        private void Reconnect_Click(object sender, RoutedEventArgs e)
        {
            var s = DataContext as DiscordRichPresenceSettings;
            if (s == null) return;

            var id = (s.DiscordAppId ?? string.Empty).Trim();
            if (!AppIdStrict.IsMatch(id))
            {
                API.Instance?.Dialogs?.ShowErrorMessage(
                    "Invalid App ID. It must be 17–19 digits.", "Discord Rich Presence");
                return;
            }

            s.RequestReconnect(); // викликає plugin.ApplyNewDiscordAppId(...)
        }
        private void CopyActiveId_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DiscordRichPresenceSettings s && !string.IsNullOrWhiteSpace(s.ActiveAppId))
            {
                Clipboard.SetText(s.ActiveAppId);
            }
        }

        private void AppIdBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // дозволяємо вводити лише цифри
            e.Handled = !DigitsOnly.IsMatch(e.Text);
        }

        private void AppIdBox_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            // блокуємо пастинг не-цифр і довжину > 19
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                e.CancelCommand();
                return;
            }

            var paste = (string)e.DataObject.GetData(DataFormats.UnicodeText);
            var box = (TextBox)sender;

            var proposed = box.Text.Remove(box.SelectionStart, box.SelectionLength)
                                   .Insert(box.SelectionStart, paste);

            if (!Regex.IsMatch(proposed, @"^[0-9]{0,19}$"))
            {
                e.CancelCommand();
            }
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

        private void OnLoadedAttachTemplateManager(object sender, RoutedEventArgs e)
        {
            try
            {
                // шукаємо кнопку за Tag="templateManager", щоб не міняти XAML
                var btn = FindChild<Button>(this, b => (b.Tag as string) == "templateManager");
                if (btn != null)
                {
                    btn.Click -= TemplateManagerButton_Click;
                    btn.Click += TemplateManagerButton_Click;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to wire Template Manager button: {ex.Message}");
            }
        }

        private void TemplateManagerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pluginFolder = GetPluginFolderPath();
                var templateService = new TemplateService(pluginFolder, logger);

                var view = new TemplateManagerView();
                var vm = new TemplateManagerViewModel(templateService);
                view.DataContext = vm;

                // У options залишаємо тільки те, що підтримується
                var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowCloseButton = true,
                    ShowMaximizeButton = true,
                    ShowMinimizeButton = true
                });

                // Налаштовуємо властивості ВЖЕ створеного вікна
                window.Title = "Менеджер шаблонів";
                window.Width = 900;
                window.Height = 600;
                window.ResizeMode = ResizeMode.CanResize;
                window.SizeToContent = SizeToContent.Manual;
                window.Owner = Window.GetWindow(this);
                window.Content = view;

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to open Template Manager: {ex.Message}");
                playniteApi?.Dialogs?.ShowErrorMessage(
                    "Не вдалося відкрити менеджер шаблонів. Перевірте журнали.",
                    "Помилка");
            }
        }

        private void ButtonOpenAssets_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (imageManager != null)
                {
                    imageManager.OpenAssetsFolder();
                    return;
                }

                // fallback: відкрити теку напряму, якщо з якоїсь причини сервіс не передали
                var path = System.IO.Path.Combine(GetPluginFolderPath(), "assets");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to open assets folder: {ex.Message}");
                playniteApi?.Dialogs?.ShowErrorMessage("Не вдалося відкрити теку assets. Перевірте журнали.", "Помилка");
            }
        }

        // універсальний пошук дочірнього елемента за предикатом
        private T FindChild<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
        {
            if (parent == null) return null;

            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && (predicate?.Invoke(typed) ?? true))
                    return typed;

                var nested = FindChild<T>(child, predicate);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}