using System.IO;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;

namespace DiscordRichPresencePlugin.Views
{
    public partial class TemplateManagerView : UserControl
    {
        private readonly ILogger logger = LogManager.GetLogger();

        public TemplateManagerView()
        {
            InitializeComponent();
        }

        private TemplateManagerViewModel VM => DataContext as TemplateManagerViewModel;

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            VM?.Refresh();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            if (VM.Save(out var error))
            {
                API.Instance.Dialogs.ShowMessage("Зміни збережено.", "Template Manager");
            }
            else
            {
                API.Instance.Dialogs.ShowErrorMessage($"Не вдалося зберегти шаблони.\n{error}", "Помилка");
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;

            var path = API.Instance.Dialogs.SaveFile("JSON|*.json");
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!path.EndsWith(".json")) path += ".json";

            if (VM.Export(path, out var error))
            {
                API.Instance.Dialogs.ShowMessage($"Експортовано до:\n{path}", "Template Manager");
            }
            else
            {
                API.Instance.Dialogs.ShowErrorMessage($"Не вдалося експортувати шаблони.\n{error}", "Помилка");
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;

            var path = API.Instance.Dialogs.SelectFile("JSON|*.json");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            // merge = true: додаємо нові та оновлюємо існуючі за Id/Name
            if (VM.Import(path, merge: true, out var error))
            {
                API.Instance.Dialogs.ShowMessage($"Імпортовано з:\n{path}", "Template Manager");
            }
            else
            {
                API.Instance.Dialogs.ShowErrorMessage($"Не вдалося імпортувати шаблони.\n{error}", "Помилка");
            }
        }
    }
}
