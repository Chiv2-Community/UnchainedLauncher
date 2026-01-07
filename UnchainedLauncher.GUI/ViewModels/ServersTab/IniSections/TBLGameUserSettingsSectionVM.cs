using PropertyChanged;
using UnchainedLauncher.Core.INIModels.GameUserSettings;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections {
    [AddINotifyPropertyChangedInterface]
    public class TBLGameUserSettingsSectionVM {
        private bool _syncingFps;

        public int MaxFPS { get; set; }
        public float FrameRateLimit { get; set; }

        public int FpsLimit {
            get => MaxFPS;
            set {
                _syncingFps = true;
                MaxFPS = value;
                FrameRateLimit = value;
                _syncingFps = false;
            }
        }

        private void OnMaxFPSChanged() {
            if (_syncingFps) return;
            _syncingFps = true;
            FrameRateLimit = MaxFPS;
            _syncingFps = false;
        }

        private void OnFrameRateLimitChanged() {
            if (_syncingFps) return;
            _syncingFps = true;
            MaxFPS = (int)FrameRateLimit;
            _syncingFps = false;
        }

        public void LoadFrom(TBLGameUserSettings model) {
            MaxFPS = model.MaxFPS;
            FrameRateLimit = model.FrameRateLimit;
        }

        public TBLGameUserSettings ToModel() => new(MaxFPS, FrameRateLimit);
    }
}
