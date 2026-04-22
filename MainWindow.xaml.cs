using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Web.WebView2.Wpf;

namespace Sutra
{
    // Data model for RSS storage
    public class RssStoredItem
    {
        public string Title { get; set; }
        public string Link { get; set; }
        public string Description { get; set; }
        public DateTimeOffset PubDate { get; set; }
    }

    public partial class MainWindow : Window
    {
        private DateTime _nextRssUpdate;
        private DispatcherTimer _uiCountdownTimer;
        private const string HomeUrl = "https://www.google.com";
        private DispatcherTimer _rssTimer;
        private readonly string _rssDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "rss");

        public MainWindow()
        {

            InitializeComponent();
            Directory.CreateDirectory(_rssDataPath);

            // Initial Setup
            AddNewTab(HomeUrl);
            InitializeRssTimer();

            // Update URL bar when switching tabs
            BrowserTabs.SelectionChanged += (s, e) =>
            {
                var currentWebView = GetCurrentWebView();
                if (currentWebView != null && currentWebView.Source != null)
                {
                    UrlTextBox.Text = currentWebView.Source.ToString();
                }
                // Use Tag to check if this is the RSS tab
                else if (BrowserTabs.SelectedItem is TabItem ti && Equals(ti.Tag, "RSS_TAB"))
                {
                    UrlTextBox.Text = "sutra://rss-reader";
                }
            };
        }

        #region Browser Logic

        private void AddNewTab(string url)
        {
            var webView = new WebView2();
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };

            var headerText = new TextBlock
            {
                Text = "Loading...",
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var closeButton = new Button
            {
                Style = (Style)this.Resources["TabCloseButtonStyle"],
                Content = "✕",
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(2),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            headerStack.Children.Add(headerText);
            headerStack.Children.Add(closeButton);

            var newTab = new TabItem { Header = headerStack, Content = webView };

            closeButton.Click += (s, e) =>
            {
                BrowserTabs.Items.Remove(newTab);
                webView.Dispose();
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
                    textBlock.Text = (fullTitle?.Length > 32) ? fullTitle.Substring(0, 29) + "..." : fullTitle;
                }

                if (BrowserTabs.SelectedItem == tab)
                    UrlTextBox.Text = webView.Source.ToString();
            };
        }

        private WebView2 GetCurrentWebView()
        {
            if (BrowserTabs.SelectedItem is TabItem item && item.Content is WebView2 webView)
                return webView;
            return null;
        }

        #endregion

        #region RSS Logic

        private async void UpdateRss_Click(object sender, RoutedEventArgs e)
        {
            // 1. Visually indicate start
            RssTimerText.Text = "Updating feeds...";
            ((Button)sender).IsEnabled = false;

            try
            {
                // 2. Perform the update
                await RefreshAllFeedsAsync();

                // 3. Reset the countdown timer target to 30 minutes from now
                _nextRssUpdate = DateTime.Now.AddMinutes(30);

                MessageBox.Show("All feeds have been updated.", "RSS Update", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ((Button)sender).IsEnabled = true;
            }
        }

        private void InitializeRssTimer()
        {
            // 1. Setup the existing 30-minute logic timer
            _rssTimer = new DispatcherTimer();
            _rssTimer.Interval = TimeSpan.FromMinutes(30);
            _rssTimer.Tick += async (s, e) =>
            {
                _nextRssUpdate = DateTime.Now.AddMinutes(30); // Reset target
                await RefreshAllFeedsAsync();
            };

            // Set the initial target time
            _nextRssUpdate = DateTime.Now.AddMinutes(30);
            _rssTimer.Start();

            // 2. Setup a new 1-second timer just for the UI text
            _uiCountdownTimer = new DispatcherTimer();
            _uiCountdownTimer.Interval = TimeSpan.FromSeconds(1);
            _uiCountdownTimer.Tick += (s, e) =>
            {
                TimeSpan remaining = _nextRssUpdate - DateTime.Now;
                if (remaining.TotalSeconds > 0)
                {
                    RssTimerText.Text = $"Next update: {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                }
                else
                {
                    RssTimerText.Text = "Updating now...";
                }
            };
            _uiCountdownTimer.Start();

            // Run initial refresh
            Task.Run(() => RefreshAllFeedsAsync());
        }

        private async void SubscribeRss_Click(object sender, RoutedEventArgs e)
        {
            string url = RssUrlInput.Text;
            if (string.IsNullOrWhiteSpace(url)) return;

            SaveSourceUrl(url);
            await RefreshFeedAsync(url);

            RssUrlInput.Clear();
            MessageBox.Show("Subscribed and updated successfully!");
        }

        private void SaveSourceUrl(string url)
        {
            string path = Path.Combine(_rssDataPath, "sources.json");
            List<string> sources = new List<string>();

            if (File.Exists(path))
                sources = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));

            if (!sources.Contains(url))
            {
                sources.Add(url);
                File.WriteAllText(path, JsonSerializer.Serialize(sources));
            }
        }

        private async Task RefreshAllFeedsAsync()
        {
            string path = Path.Combine(_rssDataPath, "sources.json");
            if (!File.Exists(path)) return;

            var sources = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            foreach (var url in sources)
            {
                await RefreshFeedAsync(url);
            }
        }

