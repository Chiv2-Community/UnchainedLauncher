using Microsoft.VisualBasic.FileIO;
using PropertyChanged;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;

namespace UnchainedLauncher.GUI.ViewModels.Registry {
    public partial class RegistryTabVM {
        public AggregateModRegistry Registry { get; private set; }
        public ObservableCollection<IModRegistryVM> Registries { get; }
        public RegistryTabVM(AggregateModRegistry registry) {
            Registry = registry;
            Registries = new ObservableCollection<IModRegistryVM>();
            pullRegistryVMs();
        }

        public void pullRegistryVMs() {
            Registries.Clear();
            foreach (IModRegistryVM vm in Registry.ModRegistries.Map(IntoVM)) {
                Registries.Add(vm);
            }
        }

        public static IModRegistryVM IntoVM(IModRegistry registry) {
            return registry switch {
                GithubModRegistry reg => new GithubModRegistryVM(reg),
                LocalModRegistry reg => new LocalModRegistryVM(reg),
                _ => new GenericModRegistryVM(registry)
            };
        }
        
        public RegistryTabVM() : this(new AggregateModRegistry()) {}
        
        public static RegistryTabVM DEFAULT => makeDefault();
    
        private static RegistryTabVM makeDefault() {
            var vm = new RegistryTabVM();
            vm.Registries.Add(
                new GithubModRegistryVM(
                    new GithubModRegistry(
                        "Chiv2-Community", 
                        "C2ModRegistry", 
                        new HttpPakDownloader($"https://github.com/<Org>/<Repo>/releases/download/<Version>/<PakFileName>"))
                    )
                );
            vm.Registries.Add(
                new LocalModRegistryVM(
                    new LocalModRegistry(
                        "LocalModRegistryTesting1",
                        new LocalFilePakDownloader("LocalModRegistryTesting1"))
                )
            );
            vm.Registries.Add(
                new LocalModRegistryVM(
                    new LocalModRegistry(
                        "LocalModRegistryTesting2",
                        new LocalFilePakDownloader("LocalModRegistryTesting2"))
                )
            );
            return vm;
        }
    }

    public interface IModRegistryVM {
        public IModRegistry Registry { get; }
    }
    
    public class GenericModRegistryVM : IModRegistryVM {
        public IModRegistry Registry { get; }

        public GenericModRegistryVM(IModRegistry registry) {
            Registry = registry;
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class GithubModRegistryVM : INotifyPropertyChanged, IModRegistryVM {
        public GithubModRegistry _registry { get; private set; }
        public IModRegistry Registry => _registry;

        public GithubModRegistryVM(GithubModRegistry registry) {
            _registry = registry;
        }
        
        [DependsOn(nameof(Org), nameof(RepoName))]
        public string Name => _registry.Name;

        public string Org {
            get => _registry.Organization;
            set => _registry.Organization = value;
        }
        public string RepoName {
            get => _registry.RepoName;
            set => _registry.RepoName = value;
        }
    }
    
    [AddINotifyPropertyChangedInterface]
    public partial class LocalModRegistryVM : INotifyPropertyChanged, IModRegistryVM {
        public LocalModRegistry _registry { get; private set; }
        public IModRegistry Registry => _registry;

        public LocalModRegistryVM(LocalModRegistry registry) {
            _registry = registry;
        }
        
        [DependsOn(nameof(RegistryPath))]
        public string Name => _registry.Name;

        public string RegistryPath {
            get => _registry.RegistryPath;
            set {
                _registry.RegistryPath = value;
                if (_registry.ModRegistryDownloader is LocalFilePakDownloader downloader) {
                    downloader.PakReleasesDir = value;
                }
                else {
                    throw new InvalidOperationException(
                        $"Unsure how to mutate downloader of type '{_registry.ModRegistryDownloader.GetType().Name}' to use new pak dir"
                        );
                }
            } 
        }

        public string AbsoluteStub => FileSystem.CurrentDirectory;
        public string PathSeparator => Path.DirectorySeparatorChar.ToString();

    }
}