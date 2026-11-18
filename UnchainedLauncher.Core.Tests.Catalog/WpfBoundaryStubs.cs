using System.ComponentModel;
using System.Globalization;

namespace System.ComponentModel {
    internal static class PropertyChangedEventManager {
        public static void AddHandler(INotifyPropertyChanged source, EventHandler<PropertyChangedEventArgs> handler, string propertyName) {
            source.PropertyChanged += (sender, args) => {
                if (args.PropertyName == propertyName) handler(sender, args);
            };
        }
    }
}

namespace System.Windows.Data {
    public interface IValueConverter {
        object Convert(object value, Type targetType, object parameter, CultureInfo culture);
        object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture);
    }
}

namespace System.Windows {
    internal static class WeakEventManager<TSource, TArgs> where TArgs : EventArgs {
        public static void AddHandler(TSource source, string eventName, EventHandler<TArgs> handler) {
            var eventInfo = typeof(TSource).GetEvent(eventName)!;
            eventInfo.AddEventHandler(source, Delegate.CreateDelegate(eventInfo.EventHandlerType!, handler.Target, handler.Method));
        }
    }
}

namespace System.Windows.Threading {
    // These tests use the real catalog/cache/scan code, not the WPF watcher timer.
    // Fail explicitly if a test accidentally enters a dispatcher-only path.
    public sealed class Dispatcher {
        public static Dispatcher CurrentDispatcher => throw new NotSupportedException("Requires WPF");
        public void InvokeAsync(Action callback) => throw new NotSupportedException("Requires WPF");
    }

    public sealed class DispatcherTimer {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => throw new NotSupportedException("Requires WPF");
        public void Stop() => throw new NotSupportedException("Requires WPF");
    }
}
