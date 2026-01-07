using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Server;
using UnchainedLauncher.Core.Services.Server.A2S;
using UnchainedLauncher.GUI.ViewModels.ServersTab;
using static LanguageExt.Prelude;
using Environment = UnchainedLauncher.Core.Services.Server.A2S.Environment;

namespace UnchainedLauncher.GUI.Views.Servers.DesignInstances {
    public static class ServerViewModelInstances {
        public static ServerVM DEFAULT => new ServerDesignVM();
    }

    public class MockA2S(A2SInfo info) : IA2S {
        public Task<A2SInfo> InfoAsync() {
            return Task.FromResult(info);
        }
    }

    public class MockRcon() : IRCON {
        public string RconLocation { get; } = "mock://localhost:27020";
        public Task SendCommand(string command) {
            return Task.CompletedTask;
        }
    }

    public class ServerDesignVM : ServerVM {
        public ServerDesignVM() : base(
            new Chivalry2Server(
                Process.GetCurrentProcess(),
                new ServerLaunchOptions(
                    Headless: true,
                    Name: "Example Server",
                    Description: "",
                    Password: None,
                    Map: "FFA_Wardenglade",
                    GamePort: 7777,
                    BeaconPort: 15000,
                    QueryPort: 27015,
                    RconPort: 27020,
                    LocalIp: Some("127.0.0.1"),
                    NextMapModActors: new[] { "ExampleActorModA", "ExampleActorModB" }
                ),
                new MockA2S(new A2SInfo(
                    0,
                    "Example Server",
                    "FFA_Wardenglade",
                    "FFA",
                    "Chivalry 2",
                    2,
                    12,
                    64,
                    0,
                    ServerType.Dedicated,
                    Environment.Windows,
                    true,
                    false
                )), new MockRcon()
            ),
            "Example Server",
            new ObservableCollection<string>() { "FFA_Wardenglade", "TDM_Wardenglade" }
        ) {
            RestartCount = 2;
            LastStartTime = DateTimeOffset.Now.AddMinutes(-17);
            IsUp = true;

            // Seed A2S values for the designer.
            PlayerCount = 12;
            MaxPlayers = 64;
            CurrentMap = "FFA_Wardenglade";
            GameType = "Free For All";
            HasA2SInfo = true;


            // Seed some recent samples.
            var rnd = new Random(1);
            var baseline = 4096d;
            var recentSamples = 360; // last hour at 10s sampling
            for (var i = 0; i < recentSamples; i++) {
                var wave = Math.Sin(i / 20d) * 250d;
                var noise = (rnd.NextDouble() - 0.5d) * 120d;
                var val = Math.Max(0d, baseline + wave + noise);
                PrivateMemoryHistoryMiB.Add(val);

                // Simulate players varying over time.
                var players = Math.Clamp(12 + (int)Math.Round(Math.Sin(i / 25d) * 8d) + rnd.Next(-2, 3), 0, 64);
                PlayerCountHistory.Add(players);

                // Simulate: starting (yellow) briefly after boot, then up (blue), with a couple brief downtimes (red).
                var state = UptimeState.Up;
                if (i < 18) state = UptimeState.Starting;
                if (i is > 70 and < 80) state = UptimeState.Down;
                if (i is > 200 and < 212) state = UptimeState.Down;
                UpDownHistory.Add(state);
            }

            // Populate display histories with a 12h label but dynamic-window behavior.
            // With only 1 hour of samples, the display should show 1 hour (>= 10 minutes minimum).
            DisplayPrivateMemoryHistoryMiB.Clear();
            foreach (var v in PrivateMemoryHistoryMiB) DisplayPrivateMemoryHistoryMiB.Add(v);
            DisplayPrivateMemoryHistoryMaxMiB = DisplayPrivateMemoryHistoryMiB.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0d).Max();

            DisplayUpDownHistory.Clear();
            foreach (var v in UpDownHistory) DisplayUpDownHistory.Add(v);

            DisplayPlayerCountHistory.Clear();
            foreach (var v in PlayerCountHistory) DisplayPlayerCountHistory.Add(v);
            DisplayPlayerCountHistoryMax = DisplayPlayerCountHistory.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0d).Max();

            PrivateMemorySize64 = (long)((PrivateMemoryHistoryMiB.LastOrDefault(v => v.HasValue) ?? 0d) * 1024d * 1024d);
            PeakPrivateMemorySize64 = (long)(PrivateMemoryHistoryMiB.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0d).Max() * 1024d * 1024d);
        }
    }
}