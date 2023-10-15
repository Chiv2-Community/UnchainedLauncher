using System.IO;
using System.Reflection;
using System.Windows;

namespace UnchainedLauncher.GUI {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        public App() : base() {
            var assembly = Assembly.GetExecutingAssembly();

            // First try to load a local "log4net.config" file, then try to load the embedded one.
            if (File.Exists("log4net.config")) {
                log4net.Config.XmlConfigurator.Configure(new FileInfo("log4net.config"));
            } else {
                using Stream? configStream = assembly.GetManifestResourceStream("UnchainedLauncherGUI.Resources.log4net.config");
                if (configStream != null) {
                    log4net.Config.XmlConfigurator.Configure(configStream);
                }
            }
        }
    }
}
