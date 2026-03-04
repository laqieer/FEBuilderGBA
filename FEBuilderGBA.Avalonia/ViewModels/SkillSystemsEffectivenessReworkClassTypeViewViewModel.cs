using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SkillSystemsEffectivenessReworkClassTypeViewViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void Initialize()
        {
            // Skill system detection requires PatchUtil which is WinForms-only.
            // In Avalonia, we show an informational message.
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string> { ["status"] = "skill_patch_required" };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
