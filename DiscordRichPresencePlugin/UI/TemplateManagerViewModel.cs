using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Services;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DiscordRichPresencePlugin.UI
{
    public class TemplateManagerViewModel : ObservableObject
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly TemplateService templateService;

        public ObservableCollection<StatusTemplate> Templates { get; } = new ObservableCollection<StatusTemplate>();

        private StatusTemplate _selectedTemplate;
        public StatusTemplate SelectedTemplate
        {
            get => _selectedTemplate;
            set => SetValue(ref _selectedTemplate, value);
        }

        // Active list for the right pane
        public System.Collections.Generic.IEnumerable<StatusTemplate> ActiveTemplates
            => Templates.Where(t => t.IsEnabled).OrderBy(t => t.Priority);

        public TemplateManagerViewModel(TemplateService templateService)
        {
            this.templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            Refresh();
        }

        public void Refresh()
        {
            Templates.Clear();
            foreach (var t in templateService.GetAllTemplates().OrderBy(x => x.Priority))
            {
                Templates.Add(t);
            }
            RecalculatePriorities();
            OnPropertyChanged(nameof(ActiveTemplates));
        }

        public void RecalculatePriorities()
        {
            // Top row gets priority 1
            for (int i = 0; i < Templates.Count; i++)
            {
                Templates[i].Priority = i + 1;
            }
            OnPropertyChanged(nameof(ActiveTemplates));
        }

        public void AddNew()
        {
            var t = new StatusTemplate
            {
                Name = "New template",
                DetailsFormat = "{game} — {sessionTime}",
                StateFormat = "{platform} · {genre}",
                IsEnabled = true,
                Priority = Templates.Count + 1
            };
            Templates.Add(t);
            SelectedTemplate = t;
            OnPropertyChanged(nameof(ActiveTemplates));
        }

        public void DuplicateSelected()
        {
            if (SelectedTemplate == null) return;
            var src = SelectedTemplate;
            var idx = Templates.IndexOf(src);
            if (idx < 0) return;

            var copy = new StatusTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = src.Name + " (copy)",
                Description = src.Description,
                DetailsFormat = src.DetailsFormat,
                StateFormat = src.StateFormat,
                IsEnabled = src.IsEnabled,
                Priority = src.Priority
            };

            Templates.Insert(Math.Min(idx + 1, Templates.Count), copy);
            RecalculatePriorities();
            SelectedTemplate = copy;
        }

        public void RemoveSelected()
        {
            if (SelectedTemplate == null) return;
            var idx = Templates.IndexOf(SelectedTemplate);
            if (idx < 0) return;

            Templates.RemoveAt(idx);
            RecalculatePriorities();
            SelectedTemplate = Templates.Count > 0 ? Templates[Math.Min(idx, Templates.Count - 1)] : null;
        }

        public void MoveSelectedUp()
        {
            if (SelectedTemplate == null) return;
            var idx = Templates.IndexOf(SelectedTemplate);
            if (idx <= 0) return;

            Templates.Move(idx, idx - 1);
            RecalculatePriorities();
        }

        public void MoveSelectedDown()
        {
            if (SelectedTemplate == null) return;
            var idx = Templates.IndexOf(SelectedTemplate);
            if (idx < 0 || idx >= Templates.Count - 1) return;

            Templates.Move(idx, idx + 1);
            RecalculatePriorities();
        }

        public bool Save(out string error)
        {
            error = null;
            try
            {
                // Persist the current in-memory list
                templateService.ReplaceAllTemplates(Templates);
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

        // Stub for future generator
        public void GenerateTemplateStub()
        {
            AddNew();
            if (SelectedTemplate != null)
            {
                SelectedTemplate.Name = "Suggested template";
                SelectedTemplate.Description = "Auto-generated (stub)";
            }
        }
    }
}
