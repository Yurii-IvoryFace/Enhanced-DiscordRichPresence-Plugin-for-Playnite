using DiscordRichPresencePlugin.Models;
using DiscordRichPresencePlugin.Services;
using Playnite.SDK;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DiscordRichPresencePlugin.Views
{
    public class TemplateManagerViewModel : ObservableObject
    {
        private readonly TemplateService templateService;

        public ObservableCollection<StatusTemplate> Templates { get; } =
            new ObservableCollection<StatusTemplate>();

        public TemplateManagerViewModel(TemplateService service)
        {
            templateService = service;
            Refresh();
        }

        public void Refresh()
        {
            Templates.Clear();
            var list = templateService?.GetAllTemplates() ?? new System.Collections.Generic.List<StatusTemplate>();
            foreach (var t in list)
            {
                Templates.Add(t);
            }
        }
    }
}
