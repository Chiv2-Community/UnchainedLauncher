using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UnchainedLauncher.Core.Services.Server;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    public enum UptimeState {
        Down,
        Starting,
        Up
    }

    // TODO? listplayers integration.
    // 1. send listplayers to rcon
    // 2. get response from system clipboard
    // 3. neatly display response information in-window

    [AddINotifyPropertyChangedInterface]
    public partial class ServerVM(Chivalry2Server server, string serverName, ObservableCollection<string> availableMaps) {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ServerVM));

        public Chivalry2Server Server { get; private set; } = server;

        public string ServerName { get; set; } = serverName;

        public ObservableCollection<string> AvailableMaps { get; } = availableMaps;
        public string SelectedMapToChange { get; set; } = "";

        public bool CanChangeMap =>
            !string.IsNullOrWhiteSpace(SelectedMapToChange)
            && !string.Equals(SelectedMapToChange, CurrentMap ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Launch options (static per launch, but refreshed on restart)
        public int GamePort { get; set; }
        public int BeaconPort { get; set; }
        public int QueryPort { get; set; }
        public int RconPort { get; set; }
        public string LocalIpText { get; set; } = "-";
        public string ModActorsText { get; set; } = "-";

        public bool IsUp { get; set; } = true;
        public DateTimeOffset LastStartTime { get; set; } = DateTimeOffset.Now;
        public int RestartCount { get; set; }
        public string CurrentUptimeText { get; set; } = "00:00:00";
        public int HandleCount { get; set; }
        public long PrivateMemorySize64 { get; set; }
        public long PeakPrivateMemorySize64 { get; set; }

        // A2S (server query) details
        public int PlayerCount { get; set; }
        public int BotCount { get; set; }
        public int MaxPlayers { get; set; }
        public string CurrentMap { get; set; } = "";
        public string GameType { get; set; } = "";
        public bool HasA2SInfo { get; set; }

        private bool _hasSeenA2SSuccessSinceStart;

        // History data (max 12 hours, sampled every 10 seconds)
        // Rightmost = most recent. Display range scales dynamically to a minimum of 10 minutes.
        // `null` means "unrecorded" (e.g., A2S unavailable or not yet collected).
        public ObservableCollection<double?> PrivateMemoryHistoryMiB { get; } = new();
        public ObservableCollection<UptimeState?> UpDownHistory { get; } = new();
        public ObservableCollection<double?> PlayerCountHistory { get; } = new();

        // Display histories (padded to the current dynamic window).
        public ObservableCollection<double?> DisplayPrivateMemoryHistoryMiB { get; } = new();
        public double DisplayPrivateMemoryHistoryMaxMiB { get; set; }

        public ObservableCollection<UptimeState?> DisplayUpDownHistory { get; } = new();

        public ObservableCollection<double?> DisplayPlayerCountHistory { get; } = new();
        public double DisplayPlayerCountHistoryMax { get; set; }

        private const int HistorySampleStepSeconds = 10;
        private const int HistoryMaxWindowSeconds = 12 * 60 * 60;
        private const int HistoryMinWindowSeconds = 10 * 60;
        private const int HistoryMaxSamples = HistoryMaxWindowSeconds / HistorySampleStepSeconds;
        private const int HistoryMinSamples = HistoryMinWindowSeconds / HistorySampleStepSeconds;
        private int _historyTickCounter;

        private readonly int UpdateIntervalMillis = 1000;

        private const int A2SPollIntervalSeconds = 10;
        private int _a2sPollTickCounter;
        private int _a2sPollInFlight;

        private DispatcherTimer? _updateTimer;

        private void UpdateLaunchOptionsValues() {
            var opts = Server.LaunchOptions;
            GamePort = opts.GamePort;
            BeaconPort = opts.BeaconPort;
            QueryPort = opts.QueryPort;
            RconPort = opts.RconPort;

            LocalIpText = opts.LocalIp.IfNone("")?.Trim();
            if (string.IsNullOrWhiteSpace(LocalIpText)) {
                LocalIpText = "-";
            }

            var actors = opts.NextMapModActors?.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray() ?? Array.Empty<string>();
            ModActorsText = actors.Length == 0 ? "-" : string.Join(", ", actors);

            if (string.IsNullOrWhiteSpace(SelectedMapToChange)) {
                SelectedMapToChange = opts.Map;
            }
        }

        [RelayCommand(CanExecute = nameof(CanChangeMap))]
        private async Task ChangeMapAsync() {
            var map = SelectedMapToChange?.Trim();
            if (string.IsNullOrWhiteSpace(map)) return;

            try {
                await Server.RCON.SendCommand($"servertravel {map}");
            }
            catch (Exception ex) {
                Logger.Error($"Failed to change map via RCON: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task StartNextMapAsync() {
            try {
                await Server.RCON.SendCommand("tbsendgame 1");
            }
            catch (Exception ex) {
                Logger.Error($"Failed to start next map via RCON: {ex.Message}");
            }
        }

        public void ReplaceServer(Chivalry2Server newServer, bool countAsRestart) {
            try {
                Server.Dispose();
            }
            catch {
                // ignore
            }

            Server = newServer;

            if (countAsRestart) {
                RestartCount++;
            }

            var appDispatcher = Application.Current?.Dispatcher;
            if (_updateTimer == null) {
                StartUpdateLoop();
            }
            else if (appDispatcher != null && _updateTimer.Dispatcher != appDispatcher) {
                _updateTimer.Stop();
                _updateTimer = null;
                StartUpdateLoop();
            }

            UpdateLaunchOptionsValues();

            IsUp = true;
            LastStartTime = DateTimeOffset.Now;
            HasA2SInfo = false;
            _hasSeenA2SSuccessSinceStart = false;
        }

        public void StartUpdateLoop() {
            if (_updateTimer != null) return;

            // Ensure launch-option derived fields are populated as soon as the VM is activated.
            UpdateLaunchOptionsValues();

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            _updateTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) {
                Interval = TimeSpan.FromMilliseconds(UpdateIntervalMillis)
            };
            _updateTimer.Tick += (_, _) => UpdateUsageValues();
            _updateTimer.Start();
        }

        public async Task PollA2S() {
            if (!IsUp) {
                HasA2SInfo = false;
                return;
            }

            // Prevent overlapping polls in case a request takes longer than the poll interval.
            if (Interlocked.Exchange(ref _a2sPollInFlight, 1) == 1) return;

            try {
                var a2sInfo = await Server.A2S.InfoAsync();
                PlayerCount = a2sInfo.Players;
                BotCount = a2sInfo.Bots;
                MaxPlayers = a2sInfo.MaxPlayers;
                GameType = a2sInfo.GameType;
                CurrentMap = a2sInfo.Map;
                HasA2SInfo = true;
                _hasSeenA2SSuccessSinceStart = true;
            }
            catch (Exception ex) {
                HasA2SInfo = false;
                if (_hasSeenA2SSuccessSinceStart)
                    Logger.Error($"Failed to poll A2S: {ex.Message}");
            }
            finally {
                Interlocked.Exchange(ref _a2sPollInFlight, 0);
            }
        }

        public void UpdateUsageValues() {
            try {
                var process = Server.ServerProcess;
                if (process.HasExited) {
                    IsUp = false;
                    HasA2SInfo = false;
                    _hasSeenA2SSuccessSinceStart = false;
                    UpdateUptimeValues();
                    UpdateHistory();
                    return;
                }

                // Process memory properties are cached; Refresh() ensures values update.
                process.Refresh();

                HandleCount = process.HandleCount;
                PrivateMemorySize64 = process.PrivateMemorySize64;
                if (PrivateMemorySize64 > PeakPrivateMemorySize64) PeakPrivateMemorySize64 = PrivateMemorySize64;

                IsUp = true;

                _a2sPollTickCounter++;
                if (_a2sPollTickCounter % A2SPollIntervalSeconds == 0) {
                    PollA2S();
                }

                UpdateUptimeValues();
                UpdateHistory();
            }
            catch (ObjectDisposedException) {
                // Can occur if the underlying Process is disposed during a restart/replace cycle.
                IsUp = false;
                HasA2SInfo = false;
                _hasSeenA2SSuccessSinceStart = false;
                UpdateUptimeValues();
                UpdateHistory();
            }
            catch (InvalidOperationException) {
                // Process may not be started / may have exited between checks.
                IsUp = false;
                HasA2SInfo = false;
                _hasSeenA2SSuccessSinceStart = false;
                UpdateUptimeValues();
                UpdateHistory();
            }
        }

        private void UpdateUptimeValues() {
            if (!IsUp) {
                CurrentUptimeText = "00:00:00";
                return;
            }

            var uptime = DateTimeOffset.Now - LastStartTime;
            if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;
            CurrentUptimeText = uptime.ToString(@"hh\:mm\:ss");
        }

        private void UpdateHistory() {
            _historyTickCounter++;
            if (_historyTickCounter % HistorySampleStepSeconds != 0) return;

            var mib = PrivateMemorySize64 / (1024d * 1024d);
            AppendRolling(PrivateMemoryHistoryMiB, mib, HistoryMaxSamples);

            UptimeState uptimeState;
            if (!IsUp) {
                uptimeState = UptimeState.Down;
            }
            else if (_hasSeenA2SSuccessSinceStart) {
                uptimeState = UptimeState.Up;
            }
            else {
                uptimeState = UptimeState.Starting;
            }
            AppendRolling(UpDownHistory, uptimeState, HistoryMaxSamples);

            // Player count comes from A2S polls; use null when unavailable.
            double? playersSample = HasA2SInfo ? PlayerCount : null;
            AppendRolling(PlayerCountHistory, playersSample, HistoryMaxSamples);

            UpdateDisplayHistories();
        }

        private void UpdateDisplayHistories() {
            var recorded = new[] { PrivateMemoryHistoryMiB.Count, UpDownHistory.Count, PlayerCountHistory.Count }.Max();
            var window = Math.Clamp(recorded, HistoryMinSamples, HistoryMaxSamples);

            FillDisplay(DisplayPrivateMemoryHistoryMiB, PrivateMemoryHistoryMiB, window);
            FillDisplay(DisplayUpDownHistory, UpDownHistory, window);
            FillDisplay(DisplayPlayerCountHistory, PlayerCountHistory, window);

            DisplayPrivateMemoryHistoryMaxMiB = DisplayPrivateMemoryHistoryMiB
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .DefaultIfEmpty(0d)
                .Max();

            // Peak is still derived from the full rolling 12h buffer.
            var peakMib = PrivateMemoryHistoryMiB
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .DefaultIfEmpty(0d)
                .Max();
            PeakPrivateMemorySize64 = (long)(peakMib * 1024d * 1024d);

            DisplayPlayerCountHistoryMax = DisplayPlayerCountHistory
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .DefaultIfEmpty(0d)
                .Max();
        }

        private static void AppendRolling<T>(ObservableCollection<T> collection, T item, int maxItems) {
            if (collection.Count >= maxItems) {
                collection.RemoveAt(0);
            }
            collection.Add(item);
        }

        private static void FillDisplay<T>(ObservableCollection<T> target, ObservableCollection<T> source, int window) {
            target.Clear();

            var missing = Math.Max(0, window - source.Count);
            for (var i = 0; i < missing; i++) {
                target.Add(default!);
            }

            var start = Math.Max(0, source.Count - window);
            for (var i = start; i < source.Count; i++) {
                target.Add(source[i]);
            }
        }


        public void DisposeServer() {
            if (_updateTimer == null) return;

            _updateTimer?.Stop();
            _updateTimer = null;
            Server.Dispose();
        }
    }
}