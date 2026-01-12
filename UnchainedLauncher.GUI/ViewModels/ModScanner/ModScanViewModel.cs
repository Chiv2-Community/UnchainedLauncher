
//using System.Collections.ObjectModel;
//using System.Linq;
//using System.Threading.Tasks;
//using UnchainedLauncher.UnrealModScanner;
//using UnrealModScanner.Export;
//using UnrealModScanner.Models;

//namespace UnchainedLauncher.GUI.ViewModels.ModScanner {
//    public sealed class ModScanViewModel {
//        public ObservableCollection<PakScanResult> Results { get; } = new();

//        public async Task ScanAsync(string pakDirectory) {
//            Results.Clear();

//            IModScanner scanner = new UnchainedLauncher.UnrealModScanner.Scanning.ModScanner();
//            var scanResults = await scanner.ScanAsync(pakDirectory);

//            foreach (var pak in scanResults)
//                Results.Add(pak);
//        }

//        public void ExportJson(string path) {
//            ModScanJsonExporter.ExportToFile(Results.ToList(), path);
//        }
//    }
//}
