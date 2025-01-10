using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using PropertyChanged;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    [AddINotifyPropertyChangedInterface]
    public class ServersTabVM {
        public ServersVM RunningServers { get; }
        public ObservableCollection<ServerTemplateVM> ServerTemplates { get; }
        public ObservableCollection<(ServerTemplateVM template, ServerViewModel live)> RunningTemplates { get; } = new();
        public ServerTemplateVM? SelectedTemplate { get; set; }
        public ServerViewModel? SelectedLive => 
            RunningTemplates.Choose(
                (e) => e.template == SelectedTemplate ? e.live : Option<ServerViewModel>.None
            ).FirstOrDefault();
        public bool IsSelectedRunning => SelectedLive != null;
        
        public ICommand Add_Template_Command { get; }
        public ICommand Remove_Template_Command { get; }
        public ServersTabVM(ServersVM servers, ObservableCollection<ServerTemplateVM>? templates = null) {
            ServerTemplates = templates ?? new();
            SelectedTemplate = ServerTemplates.FirstOrDefault();
            RunningServers = servers;
            Add_Template_Command = new RelayCommand(Add_Template);
            Remove_Template_Command = new RelayCommand(() => {
                if (SelectedTemplate != null) {
                    ServerTemplates.Remove(SelectedTemplate);
                }
                SelectedTemplate = ServerTemplates.FirstOrDefault();
            });
        }

        private void Add_Template() {
            var newTemplate = new ServerTemplateVM();
            var occupiedPorts = ServerTemplates.Select(
                (e) => new Set<int>( new List<int> {
                    e.Form.A2sPort,
                    e.Form.RconPort,
                    e.Form.PingPort,
                    e.Form.GamePort
                })
            ).Aggregate(Set<int>.Empty, (s1, s2) => s1.AddRange(s2));

            // try to make the new template nice
            if (SelectedTemplate != null) {
                // increment ports so that added server is not incompatible with other templates
                var oldForm = SelectedTemplate.Form;
                var newForm = newTemplate.Form;
                (newForm.GamePort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.GamePort, occupiedPorts);
                (newForm.PingPort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.PingPort, occupiedPorts);
                (newForm.A2sPort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.A2sPort, occupiedPorts);
                (newForm.RconPort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.RconPort, occupiedPorts);

                // increment name in a similar way, so the user doesn't get things confused
                newForm.Name = TextualSuccessor(oldForm.Name);
            }

            ServerTemplates.Add(newTemplate);
            SelectedTemplate = newTemplate;
        }

        public static (int, Set<int>) ReserveRestrictedSuccessor(int number, Set<int> excluded) {
            int next = RestrictedSuccessor(number, excluded);
            return (next, excluded.Add(next));
        }

        /// <summary>
        /// gets a number's successor, excluding any number in excluded
        /// </summary>
        /// <param name="number">the number to get the successor of</param>
        /// <param name="excluded">numbers that should not be returned</param>
        /// <returns>number's next successor not in excluded</returns>
        public static int RestrictedSuccessor(int number, Set<int> excluded) {
            while (excluded.Contains(++number)) ;
            return number;
        }

        /// <summary>
        /// Return a string which is the textual successor. 
        /// This means incrementing any counting numbers in the string.
        /// Ignores numbers not in parentheses, and preserves intervening whitespace.
        /// Examples:
        ///     "Test 1 string (1)" => "Test 1 string (2)"
        ///     "Test 1 string" => "Test 1 string (2)"
        /// </summary>
        /// <param name="text">The text to get a successor for</param>
        /// <returns>The successor text</returns>
        public static string TextualSuccessor(string text) {
            if (Regex.IsMatch(text, "\\((\\s*)(\\d+)(\\s*)\\)")) {
                return Regex.Replace(
                    text,
                    "\\((\\s*)(\\d+)(\\s*)\\)",
                    (Match match) =>
                        $"({match.Groups[1].Value}{int.Parse(match.Groups[2].Value) + 1}{match.Groups[3].Value})"
                );
            }
            return $"{text} (1)";
        }
    }
}
