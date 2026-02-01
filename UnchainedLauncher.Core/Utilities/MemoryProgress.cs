using PropertyChanged;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace UnchainedLauncher.Core.Utilities {
    [AddINotifyPropertyChangedInterface]
    public partial class MemoryProgress : IProgress<double>, INotifyPropertyChanged {
        public double ProgressPercentage { get; protected set; }
        public string TaskName { get; protected set; }

        public MemoryProgress(string? taskName = null, double progress = 0) {
            TaskName = taskName ?? "";
            ProgressPercentage = progress;
        }

        public void Report(double value) {
            ProgressPercentage = Math.Clamp(value, 0.00, 100.00);
        }
    }

    public class AccumulatedMemoryProgress : MemoryProgress {
        private readonly SynchronizationContext? _synchronizationContext;

        // average the progress of sub-percentages
        private double CalcAggregatePercentage() =>
            !Progresses.Any()
                ? 100.00
                : Progresses.ToList()
                      .Sum(mp => mp.ProgressPercentage)
                  / Progresses.Count;

        public ObservableCollection<MemoryProgress> Progresses { get; private set; }

        public AccumulatedMemoryProgress(IEnumerable<MemoryProgress>? memoryProgress = null, string? taskName = null, SynchronizationContext? synchronizationContext = null) : base(taskName) {
            _synchronizationContext = synchronizationContext ?? SynchronizationContext.Current ?? UISynchronizationContext.Context;
            Progresses = new ObservableCollection<MemoryProgress>(memoryProgress ?? new List<MemoryProgress>());
            Progresses.ToList().ForEach(BindTo);
            ProgressPercentage = CalcAggregatePercentage();
        }

        public void AlsoTrack(MemoryProgress memoryProgress) {
            _synchronizationContext.Post(_ => {
                Progresses.Add(memoryProgress);
                ProgressPercentage = CalcAggregatePercentage();
            }, null);
            
            BindTo(memoryProgress);
        }

        private void OnProgressPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(ProgressPercentage)) {
                ProgressPercentage = CalcAggregatePercentage();
            }
        }

        private void BindTo(MemoryProgress mp) {
            mp.PropertyChanged += OnProgressPropertyChanged;
        }
    }
}