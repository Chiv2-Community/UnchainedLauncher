using CommunityToolkit.Mvvm.Input;
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

        public IModRegistryVM IntoVM(IModRegistry registry) {
            return registry switch {
                GithubModRegistry reg => new GithubModRegistryVM(reg, RemoveRegistry),
                LocalModRegistry reg => new LocalModRegistryVM(reg, RemoveRegistry),
                _ => new GenericModRegistryVM(registry)
            };
        }

        [RelayCommand]
        public void AddNewGithubRegistry() {
            GithubModRegistry ghr = new GithubModRegistry(
                "Chiv2-Community",
                "C2ModRegistry",
                HttpPakDownloader.GithubPakDownloader
            );

            AddRegistry(ghr);
        }

        [RelayCommand]
        public void AddNewLocalRegistry() {
            LocalModRegistry lmr = new LocalModRegistry("LocalRegistryPath", new LocalFilePakDownloader("LocalRegistryPath"));

            AddRegistry(lmr);
        }

        public void AddRegistry(IModRegistry mr) {
            Registry.ModRegistries.Add(mr);
            Registries.Add(IntoVM(mr));
        }

        public void RemoveRegistry(IModRegistryVM vm) {
            Registry.ModRegistries.Remove(vm.Registry);
            Registries.Remove(vm);
        }

        public RegistryTabVM() : this(new AggregateModRegistry()) { }

        public static RegistryTabVM DEFAULT => makeDefault();

        private static RegistryTabVM makeDefault() {
            var vm = new RegistryTabVM();
            vm.Registries.Add(
                new GithubModRegistryVM(
                    new GithubModRegistry(
                            "Chiv2-Community",
                            "C2ModRegistry",
                            HttpPakDownloader.GithubPakDownloader
                        ), vm.RemoveRegistry
                    )
                );
            vm.Registries.Add(
                new LocalModRegistryVM(
                    new LocalModRegistry(
                        "LocalModRegistryTesting1",
                        new LocalFilePakDownloader("LocalModRegistryTesting1")
                        ), vm.RemoveRegistry
                )
            );
            vm.Registries.Add(
                new LocalModRegistryVM(
                    new LocalModRegistry(
                        "LocalModRegistryTesting2",
                        new LocalFilePakDownloader("LocalModRegistryTesting2")
                    ), vm.RemoveRegistry
                )
            );
            return vm;
        }
    }

    public interface IModRegistryVM {
        public IModRegistry Registry { get; }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class GenericModRegistryVM : INotifyPropertyChanged, IModRegistryVM {
        public IModRegistry Registry { get; }
        public delegate void RequestDeletion(IModRegistryVM toDelete);
        private RequestDeletion? _requestDeletion;

        [RelayCommand]
        public void SelfDelete() {
            _requestDeletion?.Invoke(this);
        }

        public GenericModRegistryVM(IModRegistry registry, RequestDeletion? requestDeletion = null) {
            Registry = registry;
            _requestDeletion = requestDeletion;
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class GithubModRegistryVM : GenericModRegistryVM {
        public new GithubModRegistry Registry { get; private set; }

        public GithubModRegistryVM(GithubModRegistry registry, RequestDeletion? requestDeletion = null) : base(registry, requestDeletion) {
            Registry = registry;
        }

        [DependsOn(nameof(Org), nameof(RepoName))]
        public string Name => Registry.Name;

        public string Org {
            get => Registry.Organization;
            set => Registry.Organization = value;
        }
        public string RepoName {
            get => Registry.RepoName;
            set => Registry.RepoName = value;
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class LocalModRegistryVM : GenericModRegistryVM {
        public new LocalModRegistry Registry { get; private set; }

        public LocalModRegistryVM(LocalModRegistry registry, RequestDeletion? requestDeletion = null) : base(registry, requestDeletion) {
            Registry = registry;
        }

        [DependsOn(nameof(RegistryPath))]
        public string Name => Registry.Name;

        public string RegistryPath {
            get => Registry.RegistryPath;
            set {
                Registry.RegistryPath = value;
                if (Registry.ModRegistryDownloader is LocalFilePakDownloader downloader) {
                    downloader.PakReleasesDir = value;
                }
                else {
                    throw new InvalidOperationException(
                        $"Unsure how to mutate downloader of type '{Registry.ModRegistryDownloader.GetType().Name}' to use new pak dir"
                        );
                }
            }
        }

        public string AbsoluteStub => FileSystem.CurrentDirectory;
        public string PathSeparator => Path.DirectorySeparatorChar.ToString();

    }
}