using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Playnite.SDK;

namespace DiscordRichPresencePlugin.UI
{
    public partial class TemplateManagerView : UserControl
    {
        private readonly ILogger logger = LogManager.GetLogger();

        public TemplateManagerView()
        {
            InitializeComponent();
        }

        private global::DiscordRichPresencePlugin.UI.TemplateManagerViewModel VM
            => this.DataContext as global::DiscordRichPresencePlugin.UI.TemplateManagerViewModel;

        // --- Toolbar actions ---

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            VM.AddNew();
            TemplatesGrid?.Items.Refresh();

        }

        private void BtnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null || VM.SelectedTemplate == null) return;
            VM.DuplicateSelected();
            TemplatesGrid?.Items.Refresh();

        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null || VM.SelectedTemplate == null) return;

            var res = API.Instance.Dialogs.ShowMessage(
                "Remove selected template?",
                "Template Manager",
                MessageBoxButton.YesNo);

            if (res == MessageBoxResult.Yes)
            {
                VM.RemoveSelected();
                TemplatesGrid?.Items.Refresh();

            }
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            VM.MoveSelectedUp();
            TemplatesGrid?.Items.Refresh();

        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            VM.MoveSelectedDown();
            TemplatesGrid?.Items.Refresh();

        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            VM.Refresh();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;

            string error;
            if (VM.Save(out error))
            {
                API.Instance.Dialogs.ShowMessage("Templates saved.", "Template Manager");
            }
            else
            {
                API.Instance.Dialogs.ShowErrorMessage($"Failed to save templates.\n{error}", "Template Manager");
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "status_templates.json",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (sfd.ShowDialog() != true) return;

            string error;
            if (VM.Export(sfd.FileName, out error))
            {
                API.Instance.Dialogs.ShowMessage($"Exported to:\n{sfd.FileName}", "Template Manager");
            }
            else
            {
                API.Instance.Dialogs.ShowErrorMessage($"Export failed.\n{error}", "Template Manager");
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;

            var ofd = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true) return;

            string error;
            if (VM.Import(ofd.FileName, true, out error))
            {
                API.Instance.Dialogs.ShowMessage($"Imported from:\n{ofd.FileName}", "Template Manager");
            }
            else
            {
                API.Instance.Dialogs.ShowErrorMessage($"Import failed.\n{error}", "Template Manager");
            }
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            VM.GenerateTemplateStub();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            var wnd = Window.GetWindow(this);
            if (wnd != null)
            {
                wnd.Close();
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void DataGrid_SelectionChanged_1(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}