        private async Task RefreshFeedAsync(string url)
        {
            try
            {
                using (XmlReader reader = XmlReader.Create(url))
                {
                    SyndicationFeed feed = SyndicationFeed.Load(reader);
                    string safeName = string.Join("_", feed.Title.Text.Split(Path.GetInvalidFileNameChars()));
                    string filePath = Path.Combine(_rssDataPath, $"{safeName}.csv"); // Changed to .csv

                    var csvLines = new List<string>();
                    // Add Header Row
                    csvLines.Add("Title,Link,Description,PubDate");

                    foreach (var item in feed.Items)
                    {
                        string title = EscapeCsv(item.Title.Text);
                        string link = EscapeCsv(item.Links.FirstOrDefault()?.Uri.ToString());
                        string desc = EscapeCsv(item.Summary?.Text ?? "");
                        string date = EscapeCsv(item.PublishDate.ToString("yyyy-MM-dd HH:mm:ss"));

                        csvLines.Add($"{title},{link},{desc},{date}");
                    }

                    await File.WriteAllLinesAsync(filePath, csvLines);
                }
            }
            catch { /* Silent fail */ }
        }

        // Helper to handle commas and quotes in content
        private string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "\"\"";
            // Replace double quotes with two double quotes (CSV standard)
            string escaped = text.Replace("\"", "\"\"");
            // Wrap the whole thing in double quotes
            return $"\"{escaped}\"";
        }

        private void ViewRss_Click(object sender, RoutedEventArgs e)
{
    var rssContainer = new ListBox
    {
        Margin = new Thickness(10),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        BorderThickness = new Thickness(0)
    };

    if (Directory.Exists(_rssDataPath))
    {
        var files = Directory.GetFiles(_rssDataPath, "*.csv");
        int itemIndex = 0;

        foreach (var file in files)
        {
            // Skip sources.csv if it exists
            if (file.EndsWith("sources.csv")) continue;

            var lines = File.ReadAllLines(file).Skip(1); 
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { "\",\"" }, StringSplitOptions.None)
                                .Select(p => p.Trim('\"')).ToArray();

                if (parts.Length < 4) continue;

                var item = new RssStoredItem
                {
                    Title = parts[0],
                    Link = parts[1],
                    Description = parts[2],
                    PubDate = DateTimeOffset.TryParse(parts[3], out var dt) ? dt : DateTimeOffset.Now
                };

                var bgColor = (itemIndex % 2 == 0) ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 245));
                var rowBorder = new Border { Background = bgColor, Padding = new Thickness(5, 4, 5, 4), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 230, 230)) };
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                rowGrid.Children.Add(new TextBlock { Text = item.PubDate.ToString("yyyy-MM-dd HH:mm"), Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });

                string domain = "Link";
                try { domain = new Uri(item.Link).Host.Replace("www.", ""); } catch { }
                var domBlock = new TextBlock { Text = domain, FontStyle = FontStyles.Italic, Foreground = System.Windows.Media.Brushes.DarkSlateGray, Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(domBlock, 1); rowGrid.Children.Add(domBlock);

                var titleBlock = new TextBlock { Text = item.Title, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(5, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(titleBlock, 2); rowGrid.Children.Add(titleBlock);

                var link = new TextBlock { Text = "Read More", Foreground = System.Windows.Media.Brushes.Blue, TextDecorations = TextDecorations.Underline, Cursor = Cursors.Hand, Tag = item.Link, VerticalAlignment = VerticalAlignment.Center };
                link.MouseDown += (s, ev) => AddNewTab(((TextBlock)s).Tag.ToString());
                Grid.SetColumn(link, 3); rowGrid.Children.Add(link);

                rowBorder.Child = rowGrid;
                rssContainer.Items.Add(rowBorder);
                itemIndex++;
            }
        }
    } // End of Directory.Exists check

    // TAB CREATION LOGIC (Now correctly inside the method)
    var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
    headerStack.Children.Add(new TextBlock { Text = "RSS Reader", VerticalAlignment = VerticalAlignment.Center });
    var closeBtn = new Button { Style = (Style)this.Resources["TabCloseButtonStyle"], Content = "✕", Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand };
    headerStack.Children.Add(closeBtn);

    var rssTab = new TabItem { 
        Header = headerStack, 
        Content = rssContainer,
        Tag = "RSS_TAB" // Set Tag for identification
    };
    
    closeBtn.Click += (s, ev) => { BrowserTabs.Items.Remove(rssTab); ev.Handled = true; };

    BrowserTabs.Items.Add(rssTab);
    BrowserTabs.SelectedItem = rssTab;
}

        #endregion

        #region Navigation UI Handlers

        private void Navigate()
        {
            string url = UrlTextBox.Text;
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http")) url = "https://" + url;

            var webView = GetCurrentWebView();
            if (webView != null) webView.Source = new Uri(url);
            else AddNewTab(url);
        }

        private void Go_Click(object sender, RoutedEventArgs e) => Navigate();
        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(); }
        private void Back_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.GoBack();
        private void Forward_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.GoForward();
        private void Reload_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.Reload();
        private void Home_Click(object sender, RoutedEventArgs e) { var wv = GetCurrentWebView(); if (wv != null) wv.Source = new Uri(HomeUrl); }
        private void NewTab_Click(object sender, RoutedEventArgs e) => AddNewTab(HomeUrl);
        private void SidebarBookmark_Click(object sender, RoutedEventArgs e) { if (sender is Button btn) AddNewTab(btn.Tag.ToString()); }

        #endregion
    }
}