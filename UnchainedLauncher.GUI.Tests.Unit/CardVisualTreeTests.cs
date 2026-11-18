using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml.Linq;
using UnchainedLauncher.GUI.Views.Controls;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

// These tests run on Windows; the portable target checks the XAML structure instead.
public class CardVisualTreeTests {
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void NestedCardsKeepLayoutAndHitTargetsAtDifferentScales(double scale) => RunSta(() => {
        var card = CreateCard(out var nested, out var editor);
        var legacy = CreateCard(out var legacyNested, out _);
        RestoreLegacyBorder(legacyNested);
        RestoreLegacyBorder(legacy);
        card.LayoutTransform = new ScaleTransform(scale, scale);
        legacy.LayoutTransform = new ScaleTransform(scale, scale);
        using var host = new CardHost(card, legacy);

        Layout(card);
        Layout(legacy);
        Assert.Equal(legacy.DesiredSize, card.DesiredSize);
        var expandedHeight = card.DesiredSize.Height;
        AssertNoAncestorEffect(editor);

        var editorCenter = editor.TranslatePoint(new Point(editor.ActualWidth / 2, editor.ActualHeight / 2), card);
        AssertHitWithin(card, editorCenter, editor);
        var header = Descendants<ToggleButton>(nested).Single();
        Assert.True(header.IsChecked);
        var headerCenter = header.TranslatePoint(new Point(header.ActualWidth / 2, header.ActualHeight / 2), card);
        AssertHitWithin(card, headerCenter, header);

        header.IsChecked = false;
        legacyNested.IsExpanded = false;
        Layout(card);
        Layout(legacy);
        Assert.Equal(legacy.DesiredSize, card.DesiredSize);
        Assert.True(card.DesiredSize.Height < expandedHeight);
        var body = Descendants<ContentPresenter>(nested).Single(presenter => presenter.Content == editor);
        Assert.Equal(Visibility.Collapsed, body.Visibility);
        Assert.False(nested.IsExpanded);
        Assert.False(header.IsChecked);
    });

    [Fact]
    public void ThemeChangesKeepOnlyTheEmptySiblingShadowed() => RunSta(() => {
        var card = CreateCard(out _, out var editor);
        using var host = new CardHost(card);
        Layout(card);
        var borders = ((Grid)card.Content).Children.Cast<Border>().ToArray();
        Assert.Equal(2, borders.Length);
        Assert.Null(borders[0].Child);
        Assert.False(borders[0].IsHitTestVisible);
        Assert.IsType<DropShadowEffect>(borders[0].Effect);
        Assert.Null(borders[1].Effect);
        Assert.Equal(borders[0].RenderSize, borders[1].RenderSize);
        Assert.Equal(borders[0].Margin, borders[1].Margin);
        Assert.Equal(borders[0].CornerRadius, borders[1].CornerRadius);
        Assert.Equal(borders[0].BorderThickness, borders[1].BorderThickness);
        Assert.Equal(new Thickness(0), borders[0].Padding);
        Assert.True(borders[0].SnapsToDevicePixels);
        Assert.True(borders[1].SnapsToDevicePixels);

        var background = new SolidColorBrush(Colors.DarkBlue);
        var borderBrush = new SolidColorBrush(Colors.Orange);
        card.Resources["Brush.Background"] = background;
        card.Resources["Brush.Border"] = borderBrush;
        card.Resources["Card"] = LoadCardStyle();
        Layout(card);
        foreach (var border in borders) {
            Assert.Same(background, border.Background);
            Assert.Same(borderBrush, border.BorderBrush);
        }
        Assert.IsType<DropShadowEffect>(borders[0].Effect);
        Assert.Null(borders[1].Effect);
        AssertNoAncestorEffect(editor);
    });

    private static CollapsibleCard CreateCard(out CollapsibleCard nested, out TextBox editor) {
        editor = new TextBox { Text = "Server name", Width = 160, Height = 28 };
        nested = new CollapsibleCard { Header = "Server details", Body = editor };
        var card = new CollapsibleCard { Header = "Server configuration", Body = nested };
        card.Resources["Brush.Background"] = Brushes.White;
        card.Resources["Brush.Border"] = Brushes.Gray;
        card.Resources["Brush.TextPrimary"] = Brushes.Black;
        card.Resources["Card"] = LoadCardStyle();
        return card;
    }

    private static Style LoadCardStyle() {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "Misc.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = new XElement(document.Root!.Elements(wpf + "Style")
            .Single(element => (string?)element.Attribute(x + "Key") == "Card"));
        style.Add(new XAttribute(XNamespace.Xmlns + "x", x));
        return (Style)XamlReader.Parse(style.ToString());
    }

    private static void RestoreLegacyBorder(CollapsibleCard card) {
        var root = (Grid)card.Content;
        var border = root.Children.Cast<Border>().Last();
        root.Children.Remove(border);
        border.ClearValue(UIElement.EffectProperty);
        card.Content = border;
    }

    private static void Layout(FrameworkElement element) {
        element.UpdateLayout();
    }

    private sealed class CardHost : IDisposable {
        private readonly Window window;

        public CardHost(params CollapsibleCard[] cards) {
            var panel = new StackPanel { Width = 640 };
            foreach (var card in cards) panel.Children.Add(card);
            window = new Window {
                Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false
            };
            window.Show();
        }

        public void Dispose() => window.Close();
    }

    private static void AssertNoAncestorEffect(DependencyObject element) {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current)) {
            if (current is UIElement visual) Assert.Null(visual.Effect);
        }
    }

    private static void AssertHitWithin(UIElement root, Point point, Visual expected) {
        DependencyObject? current = root.InputHitTest(point) as DependencyObject;
        Assert.NotNull(current);
        while (current != null && current != expected) {
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        Assert.Same(expected, current);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void RunSta(Action action) {
        Exception? failure = null;
        var thread = new Thread(() => {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
