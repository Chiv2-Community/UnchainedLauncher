using log4net;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views {
    /// <summary>
    ///     Interaction logic for Home.xaml
    /// </summary>
    public partial class Home : UserControl {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Home));

        public Home() {
            InitializeComponent();
            DataContextChanged += Home_DataContextChanged;
        }

        private void Home_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is HomeVM { WhatsNew: INotifyCollectionChanged oldColl }) {
                oldColl.CollectionChanged -= WhatsNew_CollectionChanged;
            }

            if (e.NewValue is HomeVM vm) {
                if (vm.WhatsNew is INotifyCollectionChanged coll) {
                    coll.CollectionChanged += WhatsNew_CollectionChanged;
                }

                // Try select first if already populated
                TrySelectFirst();
            }
        }

        private void WhatsNew_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            // When items appear for the first time, select the first
            if (WhatsNewList.SelectedIndex < 0 && WhatsNewList.Items.Count > 0) {
                WhatsNewList.SelectedIndex = 0;
            }

            UpdatePreviewHtml();
        }

        private void WhatsNewList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            UpdatePreviewHtml();
        }

        private void TrySelectFirst() {
            if (WhatsNewList.SelectedIndex < 0 && WhatsNewList.Items.Count > 0) {
                WhatsNewList.SelectedIndex = 0;
            }

            UpdatePreviewHtml();
        }

        private void UpdatePreviewHtml() {
            if (WhatsNewList.SelectedItem is HomeVM.WhatsNewItem item) {
                WhatsNewHtml.Html = item.Html;
            }
            else {
                if (WhatsNewList.SelectedItem != null) {
                    logger.Warn(
                        $"Selected item in WhatsNewList is not a HomeVM.WhatsNewItem. Got {WhatsNewList.SelectedItem}. HTML will be empty.");
                }

                WhatsNewHtml.Html = string.Empty;
            }
        }
    }
}