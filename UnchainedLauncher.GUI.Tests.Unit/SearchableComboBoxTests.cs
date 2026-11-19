using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Xml.Linq;
using UnchainedLauncher.GUI.Behaviors;
using UnchainedLauncher.GUI.Converters;
using UnchainedLauncher.GUI.ViewModels;
using Xunit.Abstractions;

namespace UnchainedLauncher.GUI.Tests.Unit;

[CollectionDefinition("WPF input", DisableParallelization = true)]
public class WpfInputCollection { }

[Collection("WPF input")]
public class SearchableComboBoxTests {
    private readonly ITestOutputHelper output;

    public SearchableComboBoxTests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [InlineData(Key.Home)]
    [InlineData(Key.End)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.PageUp)]
    [InlineData(Key.PageDown)]
    public void RoutedControlNavigationOnlyChangesProvisionalResults(Key key) => RunSta(() => {
        using var host = new Host(new[] { "Steam", "Epic Games Store", "Xbox Game Pass" });
        var model = BindMiddleSelection(host);
        host.Window.Activate();
        host.Combo.Focus();
        Press(host.Combo, key);
        Assert.Equal("Epic Games Store", host.Combo.SelectedItem);
        Assert.Equal("Epic Games Store", model.Selection);
        Assert.True(host.Combo.IsDropDownOpen);
        Assert.NotNull(host.Results.SelectedItem);
        Press(host.Box, Key.Down);
        Assert.Equal("Epic Games Store", model.Selection);
        var provisional = host.Results.SelectedItem;
        Press(host.Box, Key.Enter);
        Assert.Equal(provisional, model.Selection);
        Assert.True(BindingOperations.IsDataBound(host.Combo, ComboBox.SelectedItemProperty));
    });

    [Theory]
    [InlineData(120)]
    [InlineData(-120)]
    public void ClosedWheelScrollsParentWithoutCommitting(int delta) => RunSta(() => {
        using var host = new Host(new[] { "Steam", "Epic Games Store", "Xbox Game Pass" });
        var model = BindMiddleSelection(host);
        var panel = (StackPanel)host.Window.Content;
        host.Window.Content = null;
        panel.Children.Add(new Border { Height = 1000 });
        var scroll = new ScrollViewer { Content = panel };
        host.Window.Content = scroll;
        host.Window.UpdateLayout();
        host.Window.Activate();
        host.Combo.Focus();
        Pump();
        scroll.ScrollToVerticalOffset(60);
        host.Window.UpdateLayout();
        var before = scroll.VerticalOffset;
        Wheel(host.Combo, delta);
        host.Window.UpdateLayout();
        Assert.Equal("Epic Games Store", host.Combo.SelectedItem);
        Assert.Equal("Epic Games Store", model.Selection);
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.True(delta > 0 ? scroll.VerticalOffset < before : scroll.VerticalOffset > before,
            $"Wheel {delta}: offset {before} -> {scroll.VerticalOffset}");
    });

    [Fact]
    public void OpenWheelScrollsResultsWithoutCommitting() => RunSta(() => {
        using var host = new Host(new[] { "Steam", "Epic Games Store", "Xbox Game Pass" }
            .Concat(Enumerable.Range(0, 40).Select(i => $"Option {i}")));
        var model = BindMiddleSelection(host);
        host.Combo.IsDropDownOpen = true;
        host.Window.UpdateLayout();
        Pump();
        var item = (ListBoxItem)host.Results.ItemContainerGenerator.ContainerFromIndex(1);
        var scroll = MouseWheelScrollForwardingBehavior.FindAncestorScrollViewer(item)!;
        var before = scroll.VerticalOffset;
        Wheel(item, -120);
        host.Window.UpdateLayout();
        Assert.True(scroll.VerticalOffset > before);
        Assert.Equal("Epic Games Store", host.Combo.SelectedItem);
        Assert.Equal("Epic Games Store", model.Selection);
        Assert.True(host.Combo.IsDropDownOpen);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NativeInputPreservesSelectionCaretEditingAndTabTraversal(bool suppressPositioningMove, bool occludeWindow) => RunSta(() => {
        using var host = new Host(new[] { "Steam", "Epic Games Store", "Xbox Game Pass" });
        using var desktop = new NativeInputScope(host.Window);
        var model = BindMiddleSelection(host);
        host.Window.Left = 100;
        host.Window.Top = 100;
        host.Window.Activate();
        Pump();
        if (occludeWindow) {
            desktop.ShowObstruction();
            var coveredPoint = host.Combo.PointToScreen(new Point(host.Combo.ActualWidth / 2, host.Combo.ActualHeight / 2));
            Assert.True(SetCursorPos((int)coveredPoint.X, (int)coveredPoint.Y));
            Pump();
            Assert.Equal(desktop.ObstructionHandle, WindowFromPoint(new NativePoint { X = (int)coveredPoint.X, Y = (int)coveredPoint.Y }));
            Assert.Equal(desktop.ObstructionHandle, GetForegroundWindow());
            output.WriteLine($"Controlled obstruction: {DescribeWindow(GetForegroundWindow())}");
        }
        // Activation alone does not retain foreground ownership while the dispatcher pumps.
        // Raise above ordinary desktop windows, then acquire foreground through real user input.
        host.Window.Topmost = true;
        Pump();
        var activationPoint = host.Before.PointToScreen(new Point(host.Before.ActualWidth / 2, host.Before.ActualHeight / 2));
        Assert.True(SetCursorPos((int)activationPoint.X, (int)activationPoint.Y));
        Pump();
        var hwnd = new WindowInteropHelper(host.Window).Handle;
        Assert.True(GetCursorPos(out var activationCursor));
        output.WriteLine($"Activation hit: {DescribeWindow(WindowFromPoint(activationCursor))}; foreground: {DescribeWindow(GetForegroundWindow())}");
        Assert.Equal(hwnd, WindowFromPoint(activationCursor));
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        Pump();
        Assert.Equal(hwnd, GetForegroundWindow());
        Assert.True(host.Window.IsActive);
        host.Combo.Focus();
        Pump();
        var positioning = true;
        var nativeWheelEvents = 0;
        HwndSource.FromHwnd(hwnd).AddHook((IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled) => {
            if (message is 0x0200 or 0x020a)
                output.WriteLine($"Native 0x{message:x}: wParam=0x{wParam:x}, lParam=0x{lParam:x}");
            if (message == 0x020a) nativeWheelEvents++;
            // Exercise setup without relying on SetCursorPos's mouse-move notification.
            if (positioning && suppressPositioningMove && message == 0x0200) handled = true;
            return IntPtr.Zero;
        });
        var point = host.Combo.PointToScreen(new Point(host.Combo.ActualWidth / 2, host.Combo.ActualHeight / 2));
        Assert.True(SetCursorPos((int)point.X, (int)point.Y));
        Pump();
        positioning = false;
        // WPF routes wheel input through its mouse target, not its keyboard focus.
        // Cross a pixel so an already-active WPF mouse device cannot reuse its cached target.
        mouse_event(0x0001, 1, 0, 0, UIntPtr.Zero);
        Pump();
        Assert.True(host.Combo.IsMouseOver, "Native movement must establish the WPF wheel target.");
        Assert.True(SetCursorPos((int)point.X, (int)point.Y));
        Pump();
        Assert.True(GetCursorPos(out var cursor));
        output.WriteLine($"Wheel target={point}, cursor={cursor.X},{cursor.Y}, hit=0x{WindowFromPoint(cursor):x}, " +
            $"host=0x{hwnd:x}, foreground=0x{GetForegroundWindow():x}, focus=0x{GetFocus():x}, " +
            $"WPF focus={Keyboard.FocusedElement}, mouseOver={Mouse.DirectlyOver}, dpi={GetDpiForWindow(hwnd)}");
        output.WriteLine($"Wheel hit: {DescribeWindow(WindowFromPoint(cursor))}; foreground: {DescribeWindow(GetForegroundWindow())}");
        Assert.Equal((int)point.X, cursor.X);
        Assert.Equal((int)point.Y, cursor.Y);
        Assert.Equal(hwnd, WindowFromPoint(cursor));
        Assert.Equal(hwnd, GetForegroundWindow());
        Assert.Equal(hwnd, GetFocus());
        Assert.True(host.Combo.IsMouseOver, "Native movement must establish the WPF wheel target.");
        var wheelEvents = 0;
        host.Combo.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => wheelEvents++), true);
        foreach (var delta in new[] { 120, -120 }) {
            mouse_event(0x0800, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
            Pump();
            Assert.Equal("Epic Games Store", host.Combo.SelectedItem);
            Assert.Equal("Epic Games Store", model.Selection);
            Assert.False(host.Combo.IsDropDownOpen);
        }
        Assert.Equal(2, nativeWheelEvents);
        Assert.Equal(2, wheelEvents);
        foreach (var key in new byte[] { 0x24, 0x23, 0x25, 0x27, 0x26, 0x28, 0x21, 0x22 }) {
            host.Combo.IsDropDownOpen = false;
            host.Combo.Focus();
            Pump();
            Assert.Same(host.Combo, Keyboard.FocusedElement);
            NativeKey(key);
            Assert.Equal("Epic Games Store", host.Combo.SelectedItem);
            Assert.Equal("Epic Games Store", model.Selection);
            Assert.True(host.Combo.IsDropDownOpen);
            NativeKey(0x1b);
            Assert.False(host.Combo.IsDropDownOpen);
        }
        NativeKey(0x73);
        Assert.True(host.Combo.IsDropDownOpen);
        host.Box.Text = "xgp";
        NativeKey(0x73);
        Assert.False(host.Combo.IsDropDownOpen);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        try { NativeKey(0x28); }
        finally { keybd_event(0x12, 0, 2, UIntPtr.Zero); }
        Assert.True(host.Combo.IsDropDownOpen);
        host.Box.Text = "xgp";
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        try { NativeKey(0x26); }
        finally { keybd_event(0x12, 0, 2, UIntPtr.Zero); }
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.Equal("Epic Games Store", model.Selection);
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "Epic Games";
        host.Box.Focus();
        host.Box.CaretIndex = 4;
        NativeKey(0x25);
        Assert.Equal(3, host.Box.CaretIndex);
        NativeKey(0x27);
        Assert.Equal(4, host.Box.CaretIndex);
        NativeKey(0x24);
        Assert.Equal(0, host.Box.CaretIndex);
        NativeKey(0x25);
        Assert.Equal(0, host.Box.CaretIndex);
        NativeKey(0x23);
        Assert.Equal(host.Box.Text.Length, host.Box.CaretIndex);
        NativeKey(0x27);
        Assert.Equal(host.Box.Text.Length, host.Box.CaretIndex);
        NativeKey(0x08);
        Assert.Equal("Epic Game", host.Box.Text);
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        try { NativeKey(0x24); }
        finally { keybd_event(0x11, 0, 2, UIntPtr.Zero); }
        Assert.Equal(0, host.Box.CaretIndex);
        keybd_event(0x10, 0, 0, UIntPtr.Zero);
        try { NativeKey(0x27); }
        finally { keybd_event(0x10, 0, 2, UIntPtr.Zero); }
        Assert.Equal("E", host.Box.SelectedText);
        Assert.Equal("Epic Games Store", model.Selection);
        NativeKey(0x09);
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.Same(host.After, Keyboard.FocusedElement);
        host.Combo.Focus();
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "xgp";
        host.Box.Focus();
        Pump();
        keybd_event(0x10, 0, 0, UIntPtr.Zero);
        try { NativeKey(0x09); }
        finally { keybd_event(0x10, 0, 2, UIntPtr.Zero); }
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.Same(host.Before, Keyboard.FocusedElement);
        Assert.Equal("Epic Games Store", model.Selection);
        host.Combo.Focus();
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "xgp";
        host.Box.Focus();
        Pump();
        NativeKey(0x0d);
        Assert.Equal("Xbox Game Pass", model.Selection);
    });

    private static SelectionModel BindMiddleSelection(Host host) {
        var model = new SelectionModel { Selection = "Epic Games Store" };
        host.Combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SelectionModel.Selection)) {
            Source = model,
            Mode = BindingMode.TwoWay
        });
        return model;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    private static string DescribeWindow(IntPtr window) {
        var title = new StringBuilder(256);
        var className = new StringBuilder(256);
        GetWindowText(window, title, title.Capacity);
        GetClassName(window, className, className.Capacity);
        var thread = GetWindowThreadProcessId(window, out var process);
        return $"HWND=0x{window:x}, pid={process}, tid={thread}, class={className}, title={title}";
    }

    private sealed class NativeInputScope : IDisposable {
        private readonly Window window;
        private readonly bool topmost;
        private readonly IntPtr foreground = GetForegroundWindow();
        private readonly NativePoint cursor;
        private Window? obstruction;

        public IntPtr ObstructionHandle => new WindowInteropHelper(obstruction!).Handle;

        public NativeInputScope(Window window) {
            this.window = window;
            topmost = window.Topmost;
            Assert.True(GetCursorPos(out cursor));
        }

        public void ShowObstruction() {
            obstruction = new Window {
                Title = "Native input ownership regression",
                Left = window.Left,
                Top = window.Top,
                Width = window.Width,
                Height = window.Height,
                ShowInTaskbar = false
            };
            obstruction.Show();
            obstruction.Activate();
            Pump();
        }

        public void Dispose() {
            window.Topmost = topmost;
            obstruction?.Close();
            SetCursorPos(cursor.X, cursor.Y);
            if (foreground != IntPtr.Zero) SetForegroundWindow(foreground);
        }
    }

    private static void NativeKey(byte key) {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
        Pump();
    }

    private static void Pump() {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void Wheel(UIElement target, int delta) {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        target.RaiseEvent(args);
        if (!args.Handled) {
            args.RoutedEvent = Mouse.MouseWheelEvent;
            target.RaiseEvent(args);
        }
    }

    [Fact]
    public void FilteringPreservesBindingUntilEnterOrMouseCommit() => RunSta(() => {
        var items = new ObservableCollection<string> { "Steam", "Epic Games Store", "Xbox Game Pass" };
        using var host = new Host(items);
        var model = new SelectionModel { Selection = "Steam" };
        host.Combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SelectionModel.Selection)) {
            Source = model,
            Mode = BindingMode.TwoWay
        });
        var other = new ComboBox { ItemsSource = items, SelectedItem = "Steam" };
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "egs";
        Assert.Equal("Epic Games Store", Assert.Single(host.Results.Items.Cast<string>()));
        Assert.Equal("Steam", model.Selection);
        Assert.Equal(3, other.Items.Count);
        Assert.Equal("Steam", other.SelectedItem);
        Press(host.Box, Key.Enter);
        Assert.Equal("Epic Games Store", model.Selection);
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.Empty(host.Box.Text);
        Assert.True(BindingOperations.IsDataBound(host.Combo, ComboBox.SelectedItemProperty));
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "no matches";
        Press(host.Box, Key.Enter);
        Assert.Equal("Epic Games Store", model.Selection);
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "xgp";
        host.Window.UpdateLayout();
        var item = (ListBoxItem)host.Results.ItemContainerGenerator.ContainerFromIndex(0);
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
            RoutedEvent = Mouse.PreviewMouseUpEvent
        });
        Assert.Equal("Xbox Game Pass", model.Selection);
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.True(BindingOperations.IsDataBound(host.Combo, ComboBox.SelectedItemProperty));
    });

    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.Tab)]
    public void CancelPreservesSelectionAndTabTraversal(Key key) => RunSta(() => {
        using var host = new Host(new[] { "Steam", "Epic Games Store" });
        host.Combo.SelectedIndex = 0;
        host.Window.Activate();
        host.Combo.Focus();
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "egs";
        var args = Press(host.Box, key);
        Assert.False(host.Combo.IsDropDownOpen);
        Assert.Equal("Steam", host.Combo.SelectedItem);
        Assert.Empty(host.Box.Text);
        if (key == Key.Tab) {
            Assert.False(args.Handled);
            foreach (var direction in new[] { FocusNavigationDirection.Next, FocusNavigationDirection.Previous }) {
                host.Combo.Focus();
                Assert.True(host.Combo.MoveFocus(new TraversalRequest(direction)));
                Assert.Same(direction == FocusNavigationDirection.Next ? host.After : host.Before, Keyboard.FocusedElement);
            }
        }
    });

    [Fact]
    public void VisibleLabelsSortWithoutChangingSourceAndRefreshLiveItems() => RunSta(() => {
        var zulu = new Choice("Zulu map");
        var beta = new Choice("Beta map");
        var alpha = new Choice("alpha map");
        var items = new ObservableCollection<Choice> { zulu, beta, alpha };
        using var host = new Host(items);
        host.Combo.DisplayMemberPath = nameof(Choice.Label);
        SearchableComboBoxBehavior.SetSortAlphabetically(host.Combo, true);
        host.Combo.SelectedItem = zulu;
        host.Combo.IsDropDownOpen = true;
        Assert.Equal(new[] { alpha, beta, zulu }, host.Results.Items.Cast<Choice>());
        host.Box.Text = "map";
        Assert.Equal(3, host.Results.Items.Count);
        var aardvark = new Choice("Aardvark map");
        items.Add(aardvark);
        Assert.Equal(new[] { aardvark, alpha, beta, zulu }, host.Results.Items.Cast<Choice>());
        Assert.Equal(new[] { zulu, beta, alpha, aardvark }, items);
        host.Box.Text = "beta";
        Assert.Same(beta, Assert.Single(host.Results.Items.Cast<Choice>()));
        Press(host.Box, Key.Escape);
        Assert.Same(zulu, host.Combo.SelectedItem);
        host.Combo.IsDropDownOpen = true;
        Assert.Equal(4, host.Results.Items.Count);
    });

    [Fact]
    public void PluginSortSearchUsesConvertedLabelAndParameter() => RunSta(() => {
        using var host = new Host(Enum.GetValues<ModSortMode>());
        SearchableComboBoxBehavior.SetTextConverter(host.Combo, new ModSortModeToStringConverter());
        SearchableComboBoxBehavior.SetTextConverterParameter(host.Combo, "Plugins");
        host.Combo.IsDropDownOpen = true;
        host.Box.Text = "plugins";
        Assert.Equal(ModSortMode.NewestModsFirst, Assert.Single(host.Results.Items.Cast<ModSortMode>()));
        host.Box.Text = "mods";
        Assert.Empty(host.Results.Items);
    });

    [Fact]
    public void ResultSelectionTracksThemeEvenWithoutKeyboardFocus() => RunSta(() => {
        using var host = new Host(new[] { "Selected option" });
        host.Combo.IsDropDownOpen = true;
        host.Window.UpdateLayout();
        var item = (ListBoxItem)host.Results.ItemContainerGenerator.ContainerFromIndex(0);
        item.ApplyTemplate();
        var border = (Border)item.Template.FindName("ResultBorder", item);
        foreach (var theme in new[] { "DarkConstants", "LightConstants" }) {
            var constants = (ResourceDictionary)XamlReader.Parse(ProgressPresentationTests.Load(theme).ToString());
            host.Combo.Resources.MergedDictionaries.Clear();
            host.Combo.Resources.MergedDictionaries.Add(constants);
            Assert.Same(constants["Brush.PrimaryTransparent"], border.Background);
            Assert.Same(constants["Brush.Primary"], border.BorderBrush);
            Assert.Same(constants["Brush.TextPrimary"], item.Foreground);
        }
    });

    private sealed class Host : IDisposable {
        public ComboBox Combo { get; }
        public TextBox Box => (TextBox)Combo.Template.FindName("PART_SearchBox", Combo);
        public ListBox Results => (ListBox)Combo.Template.FindName("PART_SearchResults", Combo);
        public Window Window { get; }
        public Button Before { get; } = new() { Content = "Before" };
        public Button After { get; } = new() { Content = "After" };

        public Host(IEnumerable items) {
            var document = ProgressPresentationTests.Load("Inputs");
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            document.Root!.Elements(wpf + "Style").Single(e => (string?)e.Attribute(x + "Key") == "FormLabel").Remove();
            var assembly = typeof(SearchableComboBoxBehavior).Assembly.GetName().Name;
            var markup = document.ToString();
            foreach (var attribute in document.Root.Attributes().Where(a => a.IsNamespaceDeclaration &&
                         a.Value.StartsWith("clr-namespace:")).ToArray())
                markup = markup.Replace(attribute.Value, attribute.Value + ";assembly=" + assembly);
            var resources = (ResourceDictionary)XamlReader.Parse(markup);
            Combo = new ComboBox { ItemsSource = items, Resources = resources, Style = (Style)resources[typeof(ComboBox)], Width = 250 };
            Window = new Window {
                Content = new StackPanel { Children = { Before, Combo, After } },
                Width = 300,
                Height = 180,
                Left = -10000,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            Window.Show();
            Window.UpdateLayout();
        }

        public void Dispose() => Window.Close();
    }

    private static KeyEventArgs Press(UIElement target, Key key) {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key) {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(args);
        if (!args.Handled) {
            args.RoutedEvent = Keyboard.KeyDownEvent;
            target.RaiseEvent(args);
        }
        return args;
    }

    public sealed class SelectionModel {
        public string Selection { get; set; } = "";
    }

    private sealed record Choice(string Label);

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