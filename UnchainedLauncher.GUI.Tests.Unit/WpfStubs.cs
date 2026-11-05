// Only the WPF services used by the linked view models are replaced; commands and
// PropertyChanged weaving run unchanged in these portable tests.
namespace System.ComponentModel {
    internal static class PropertyChangedEventManager {
        public static void AddHandler(INotifyPropertyChanged source, EventHandler<PropertyChangedEventArgs> handler, string propertyName) {
            source.PropertyChanged += (sender, args) => {
                if (args.PropertyName == propertyName) handler(sender, args);
            };
        }
    }
}

namespace System.Windows {
    internal static class MessageBox {
        public static void Show(string message) => throw new NotSupportedException("Unexpected message box: " + message);
    }
}
