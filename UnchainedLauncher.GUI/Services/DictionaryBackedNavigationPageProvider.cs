using LanguageExt;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace UnchainedLauncher.GUI.Services {

    // TODO: Make DictionaryBackedNavigationPageProvider in to a thing that also provides title and symbol for the nav
    // elements
    record NavigationPageEntry<T>(string Title, SymbolRegular Icon, T Page, object DataContext) where T : FrameworkElement;

    public interface IUsefulNavigationPageViewProvider : INavigationViewPageProvider {

    }

    public class DictionaryBackedNavigationPageProvider : INavigationViewPageProvider {
        private IDictionary<Type, FrameworkElement> PageDictionary { get; }

        private DictionaryBackedNavigationPageProvider(IDictionary<Type, FrameworkElement> pageDictionary) {
            PageDictionary = pageDictionary;
        }

        public object? GetPage(Type pageType) {
            return PageDictionary.TryGetValue(pageType).FirstOrDefault();
        }

        public static DictionaryBackedNavigationPageProviderBuilder Builder() {
            return new DictionaryBackedNavigationPageProviderBuilder(
                new Dictionary<Type, (FrameworkElement, object)>()
            );
        }

        public class DictionaryBackedNavigationPageProviderBuilder {
            private IDictionary<Type, (FrameworkElement Page, object DataContext)> PageDictionary { get; }

            public DictionaryBackedNavigationPageProviderBuilder(IDictionary<Type, (FrameworkElement Page, object DataContext)> pageDictionary) {
                PageDictionary = pageDictionary;
            }

            public DictionaryBackedNavigationPageProviderBuilder AddPage<T>(T page, object dataContext) where T : FrameworkElement {
                PageDictionary.Add(typeof(T), (page, dataContext));
                return this;
            }

            public DictionaryBackedNavigationPageProvider Build() {
                var dict = new Dictionary<Type, FrameworkElement>();

                foreach (var (k, (page, dataContext)) in PageDictionary) {
                    page.DataContext = dataContext;
                    dict.Add(k, page);
                }

                return new DictionaryBackedNavigationPageProvider(dict);
            }
        }

    }
}