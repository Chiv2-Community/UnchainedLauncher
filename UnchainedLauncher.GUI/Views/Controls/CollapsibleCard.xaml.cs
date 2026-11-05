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

        public static readonly DependencyProperty ShowExpansionIndicatorProperty = DependencyProperty.Register(
            nameof(ShowExpansionIndicator), typeof(bool), typeof(CollapsibleCard), new PropertyMetadata(true));

        public static readonly DependencyProperty HighlightWhenCollapsedProperty = DependencyProperty.Register(
            nameof(HighlightWhenCollapsed), typeof(bool), typeof(CollapsibleCard), new PropertyMetadata(false));

        public static readonly DependencyProperty ShowHeaderSeparatorProperty = DependencyProperty.Register(
            nameof(ShowHeaderSeparator), typeof(bool), typeof(CollapsibleCard), new PropertyMetadata(false));

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

        public bool ShowExpansionIndicator {
            get => (bool)GetValue(ShowExpansionIndicatorProperty);
            set => SetValue(ShowExpansionIndicatorProperty, value);
        }

        public bool HighlightWhenCollapsed {
            get => (bool)GetValue(HighlightWhenCollapsedProperty);
            set => SetValue(HighlightWhenCollapsedProperty, value);
        }

        public bool ShowHeaderSeparator {
            get => (bool)GetValue(ShowHeaderSeparatorProperty);
            set => SetValue(ShowHeaderSeparatorProperty, value);
        }
    }
}
