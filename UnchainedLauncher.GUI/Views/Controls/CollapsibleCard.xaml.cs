using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace UnchainedLauncher.GUI.Views.Controls {
    [ContentProperty(nameof(Body))]
    public partial class CollapsibleCard : UserControl {
        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(object), typeof(CollapsibleCard), new PropertyMetadata(null));

        public static readonly DependencyProperty HeaderTemplateProperty = DependencyProperty.Register(
            nameof(HeaderTemplate), typeof(DataTemplate), typeof(CollapsibleCard), new PropertyMetadata(null));

        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
            nameof(IsExpanded), typeof(bool), typeof(CollapsibleCard), new PropertyMetadata(true));

        public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
            nameof(Body), typeof(object), typeof(CollapsibleCard), new PropertyMetadata(null));

        public CollapsibleCard() {
            InitializeComponent();
        }

        public object? Header {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public DataTemplate? HeaderTemplate {
            get => (DataTemplate?)GetValue(HeaderTemplateProperty);
            set => SetValue(HeaderTemplateProperty, value);
        }

        public bool IsExpanded {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public object? Body {
            get => GetValue(BodyProperty);
            set => SetValue(BodyProperty, value);
        }
    }
}
