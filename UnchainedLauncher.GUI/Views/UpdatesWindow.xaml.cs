using log4net;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    ///     Interaction logic for MessageBoxEx.xaml
    /// </summary>
    public partial class UpdatesWindow : UnchainedWindow {
        private static readonly ILog logger = LogManager.GetLogger(nameof(UpdatesWindow));

        public UpdatesWindow(string titleText, string messageText, string yesButtonText, string noButtonText,
            string? cancelButtonText, IEnumerable<DependencyUpdateViewModel> updates) {
            ViewModel = new UpdatesWindowVM(titleText, messageText, yesButtonText, noButtonText, cancelButtonText,
                updates, Close);
            DataContext = ViewModel;
            InitializeComponent();
        }

        public UpdatesWindowVM ViewModel { get; }

        private void SizeForCards(object sender, RoutedEventArgs e) {
            Loaded -= SizeForCards;
            if (Width != 640 || Height != 600) return;
            var heights = Enumerable.Range(0, Cards.Items.Count).Take(4)
                .Select(index => (Cards.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement)?.ActualHeight ?? 112)
                .ToArray();
            var viewportHeight = heights.Select((height, index) => height * (index == 3 ? 0.5 : 1)).Sum();
            if (Cards.Items.Count < 4) viewportHeight += 1; // Avoid a scrollbar from fractional-pixel rounding.
            Height = System.Math.Min(ActualHeight - CardsViewport.ActualHeight + viewportHeight, SystemParameters.WorkArea.Height);
        }

        public static MessageBoxResult? Show(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdateViewModel> updates) {
            if (!updates.Any()) {
                logger.Info("No updates available");
                return null;
            }

            var message = $"Found {updates.Count()} updates available.\n\n";
            message += string.Join("\n",
                updates.Select(x => $"- {x.Update.Name} {x.Update.CurrentVersion} -> {x.Update.LatestVersion}"));
            message.Split("\n").ToList().ForEach(x => logger.Info(x));

            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(candidate => candidate.IsActive)
                ?? Application.Current?.MainWindow;
            var window = new UpdatesWindow(titleText, messageText, yesButtonText, noButtonText, cancelButtonText,
                updates);
            if (owner?.IsVisible == true) window.Owner = owner;
            window.ShowDialog();

            var result = window.ViewModel.Result;
            logger.Info("User Selects: " + result);
            return result;
        }

        public static MessageBoxResult? Show(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, params DependencyUpdateViewModel[] updates) {
            return Show(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, updates.AsEnumerable());
        }
    }
}