using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Playnite.SDK;
using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Services;

namespace DiscordRichPresencePlugin.UI
{
    public class TemplateManagerViewModel : ObservableObject
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly TemplateService templateService;


        public RelayCommand GenerateTemplateCommand { get; private set; }
        public ObservableCollection<StatusTemplate> Templates { get; } =
            new ObservableCollection<StatusTemplate>();

        public TemplateManagerViewModel(TemplateService service)
        {
            templateService = service ?? throw new ArgumentNullException(nameof(service));
            GenerateTemplateCommand = new RelayCommand(() => GenerateTemplate());
            Refresh();
        }

        public void Refresh()
        {
            Templates.Clear();
            var list = templateService?.GetAllTemplates() ?? new List<StatusTemplate>();
            foreach (var t in list.OrderBy(t => t.Priority).ThenBy(t => t.Name))
            {
                Templates.Add(t);
            }
            logger.Debug($"TemplateManagerViewModel: loaded {Templates.Count} templates");
        }

        public bool Save(out string error)
        {
            error = null;
            try
            {
                // replace all with current in-memory list
                templateService.ReplaceAllTemplates(Templates.ToList());
                logger.Debug("TemplateManagerViewModel: templates saved");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.Error($"Failed to save templates: {ex}");
                return false;
            }
        }

        public bool Export(string path, out string error)
        {
            error = null;
            try
            {
                var ok = templateService.ExportTemplates(path);
                if (!ok) { error = "ExportTemplates returned false."; }
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.Error($"Failed to export templates: {ex}");
                return false;
            }
        }

        public bool Import(string path, bool merge, out string error)
        {
            error = null;
            try
            {
                var ok = templateService.ImportTemplates(path, merge);
                if (ok)
                {
                    Refresh();
                }
                else
                {
                    error = "ImportTemplates returned false.";
                }
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.Error($"Failed to import templates: {ex}");
                return false;
            }
        }
        private void GenerateTemplate()
        {
            // TODO: implement smart generator (ML/heuristics) later.
            // For now just inform user; you can also prefill a basic template here.
            var api = API.Instance;
            if (api != null)
            {
                api.Dialogs.ShowMessage(
                    "Template generator is coming soon. This button is a stub for now.",
                    "Template Manager");
            }
        }
    }
}
