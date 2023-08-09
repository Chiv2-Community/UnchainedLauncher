using C2GUILauncher.JsonModels;
using C2GUILauncher.Mods;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher.ViewModels
{
    [AddINotifyPropertyChangedInterface]
    public class ModViewModel
    {
        // A ModViewModel needs access to the mod manager so that it can enable/disable releases as they get set on the view.
        private ModManager ModManager { get; }

        public Mod Mod { get; }

        // This property duplication (_enabledRelease and EnabledRelease) is normal in C# and is for maintaining public
        // getters/setters independently of the underlying private value.  This is necessary in this case because of the 
        // special interactions with the ModManager.
        private Release? _enabledRelease;
        public Release? EnabledRelease
        {
            get { return _enabledRelease; }
            set
            {
                if (EnabledRelease != value)
                {
                    var original = _enabledRelease;
                    _enabledRelease = value;
                    if (value == null)
                    {
                        ModManager.DisableModRelease(original!);
                    }
                    else
                    {
                        ModManager.EnableModRelease(value);
                    }
                }
            }
        }


        public string Description
        {
            get { return EnabledRelease?.Manifest.Description ?? Mod.LatestManifest.Description; }
        }


        public string TagsString
        {
            get { return string.Join(", ", Mod.LatestManifest.Tags); }
        }

        public bool IsEnabled
        {
            get { return EnabledRelease != null; }
        }

        public string? EnabledVersion
        {
            get { return IsEnabled ? EnabledRelease!.Tag : "none"; }
        }

        public List<string> AvailableVersions
        {
            get { return Mod.Releases.Select(x => x.Tag).ToList(); }
        }


        public ModViewModel(Mod mod, Release? enabledRelease, ModManager modManager)
        {
            Mod = mod;
            EnabledRelease = enabledRelease;
            ModManager = modManager;
        }

    }
}
