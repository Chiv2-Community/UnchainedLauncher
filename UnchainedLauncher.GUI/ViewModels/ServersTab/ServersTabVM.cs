using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;


namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    [AddINotifyPropertyChangedInterface]
    public class ServersTabVM {
        //[AddINotifyPropertyChangedInterface]
        //public class DropdownItem {
        //    public string Name => Value.Form.Name;
        //    public ServerTemplateVM Value { get; }
        //    public DropdownItem(ServerTemplateVM value) {
        //        Value = value;
        //    }
        //}
        public ServersVM RunningServers { get; }
        public ObservableCollection<ServerTemplateVM> ServerTemplates { get; }
        public ServerTemplateVM? SelectedTemplate { get; set; }
        public ICommand Add_Template_Command { get; }
        public ICommand Remove_Template_Command { get; }
        public ServersTabVM(ServersVM servers, ObservableCollection<ServerTemplateVM>? templates = null) {
            if (templates == null) {
                ServerTemplates = new ObservableCollection<ServerTemplateVM>();
            }
            else {
                ServerTemplates = templates;
            }
            SelectedTemplate = ServerTemplates.FirstOrDefault();
            RunningServers = servers;
            Add_Template_Command = new RelayCommand(() => {
                var newTemplate = new ServerTemplateVM();
                HashSet<int> occupiedPorts = new();
                foreach (var t in ServerTemplates) {
                    occupiedPorts.Add(t.Form.GamePort);
                    occupiedPorts.Add(t.Form.PingPort);
                    occupiedPorts.Add(t.Form.RconPort);
                    occupiedPorts.Add(t.Form.A2sPort);
                }

                // Get next number after start that isn't in numbers
                // additionally, adds that number to the set
                static int reserveNextNotIn(int start, HashSet<int> numbers) {
                    while (!numbers.Add(++start));
                    return start;
                }

                // try to make the new template nice
                if (SelectedTemplate != null) {
                    // increment ports so added server isn't outright incompatible
                    var oldForm = SelectedTemplate.Form;
                    var newForm = newTemplate.Form;
                    newForm.GamePort = reserveNextNotIn(oldForm.GamePort, occupiedPorts);
                    newForm.PingPort = reserveNextNotIn(oldForm.PingPort, occupiedPorts);
                    newForm.A2sPort = reserveNextNotIn(oldForm.A2sPort, occupiedPorts);
                    newForm.RconPort = reserveNextNotIn(oldForm.RconPort, occupiedPorts);

                    // increment name in a similar way, for user cleanliness
                    if (Regex.IsMatch(oldForm.Name, "\\((\\s*)(\\d+)(\\s*)\\)")) {
                        newForm.Name = Regex.Replace(
                            oldForm.Name,
                            "\\((\\s*)(\\d+)(\\s*)\\)",
                            (Match match) => 
                                $"({match.Groups[1].Value}{int.Parse(match.Groups[2].Value) + 1}{match.Groups[3].Value})"
                        );
                    }
                    else {
                        newForm.Name = $"{newForm.Name} (2)";
                    }
                    
                }

                ServerTemplates.Add(newTemplate);
                SelectedTemplate = newTemplate;
            });
            Remove_Template_Command = new RelayCommand(() => {
                if (SelectedTemplate != null) {
                    ServerTemplates.Remove(SelectedTemplate);
                }
                SelectedTemplate = ServerTemplates.FirstOrDefault();
            });
        }
    }
}
