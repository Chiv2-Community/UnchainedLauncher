using Moq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using UnchainedLauncher.Core.INIModels.Game;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Server;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections;
using UnchainedLauncher.UnrealModScanner.GUI.ViewModels;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Server {
    public class MapSelectionRegressionTests {
        private static MapDto Courtyard() => new("TBL/Content/Maps/Courtyard/FFA_Courtyard.umap", "hash", null, null, null, "Courtyard", null, null, null, "FFA", null);

        [Fact]
        public void RotationRoundTripPreservesTravelStringsAndLateCatalogMetadata() {
            RunSta(() => {
                string[] paths = ["FFA_Courtyard", "ffa_courtyard", "MyCustomMap", "/Game/Maps/Courtyard/FFA_Courtyard", "FFA_Courtyard?Game=FFA", "FFA_Courtyard"];
                var catalog = new ObservableCollection<MapDto>();
                var vm = new TBLGameModeSectionVM();
                vm.LoadFrom(new TBLGameMode(MapList: paths, MapListIndex: 3), catalog);
                Assert.Equal(paths, vm.ToModel().MapList);
                Assert.Equal(3, vm.ToModel().MapListIndex);
                Assert.All(vm.MapList, entry => Assert.Null(entry.Map));

                var map = Courtyard();
                catalog.Add(map);
                Assert.Same(map, vm.MapList[0].Map);
                Assert.Same(map, vm.MapList[1].Map);
                Assert.Null(vm.MapList[2].Map);
                Assert.Same(map, vm.MapList[3].Map);
                Assert.Same(map, vm.MapList[4].Map);
                Assert.Equal(paths, vm.ToModel().MapList);
                vm.LoadFrom(new TBLGameMode(MapList: paths, MapListIndex: 3), catalog);
                Assert.Equal(paths, vm.ToModel().MapList);
                catalog.Clear();
                Assert.Equal(paths, vm.ToModel().MapList);
                Assert.All(vm.MapList, entry => Assert.Null(entry.Map));

                vm.MapToAdd = map;
                vm.AddMapCommand.Execute(null);
                Assert.Equal("FFA_Courtyard", vm.ToModel().MapList.Last());
                vm.RemoveMapCommand.Execute(vm.MapList.Last());
                Assert.Equal(paths, vm.ToModel().MapList);
            });
        }

        [Fact]
        public void ServerLaunchConfigurationUsesOriginalUnresolvedRotationEntry() {
            RunSta(() => {
                var manager = new ModManager(Mock.Of<IModRegistry>(), Mock.Of<IPakDir>(), []);
                var scan = new ModScanTabVM();
                var available = new AvailableModsAndMapsService(manager, scan);
                var vm = new ServerConfigurationVM(manager, scan, available,
                    name: "RotationRegression_" + Guid.NewGuid().ToString("N"), localIp: "127.0.0.1");
                const string travel = "/Game/Mods/CustomMap?Game=FFA";
                vm.GameMode.LoadFrom(new TBLGameMode(MapList: ["FFA_Courtyard", travel], MapListIndex: 1), vm.AvailableMaps);
                Assert.Equal(travel, vm.ToServerConfiguration().NextMapPath);
                available.AvailableMaps.Clear();
                Assert.Equal(travel, vm.ToServerConfiguration().NextMapPath);
                Assert.Equal(["FFA_Courtyard", travel], vm.GameMode.ToModel().MapList);
            });
        }

        [Fact]
        public void LiveMapSelectionBindingSendsTravelStringAndUpdatesCommandState() {
            RunSta(() => {
                var rcon = new Mock<IRCON>();
                rcon.Setup(r => r.SendCommand(It.IsAny<string>())).Returns(Task.CompletedTask);
                using var process = new Process();
                var launchOptions = new ServerLaunchOptions(false, "test", "", false, default, "ffa_courtyard",
                    7777, 3075, 7071, 9001, null, null, null, null, null, null, default, [], default,
                    false, true, CensorArg.Standard);
                var server = new Chivalry2Server(process, launchOptions, null!, rcon.Object);
                var map = Courtyard();
                var vm = new ServerVM(server, "test", new ObservableCollection<MapDto> { map });
                // Read the production XAML contract rather than duplicating an unchecked binding.
                var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "Server.xaml"));
                var combo = xaml.Descendants().Single(element => element.Name.LocalName == "ComboBox"
                    && (string?)element.Attribute("SelectedItem") == "{Binding SelectedMapToChange}");
                Assert.Equal("{Binding AvailableMaps}", (string?)combo.Attribute("ItemsSource"));
                var selector = new ComboBox { DataContext = vm };
                selector.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ServerVM.AvailableMaps)));
                selector.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                    new Binding(nameof(ServerVM.SelectedMapToChange)) { Mode = BindingMode.TwoWay });
                Assert.False(vm.ChangeMapCommand.CanExecute(null));
                var notifications = 0;
                vm.ChangeMapCommand.CanExecuteChanged += (_, _) => notifications++;
                selector.SelectedItem = map;
                Assert.Same(map, vm.SelectedMapToChange);
                Assert.True(vm.ChangeMapCommand.CanExecute(null));
                vm.ChangeMapCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                rcon.Verify(r => r.SendCommand("servertravel FFA_Courtyard"), Times.Once);
                vm.CurrentMap = "ffa_courtyard";
                Assert.False(vm.ChangeMapCommand.CanExecute(null));
                Assert.True(notifications >= 2);
                vm.SelectedMapToChange = null;
                vm.StartUpdateLoop();
                Assert.Same(map, vm.SelectedMapToChange);
            });
        }

        private static void RunSta(Action action) {
            Exception? error = null;
            var thread = new Thread(() => {
                try { action(); }
                catch (Exception ex) { error = ex; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}