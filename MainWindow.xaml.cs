using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml;
// Explicitly resolve the ambiguous File reference
using File = System.IO.File;
using Path=System.IO.Path;

namespace Sutra
{
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
        private DispatcherTimer _rssTimer;
        private const string HomeUrl = "https://www.google.com";
        private readonly string _rssDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "rss");

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(_rssDataPath);

            AddNewTab(HomeUrl);
            InitializeRssTimer();

            BrowserTabs.SelectionChanged += (s, e) =>
            {
                var currentWebView = GetCurrentWebView();
                if (currentWebView?.Source != null)
                    UrlTextBox.Text = currentWebView.Source.ToString();
                else if (BrowserTabs.SelectedItem is TabItem ti && Equals(ti.Tag, "RSS_TAB"))
                    UrlTextBox.Text = "sutra://rss-reader";
            };
        }

        #region Browser Logic

        private void AddNewTab(string url, string autoPasteText = null)
        {
            var webView = new WebView2();
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
            var headerText = new TextBlock { Text = "Loading...", VerticalAlignment = VerticalAlignment.Center, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis };
            var closeButton = new Button { Style = (Style)Resources["TabCloseButtonStyle"], Content = "✕", Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand };

            headerStack.Children.Add(headerText);
            headerStack.Children.Add(closeButton);

            var newTab = new TabItem { Header = headerStack, Content = webView };
            closeButton.Click += (s, e) => { BrowserTabs.Items.Remove(newTab); webView.Dispose(); e.Handled = true; };

            BrowserTabs.Items.Add(newTab);
            BrowserTabs.SelectedItem = newTab;

            InitializeWebView(webView, url, newTab, autoPasteText);
        }

        private async void InitializeWebView(WebView2 webView, string url, TabItem tab, string autoPasteText = null)
        {
            await webView.EnsureCoreWebView2Async(null);
            webView.Source = new Uri(url);

            webView.NavigationCompleted += async (s, e) =>
            {
                if (tab.Header is StackPanel stack && stack.Children[0] is TextBlock textBlock)
                {
                    string fullTitle = webView.CoreWebView2.DocumentTitle;
                    textBlock.Text = (fullTitle?.Length > 32) ? fullTitle.Substring(0, 29) + "..." : fullTitle;
                }

                if (BrowserTabs.SelectedItem == tab)
                    UrlTextBox.Text = webView.Source.ToString();

                // Automate Input Injection
                if (e.IsSuccess && !string.IsNullOrEmpty(autoPasteText))
                {
                    await Task.Delay(1500); // Give AI scripts time to initialize
                    string safeText = autoPasteText.Replace("'", "\\'").Replace("\n", " ").Replace("\r", "");
                    string script = "";

                    if (url.Contains("gemini.google.com"))
                        script = $"var el = document.querySelector('div[contenteditable=\"true\"]'); if(el) {{ el.innerText = '{safeText}'; el.dispatchEvent(new Event('input', {{ bubbles: true }})); }}";
                    else if (url.Contains("alice.yandex.ru"))
                        script = $"var el = document.querySelector('textarea'); if(el) {{ el.value = '{safeText}'; el.dispatchEvent(new Event('input', {{ bubbles: true }})); }}";
                    else if (url.Contains("google.com"))
                        script = $"var el = document.querySelector('input[name=\"q\"], textarea[name=\"q\"]'); if(el) {{ el.value = '{safeText}'; }}";

                    if (!string.IsNullOrEmpty(script))
                        await webView.ExecuteScriptAsync(script);
                }
            };
        }

        private WebView2 GetCurrentWebView() => (BrowserTabs.SelectedItem as TabItem)?.Content as WebView2;

        #endregion

        #region RSS Logic

        private void ViewRss_Click(object sender, RoutedEventArgs e)
        {
            var rssContainer = new ListBox { Margin = new Thickness(10), BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };

            if (Directory.Exists(_rssDataPath))
            {
                var files = Directory.GetFiles(_rssDataPath, "*.csv").Where(f => !f.Contains("sources"));
                int idx = 0;
                foreach (var file in files)
                {
                    try
                    {
                        // Используем UTF8 явно на случай кириллицы
                        var lines = File.ReadAllLines(file, System.Text.Encoding.UTF8).Skip(1);

                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            // Более надежное разделение: разбиваем по "," и убираем лишние кавычки по краям
                            var parts = line.Split(new[] { "\",\"" }, StringSplitOptions.None)
                                            .Select(p => p.Trim('\"'))
                                            .ToArray();

                            if (parts.Length < 4) continue;

                            var item = new RssStoredItem { Title = parts[0], Link = parts[1], Description = parts[2], PubDate = DateTimeOffset.TryParse(parts[3], out var dt) ? dt : DateTimeOffset.Now };

                            var rowGrid = new Grid();
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                            rowGrid.Children.Add(new TextBlock { Text = item.PubDate.ToString("yyyy-MM-dd HH:mm"), Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });

                            string domain = Uri.TryCreate(item.Link, UriKind.Absolute, out var uri) ? uri.Host.ToLower().Replace("www.", "") : "source";
                            var domBlock = new TextBlock { Text = domain, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(2, 136, 209)), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0) };
                            domBlock.MouseDown += (s, ev) => AddNewTab(item.Link);
                            Grid.SetColumn(domBlock, 1);
                            rowGrid.Children.Add(domBlock);

                            var titleBox = new TextBlock
                            {
                                Text = item.Title,
                                FontWeight = FontWeights.Medium,
                                Background = Brushes.Transparent,
                                VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis // This now works!
                            };

                            var menu = new ContextMenu();
                            var mGemini = new MenuItem { Header = "Send to Gemini" };
                            mGemini.Click += (s, ev) => AddNewTab("https://gemini.google.com", item.Title);
                            var mAlice = new MenuItem { Header = "Send to Alice" };
                            mAlice.Click += (s, ev) => AddNewTab("https://alice.yandex.ru", item.Title);
                            var mGoogle = new MenuItem { Header = "Search Google" };
                            mGoogle.Click += (s, ev) => AddNewTab("https://www.google.com", item.Title);

                            menu.Items.Add(mGemini); menu.Items.Add(mAlice); menu.Items.Add(new Separator()); menu.Items.Add(mGoogle);
                            titleBox.ContextMenu = menu;

                            Grid.SetColumn(titleBox, 2);
                            rowGrid.Children.Add(titleBox);

                            var border = new Border { Background = (idx++ % 2 == 0) ? Brushes.White : new SolidColorBrush(Color.FromRgb(245, 245, 245)), Padding = new Thickness(5), Child = rowGrid, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)) };
                            rssContainer.Items.Add(border);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Если один файл поврежден, просто пропускаем его
                        System.Diagnostics.Debug.WriteLine($"Ошибка в файле {file}: {ex.Message}");
                    }
                }


                var rssTab = new TabItem { Header = "RSS Reader", Content = rssContainer, Tag = "RSS_TAB" };
                BrowserTabs.Items.Add(rssTab);
                BrowserTabs.SelectedItem = rssTab;
            }
        }

        private void InitializeRssTimer()
        {
            _rssTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _rssTimer.Tick += async (s, e) =>
            {
                _nextRssUpdate = DateTime.Now.AddMinutes(30);
                await RefreshAllFeedsAsync();
            };
            _nextRssUpdate = DateTime.Now.AddMinutes(30);
            _rssTimer.Start();

            _uiCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiCountdownTimer.Tick += (s, e) =>
            {
                TimeSpan remaining = _nextRssUpdate - DateTime.Now;
                RssTimerText.Text = remaining.TotalSeconds > 0
                    ? $"RSS Next Update: {remaining.Minutes:D2}:{remaining.Seconds:D2}"
                    : "Updating now...";
            };
            _uiCountdownTimer.Start();

            // Run initial update
            Task.Run(() => RefreshAllFeedsAsync());
        }

        private async Task RefreshFeedAsync(string url)
        {
            try
            {
                // 1. Load the feed into memory first
                using var reader = XmlReader.Create(url);
                var feed = SyndicationFeed.Load(reader);

                // 2. Determine the domain from the site's link or the first item's link
                // We look for the "alternate" link (the website) or just the first available link
                string domain = "unknown_source";
                var siteLink = feed.Links.FirstOrDefault(l => l.RelationshipType == "alternate")?.Uri.ToString()
                               ?? feed.Items.FirstOrDefault()?.Links.FirstOrDefault()?.Uri.ToString();

                if (!string.IsNullOrEmpty(siteLink) && Uri.TryCreate(siteLink, UriKind.Absolute, out var uri))
                {
                    domain = uri.Host.ToLower().Replace("www.", "");
                }
                else
                {
                    // Fallback: If no links in feed, use a safe version of the feed title
                    domain = string.Join("_", feed.Title.Text.Split(Path.GetInvalidFileNameChars()));
                }

                string filePath = Path.Combine(_rssDataPath, $"{domain}.csv");

                // 3. Prepare the CSV data
                var lines = new List<string> { "Title,Link,Description,PubDate" };
                foreach (var item in feed.Items)
                {
                    var itemLink = item.Links.FirstOrDefault(l => l.RelationshipType == "alternate")?.Uri.ToString()
                                   ?? item.Links.FirstOrDefault()?.Uri.ToString();

                    lines.Add($"{EscapeCsv(item.Title.Text)},{EscapeCsv(itemLink)},{EscapeCsv(item.Summary?.Text ?? "")},{EscapeCsv(item.PublishDate.ToString("yyyy-MM-dd HH:mm:ss"))}");
                }

                // 4. Save the file (Using WriteAllLinesAsync for thread safety)
                await File.WriteAllLinesAsync(filePath, lines);
            }
            catch (Exception ex)
            {
                // Silently fail or log for debugging:
                // System.Diagnostics.Debug.WriteLine($"Feed error: {url} - {ex.Message}");
            }
        }

        private async Task RefreshAllFeedsAsync()
        {
            string path = Path.Combine(_rssDataPath, "sources.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var sources = JsonSerializer.Deserialize<List<string>>(json);
                if (sources != null)
                {
                    foreach (var url in sources)
                    {
                        await RefreshFeedAsync(url);
                    }
                }
            }
            catch
            {
                // Handle JSON or File IO errors
            }
        }

        private string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "\"\"";
            string cleanText = text.Replace("\r", "").Replace("\n", " ").Replace("\"", "\"\"");
            return $"\"{cleanText}\"";
        }

        #endregion

        #region Navigation UI Handlers

        private void Navigate()
        {
            string url = UrlTextBox.Text;
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http")) url = "https://" + url;
            var wv = GetCurrentWebView();
            if (wv != null) wv.Source = new Uri(url); else AddNewTab(url);
        }

        private void Go_Click(object sender, RoutedEventArgs e) => Navigate();
        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(); }
        private void Back_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.GoBack();
        private void Forward_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.GoForward();
        private void Reload_Click(object sender, RoutedEventArgs e) => GetCurrentWebView()?.Reload();
        private void Home_Click(object sender, RoutedEventArgs e) { var wv = GetCurrentWebView(); if (wv != null) wv.Source = new Uri(HomeUrl); }
        private void NewTab_Click(object sender, RoutedEventArgs e) => AddNewTab(HomeUrl);
        private void SidebarBookmark_Click(object sender, RoutedEventArgs e) { if (sender is Button btn) AddNewTab(btn.Tag.ToString()); }
        private async void SubscribeRss_Click(object sender, RoutedEventArgs e) { if (string.IsNullOrWhiteSpace(RssUrlInput.Text)) return; SaveSourceUrl(RssUrlInput.Text); await RefreshFeedAsync(RssUrlInput.Text); RssUrlInput.Clear(); MessageBox.Show("Subscribed!"); }
        private async void UpdateRss_Click(object sender, RoutedEventArgs e) { await RefreshAllFeedsAsync(); _nextRssUpdate = DateTime.Now.AddMinutes(30); MessageBox.Show("Feeds updated."); }

        private void SaveSourceUrl(string url)
        {
            string path = Path.Combine(_rssDataPath, "sources.json");
            var sources = File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) : new List<string>();
            if (!sources.Contains(url)) { sources.Add(url); File.WriteAllText(path, JsonSerializer.Serialize(sources)); }
        }

        #endregion
    }
}