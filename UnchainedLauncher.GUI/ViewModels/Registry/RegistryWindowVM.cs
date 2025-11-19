using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using Microsoft.VisualBasic.FileIO;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.Views.Registry;

namespace UnchainedLauncher.GUI.ViewModels.Registry {
    using static LanguageExt.Prelude;

    [AddINotifyPropertyChangedInterface]
    public partial class RegistryWindowVM {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(RegistryWindowVM));

        public AggregateModRegistry Registry { get; }
        public ObservableCollection<IModRegistryVM<IModRegistry>> Registries { get; }
        private readonly IRegistryWindowService _windowService;

        public RegistryWindowVM(AggregateModRegistry registry,
            IRegistryWindowService windowService) {
            _windowService = windowService;
            Registry = registry;
            Registries = new ObservableCollection<IModRegistryVM<IModRegistry>>();
            LoadRegistryViewModels();
        }

        public void LoadRegistryViewModels() {
            Registries.Clear();
            IntoVM(Registry).ForEach(Registries.Add);
            Logger.Info($"Loaded {Registries.Count} Registry View Models");
        }

        private IEnumerable<IModRegistryVM<IModRegistry>> IntoVM(IModRegistry registry) {
            Logger.Debug($"Creating Registry View Model for {registry.Name}");
            return registry switch {
                AggregateModRegistry reg => reg.ModRegistries.AsEnumerable().Bind(IntoVM),
                GithubModRegistry reg => new List<IModRegistryVM<IModRegistry>> {new GithubModRegistryVM(reg, RemoveRegistry)},
                LocalModRegistry reg => new List<IModRegistryVM<IModRegistry>> { new LocalModRegistryVM(reg, _windowService, RemoveRegistry)},
                _ => new List<IModRegistryVM<IModRegistry>> { new GenericModRegistryVM<IModRegistry>(registry, RemoveRegistry) }
            };
        }

        [RelayCommand]
        public void AddNewGithubRegistry() {
            var ghr = new GithubModRegistry(
                "Chiv2-Community",
                "C2ModRegistry"
            );

            AddRegistry(ghr);
        }

        [RelayCommand]
        public void AddNewLocalRegistry() {
            var lmr = new LocalModRegistry("LocalModRegistry");

            AddRegistry(lmr);
        }

        public void AddRegistry(IModRegistry mr) {
            Registry.ModRegistries.Add(mr);
            IntoVM(mr).ForEach(Registries.Add);
        }

        public void RemoveRegistry(IModRegistryVM<IModRegistry> vm) {
            Registry.ModRegistries.Remove(vm.Registry);
            Registries.Remove(vm);
        }
    }


    public interface IModRegistryVM<out T> where T : IModRegistry {
        T Registry { get; }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class GenericModRegistryVM<T> : INotifyPropertyChanged, IModRegistryVM<T>
        where T: IModRegistry {
        public T Registry { get; }
        public delegate void RequestDeletion(IModRegistryVM<T> toDelete);
        
        private readonly RequestDeletion? _requestDeletion;

        [RelayCommand]
        public void SelfDelete() {
            _requestDeletion?.Invoke(this);
        }

        public GenericModRegistryVM(T registry, RequestDeletion? requestDeletion = null) {
            Registry = registry;
            _requestDeletion = requestDeletion;
        }
    }

    public class GenericModRegistryVM : GenericModRegistryVM<IModRegistry> {
        public GenericModRegistryVM(IModRegistry registry, RequestDeletion? requestDeletion = null): base(registry, requestDeletion) { }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class GithubModRegistryVM : GenericModRegistryVM<GithubModRegistry> {
        public GithubModRegistryVM(GithubModRegistry registry, RequestDeletion? requestDeletion = null) : base(registry, requestDeletion) { }

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
    public partial class LocalModRegistryVM : GenericModRegistryVM<LocalModRegistry> {
        private IRegistryWindowService _windowService;

        public LocalModRegistryVM(LocalModRegistry registry, IRegistryWindowService windowService, RequestDeletion? requestDeletion = null) : base(registry, requestDeletion) {
            _windowService = windowService;
        }

        [DependsOn(nameof(RegistryPath))]
        public string Name => Registry.Name;

        public string RegistryPath {
            get => Registry.RegistryPath;
            set => Registry.RegistryPath = value;
        }

        [RelayCommand]
        public void OpenConfigWindow() {
            _windowService.ShowLocalRegistryWindow(new LocalModRegistryWindowVM(Registry, _windowService));
        }

        public string AbsoluteStub => FileSystem.CurrentDirectory;
        public string PathSeparator => Path.DirectorySeparatorChar.ToString();

    }
}