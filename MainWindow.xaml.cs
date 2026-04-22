using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace Sutra
{
    public partial class MainWindow : Window
    {
        private const string HomeUrl = "https://www.google.com";

        public MainWindow()
        {
            InitializeComponent();
            AddNewTab(HomeUrl);

            // Update URL bar when clicking different tabs
            BrowserTabs.SelectionChanged += (s, e) =>
            {
                var currentWebView = GetCurrentWebView();
                // Проверяем: существует ли WebView и задан ли у него Source
                if (currentWebView != null && currentWebView.Source != null)
                {
                    UrlTextBox.Text = currentWebView.Source.ToString();
                }
                else
                {
                    // Если вкладка новая и пустая, можно очистить строку или поставить заглушку
                    UrlTextBox.Text = "about:blank";
                }
            };
        }

        private void AddNewTab(string url)
        {
            var webView = new WebView2();

            // 1. Create the Header layout (Title + Close Button)
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };

            var headerText = new TextBlock
            {
                Text = "Loading...",
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 150,               // Limits the physical width of the text
                TextTrimming = TextTrimming.CharacterEllipsis // Adds "..." automatically
            };

            var closeButton = new Button
            {
                Style = (Style)this.Resources["TabCloseButtonStyle"],
                Content = "✕",
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(2),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            headerStack.Children.Add(headerText);
            headerStack.Children.Add(closeButton);

            // 2. Create the Tab
            var newTab = new TabItem
            {
                Header = headerStack, // Set the layout as the header
                Content = webView
            };

            // 3. Close Button Click Event
            closeButton.Click += (s, e) =>
            {
                BrowserTabs.Items.Remove(newTab);
                // Clean up WebView resources if necessary
                webView.Dispose();

                // Prevent clicking the button from selecting the tab just before it closes
                e.Handled = true;
            };

            BrowserTabs.Items.Add(newTab);
            BrowserTabs.SelectedItem = newTab;

            InitializeWebView(webView, url, newTab);
        }

        private async void InitializeWebView(WebView2 webView, string url, TabItem tab)
        {
            await webView.EnsureCoreWebView2Async(null);
            webView.Source = new Uri(url);

            webView.NavigationCompleted += (s, e) =>
            {
                if (tab.Header is StackPanel stack && stack.Children[0] is TextBlock textBlock)
                {
                    string fullTitle = webView.CoreWebView2.DocumentTitle;

                    // Apply the 32-character limit logic
                    if (!string.IsNullOrEmpty(fullTitle) && fullTitle.Length > 32)
                    {
                        textBlock.Text = fullTitle.Substring(0, 29) + "...";
                    }
                    else
                    {
                        textBlock.Text = fullTitle;
                    }
                }

                if (BrowserTabs.SelectedItem == tab)
                {
                    UrlTextBox.Text = webView.Source.ToString();
                }
            };
        }

        // Helper to get WebView from current tab
        private WebView2 GetCurrentWebView()
        {
            if (BrowserTabs.SelectedItem is TabItem item && item.Content is WebView2 webView)
                return webView;
            return null;
        }

        // --- Event Handlers ---

        private void Go_Click(object sender, RoutedEventArgs e) => Navigate();

        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Navigate();
        }

        private void Navigate()
        {
            string url = UrlTextBox.Text;
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http")) url = "https://" + url;

            var webView = GetCurrentWebView();
            if (webView != null)
            {
                try { webView.Source = new Uri(url); }
                catch { MessageBox.Show("Invalid URL"); }
            }
            else
            {
                // If no tabs are open, open the URL in a new one
                AddNewTab(url);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.GoBack();
        private void Forward_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.GoForward();
        private void Reload_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.Reload();
        private void Home_Click(object sender, RoutedEventArgs e)
        {
            var webView = GetCurrentWebView();
            if (webView != null) webView.Source = new Uri(HomeUrl);
        }
        private void NewTab_Click(object sender, RoutedEventArgs e) => AddNewTab(HomeUrl);

        private void SidebarBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) AddNewTab(btn.Tag.ToString());
        }
    }
}