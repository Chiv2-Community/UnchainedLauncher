using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using System.IO;
using System.Reflection;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    public class ServerTemplateVM {
        public ServerInfoFormVM Form { get; }
        public ObservableCollection<Release> Releases { get; }

        // TODO: serialization/deserialization for save/reload
        public ServerTemplateVM(ObservableCollection<Release>? releases = null) {
            Releases = releases ?? new ObservableCollection<Release>();
            ObservableCollection<string> maps = new(getDefaultMaps());
            foreach(Release r in Releases) {
                foreach(var m in r.Manifest.Maps) {
                    maps.Add(m);
                }
            }
            Form = new ServerInfoFormVM(maps);
        }

        private IEnumerable<string> getDefaultMaps() {
            List<string> maps = new List<string>();
            using (var defaultMapsListStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UnchainedLauncher.GUI.Resources.DefaultMaps.txt")) {
                if (defaultMapsListStream != null) {
                    using var reader = new StreamReader(defaultMapsListStream);

                    var defaultMapsString = reader.ReadToEnd();
                    defaultMapsString
                        .Split("\n")
                        .Select(x => x.Trim())
                        .ToList()
                        .ForEach(maps.Add);
                }
            }
            return maps;
        }

    }
}
