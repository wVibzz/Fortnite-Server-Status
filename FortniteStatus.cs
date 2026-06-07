using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FortniteStatus
{
    static class T
    {
        public static Brush Hex(string h)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
            b.Freeze();
            return b;
        }
        public static readonly Brush BG = Hex("#13141b");
        public static readonly Brush CARD = Hex("#1d1f2a");
        public static readonly Brush CARDHI = Hex("#252836");
        public static readonly Brush FG = Hex("#e9ebf2");
        public static readonly Brush MUTED = Hex("#878da0");
        public static readonly Brush GREEN = Hex("#3ddc84");
        public static readonly Brush RED = Hex("#ff5470");
        public static readonly Brush YELLOW = Hex("#ffd166");
        public static readonly Brush ORANGE = Hex("#ff9f43");
        public static readonly Brush BLUE = Hex("#4dabf7");
        public static readonly Brush GREY = Hex("#6b7180");
        public const string FONT = "Segoe UI";
    }

    class StatusInfo
    {
        public string Text;
        public Brush Color;
        public StatusInfo(string t, Brush c) { Text = t; Color = c; }
    }

    class LsResult
    {
        public bool Launchable;
        public string Status;
        public string Message;
    }

    class ServiceRow
    {
        public TextBlock PillText;
        public Ellipse PillDot;
        public string Status = "";
    }

    class GroupUI
    {
        public string Title;
        public string[] Members;
        public Border Container;
        public StackPanel Body;
        public StackPanel Dots;
        public TextBlock Caret;
        public bool Expanded;
        public string DotKey = "";
    }

    class CheckRow
    {
        public string Label;
        public bool Checked;
        public Border Box;
        public System.Windows.Shapes.Path Mark;
        public Action<bool> OnChange;
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            var app = new Application();
            app.Run(new MainWindow());
        }
    }

    class MainWindow : Window
    {
        const string OAUTH = "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token";
        const string LIGHTSWITCH = "https://lightswitch-public-service-prod.ol.epicgames.com/lightswitch/api/service/Fortnite/status";
        const string COMPONENTS = "https://status.epicgames.com/api/v2/components.json";
        const string EpicClientId = "ec684b8c687f479fadea3cb2ad83f5c6";
        const string EpicClientSecret = "e1f31c211f28413186262d37a13fc84d";

        static readonly string CARET_OPEN = ((char)0x25BE).ToString();
        static readonly string CARET_CLOSED = ((char)0x25B8).ToString();
        static readonly string GEAR_GLYPH = ((char)0xE713).ToString();
        static readonly string CLOSE_GLYPH = ((char)0x2715).ToString();

        static readonly Dictionary<string, StatusInfo> STATUS = new Dictionary<string, StatusInfo>
        {
            { "operational", new StatusInfo("Operational", T.GREEN) },
            { "degraded_performance", new StatusInfo("Degraded", T.YELLOW) },
            { "partial_outage", new StatusInfo("Partial", T.ORANGE) },
            { "major_outage", new StatusInfo("Outage", T.RED) },
            { "under_maintenance", new StatusInfo("Maintenance", T.BLUE) },
        };

        static readonly Tuple<string, string[]>[] GROUPS = new[]
        {
            Tuple.Create("Core", new[] { "Game Services", "Login", "Matchmaking" }),
            Tuple.Create("Social", new[] { "Parties, Friends, and Messaging", "Voice Chat" }),
            Tuple.Create("Other", new[] { "Item Shop", "Fortnite Crew", "Stats and Leaderboards", "Website" }),
        };
        readonly string[] ALL;

        readonly Dictionary<string, ServiceRow> rows = new Dictionary<string, ServiceRow>();
        readonly Dictionary<string, GroupUI> groups = new Dictionary<string, GroupUI>();
        readonly Dictionary<Border, string> rowName = new Dictionary<Border, string>();
        readonly HashSet<string> enabled = new HashSet<string>();

        StackPanel mainPanel, settingsPanel;
        TextBlock titleText, pingText, heroText;
        Ellipse heroDot;
        Border heroBar, gearBtn;
        bool settingsOpen;

        readonly string cfgPath;
        Dictionary<string, object> config;

        string token;
        DateTime tokenExp = DateTime.MinValue;
        readonly CancellationTokenSource cts = new CancellationTokenSource();

        DateTime lastPing = DateTime.Now;
        bool lastOk = true;
        readonly string tzAbbrev;

        public MainWindow()
        {
            ALL = GROUPS.SelectMany(g => g.Item2).ToArray();
            tzAbbrev = Abbrev(TimeZoneInfo.Local);

            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FortniteStatus");
            System.IO.Directory.CreateDirectory(dir);
            cfgPath = System.IO.Path.Combine(dir, "config.json");
            config = LoadConfig();
            var disabled = new HashSet<string>(GetStrings(config, "disabled"));
            foreach (var s in ALL) if (!disabled.Contains(s)) enabled.Add(s);

            BuildWindow();
            RefreshVisibility();
            Topmost = GetBool(config, "always_on_top");

            Closing += (s, e) => cts.Cancel();
            Loaded += (s, e) => Task.Run(() => PollLoop(cts.Token));
        }

        void BuildWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            UseLayoutRounding = true;

            var root = new Border
            {
                Width = 350,
                Margin = new Thickness(12),
                CornerRadius = new CornerRadius(12),
                Background = T.BG,
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
            };
            var rootClip = new Border { CornerRadius = new CornerRadius(12), Background = T.BG, ClipToBounds = true };
            root.Child = rootClip;

            var outer = new StackPanel();
            rootClip.Child = outer;

            outer.Children.Add(BuildTitleBar());

            mainPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };
            settingsPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 12), Visibility = Visibility.Collapsed };
            BuildMain();
            BuildSettings();
            outer.Children.Add(mainPanel);
            outer.Children.Add(settingsPanel);

            Content = root;
        }

        UIElement BuildTitleBar()
        {
            var bar = new Border { Background = T.CARD, CornerRadius = new CornerRadius(12, 12, 0, 0), Height = 32 };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = grid;

            titleText = new TextBlock
            {
                Text = "Fortnite Status",
                Foreground = T.FG,
                FontFamily = new FontFamily(T.FONT),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(titleText, 0);
            grid.Children.Add(titleText);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            pingText = new TextBlock
            {
                Text = "updating...",
                Foreground = T.MUTED,
                FontFamily = new FontFamily(T.FONT),
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            right.Children.Add(pingText);

            gearBtn = IconButton(GEAR_GLYPH, "Segoe MDL2 Assets", 12, () => ToggleSettings());
            gearBtn.MouseEnter += (s, e) => { gearBtn.Background = T.CARDHI; ((TextBlock)gearBtn.Child).Foreground = T.FG; };
            gearBtn.MouseLeave += (s, e) => SetGearIdle();
            right.Children.Add(gearBtn);

            var closeBtn = IconButton(CLOSE_GLYPH, T.FONT, 13, () => Close());
            closeBtn.MouseEnter += (s, e) => { closeBtn.Background = T.RED; ((TextBlock)closeBtn.Child).Foreground = T.BG; };
            closeBtn.MouseLeave += (s, e) => { closeBtn.Background = Brushes.Transparent; ((TextBlock)closeBtn.Child).Foreground = T.MUTED; };
            right.Children.Add(closeBtn);

            bar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            return bar;
        }

        Border IconButton(string glyph, string font, double size, Action onClick)
        {
            var b = new Border { Background = Brushes.Transparent, Width = 34, Height = 32, Cursor = Cursors.Hand };
            var t = new TextBlock
            {
                Text = glyph,
                Foreground = T.MUTED,
                FontFamily = new FontFamily(font),
                FontSize = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            b.Child = t;
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }

        void SetGearIdle()
        {
            var t = (TextBlock)gearBtn.Child;
            if (settingsOpen) { gearBtn.Background = T.CARDHI; t.Foreground = T.FG; }
            else { gearBtn.Background = Brushes.Transparent; t.Foreground = T.MUTED; }
        }

        TextBlock Caption(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = T.MUTED,
                FontFamily = new FontFamily(T.FONT),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 8, 0, 4),
            };
        }

        void BuildMain()
        {
            mainPanel.Children.Add(Caption("EPIC GAMES LAUNCHER"));

            var hero = new Border { Background = T.CARD, CornerRadius = new CornerRadius(12), Height = 56, Margin = new Thickness(0, 0, 0, 8) };
            var hg = new Grid();
            heroBar = new Border { Width = 5, Background = T.GREY, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(12, 0, 0, 12) };
            hg.Children.Add(heroBar);
            var hrow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            var ring = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 12, 0) };
            ring.Children.Add(new Ellipse { Fill = T.CARDHI });
            heroDot = new Ellipse { Width = 10, Height = 10, Fill = T.GREY, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ring.Children.Add(heroDot);
            hrow.Children.Add(ring);
            heroText = new TextBlock { Text = "Checking...", Foreground = T.GREY, FontFamily = new FontFamily(T.FONT), FontSize = 19, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            hrow.Children.Add(heroText);
            hg.Children.Add(hrow);
            hero.Child = hg;
            mainPanel.Children.Add(hero);

            mainPanel.Children.Add(new Border { Height = 1, Background = T.CARDHI, Margin = new Thickness(2, 2, 2, 0) });
            mainPanel.Children.Add(Caption("WEBSITE STATUS"));

            foreach (var g in GROUPS)
            {
                var gui = new GroupUI { Title = g.Item1, Members = g.Item2, Expanded = false };
                var container = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                container.Children.Add(GroupHeader(gui));
                gui.Body = new StackPanel { Margin = new Thickness(8, 2, 0, 0), Visibility = Visibility.Collapsed };
                foreach (var m in g.Item2) gui.Body.Children.Add(ServiceRowControl(m));
                container.Children.Add(gui.Body);
                gui.Container = new Border { Child = container };
                mainPanel.Children.Add(gui.Container);
                groups[g.Item1] = gui;
            }
        }

        Border GroupHeader(GroupUI gui)
        {
            var header = new Border { Background = Brushes.Transparent, Height = 26, Cursor = Cursors.Hand };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock { Text = "-  " + gui.Title.ToUpper(), Foreground = T.FG, FontFamily = new FontFamily(T.FONT), FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(title, 0);
            grid.Children.Add(title);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            gui.Dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            right.Children.Add(gui.Dots);
            gui.Caret = new TextBlock { Text = CARET_CLOSED, Foreground = T.MUTED, FontFamily = new FontFamily(T.FONT), FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(gui.Caret);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            header.Child = grid;
            header.MouseLeftButtonUp += (s, e) =>
            {
                gui.Expanded = !gui.Expanded;
                gui.Body.Visibility = gui.Expanded ? Visibility.Visible : Visibility.Collapsed;
                gui.Caret.Text = gui.Expanded ? CARET_OPEN : CARET_CLOSED;
            };
            return header;
        }

        Border ServiceRowControl(string name)
        {
            var sr = new ServiceRow();
            var card = new Border { Background = T.CARD, CornerRadius = new CornerRadius(8), Height = 32, Margin = new Thickness(0, 0, 0, 4) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = "-  " + name, Foreground = T.FG, FontFamily = new FontFamily(T.FONT), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var pill = new Border { Background = T.CARDHI, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 10, 2), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var prow = new StackPanel { Orientation = Orientation.Horizontal };
            sr.PillDot = new Ellipse { Width = 6, Height = 6, Fill = T.GREY, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            prow.Children.Add(sr.PillDot);
            sr.PillText = new TextBlock { Text = "", Foreground = T.GREY, FontFamily = new FontFamily(T.FONT), FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            prow.Children.Add(sr.PillText);
            pill.Child = prow;
            Grid.SetColumn(pill, 1);
            grid.Children.Add(pill);

            card.Child = grid;
            rows[name] = sr;
            rowName[card] = name;
            return card;
        }

        void BuildSettings()
        {
            settingsPanel.Children.Add(Caption("WINDOW"));
            settingsPanel.Children.Add(CheckRowControl("Always on top", GetBool(config, "always_on_top"), v =>
            {
                config["always_on_top"] = v;
                Topmost = v;
                SaveConfig();
            }));
            settingsPanel.Children.Add(CheckRowControl("24-hour clock", GetBool(config, "clock_24h"), v =>
            {
                config["clock_24h"] = v;
                SaveConfig();
                FormatPing();
            }));

            settingsPanel.Children.Add(Caption("TRACKED SERVICES"));
            foreach (var g in GROUPS)
            {
                var gui = new GroupUI { Title = g.Item1, Members = g.Item2, Expanded = false };
                var container = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                container.Children.Add(GroupHeader(gui));
                gui.Body = new StackPanel { Margin = new Thickness(8, 2, 0, 0), Visibility = Visibility.Collapsed };
                foreach (var m in g.Item2)
                {
                    string name = m;
                    gui.Body.Children.Add(CheckRowControl(name, enabled.Contains(name), v =>
                    {
                        if (v) enabled.Add(name); else enabled.Remove(name);
                        config["disabled"] = ALL.Where(x => !enabled.Contains(x)).ToArray();
                        SaveConfig();
                        RefreshVisibility();
                    }));
                }
                container.Children.Add(gui.Body);
                settingsPanel.Children.Add(container);
            }
        }

        Border CheckRowControl(string label, bool isChecked, Action<bool> onChange)
        {
            var cr = new CheckRow { Label = label, Checked = isChecked, OnChange = onChange };
            var card = new Border { Background = T.CARD, CornerRadius = new CornerRadius(8), Height = 32, Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

            cr.Box = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = isChecked ? T.GREEN : T.CARDHI, VerticalAlignment = VerticalAlignment.Center };
            cr.Mark = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 3,7 L 6,10 L 11,4"), Stroke = T.BG, StrokeThickness = 2, Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed };
            cr.Box.Child = cr.Mark;
            row.Children.Add(cr.Box);

            var text = new TextBlock { Text = label, Foreground = T.FG, FontFamily = new FontFamily(T.FONT), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            row.Children.Add(text);

            card.Child = row;
            card.MouseLeftButtonUp += (s, e) =>
            {
                cr.Checked = !cr.Checked;
                cr.Box.Background = cr.Checked ? T.GREEN : T.CARDHI;
                cr.Mark.Visibility = cr.Checked ? Visibility.Visible : Visibility.Collapsed;
                cr.OnChange(cr.Checked);
            };
            return card;
        }

        void ToggleSettings()
        {
            settingsOpen = !settingsOpen;
            mainPanel.Visibility = settingsOpen ? Visibility.Collapsed : Visibility.Visible;
            settingsPanel.Visibility = settingsOpen ? Visibility.Visible : Visibility.Collapsed;
            titleText.Text = settingsOpen ? "Settings" : "Fortnite Status";
            SetGearIdle();
        }

        void RefreshVisibility()
        {
            foreach (var g in GROUPS)
            {
                var gui = groups[g.Item1];
                bool any = false;
                foreach (UIElement child in gui.Body.Children)
                {
                    var card = child as Border;
                    if (card == null) continue;
                    string nm;
                    if (!rowName.TryGetValue(card, out nm)) continue;
                    bool vis = enabled.Contains(nm);
                    card.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                    if (vis) any = true;
                }
                gui.Container.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        async Task PollLoop(CancellationToken ct)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            while (!ct.IsCancellationRequested)
            {
                LsResult ls = null;
                try { ls = await GetLightswitch(http); } catch { ls = null; }
                Dictionary<string, string> comp = null;
                try { comp = await GetComponents(http); } catch { comp = null; }
                bool ok = ls != null || comp != null;
                DateTime now = DateTime.Now;
                try { Dispatcher.Invoke(() => UpdateUI(ls, comp, ok, now)); } catch { }
                try { await Task.Delay(1000, ct); } catch { break; }
            }
        }

        async Task<string> GetToken(HttpClient http)
        {
            if (token != null && DateTime.Now < tokenExp) return token;
            var req = new HttpRequestMessage(HttpMethod.Post, OAUTH);
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(EpicClientId + ":" + EpicClientSecret));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });
            var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var d = (Dictionary<string, object>)Json().DeserializeObject(await resp.Content.ReadAsStringAsync());
            token = (string)d["access_token"];
            int secs = d.ContainsKey("expires_in") ? Convert.ToInt32(d["expires_in"]) : 3600;
            tokenExp = DateTime.Now.AddSeconds(secs - 60);
            return token;
        }

        async Task<LsResult> GetLightswitch(HttpClient http)
        {
            var t = await GetToken(http);
            var req = new HttpRequestMessage(HttpMethod.Get, LIGHTSWITCH);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
            var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var d = (Dictionary<string, object>)Json().DeserializeObject(await resp.Content.ReadAsStringAsync());
            string status = d.ContainsKey("status") ? (string)d["status"] : "";
            string msg = d.ContainsKey("message") ? d["message"] as string : "";
            return new LsResult { Launchable = status == "UP", Status = status, Message = msg };
        }

        async Task<Dictionary<string, string>> GetComponents(HttpClient http)
        {
            var json = await http.GetStringAsync(COMPONENTS);
            var root = (Dictionary<string, object>)Json().DeserializeObject(json);
            var comps = (object[])root["components"];
            string groupId = null;
            foreach (var o in comps)
            {
                var c = (Dictionary<string, object>)o;
                if ((string)c["name"] == "Fortnite" && c.ContainsKey("group") && c["group"] is bool && (bool)c["group"])
                {
                    groupId = (string)c["id"];
                    break;
                }
            }
            var map = new Dictionary<string, string>();
            if (groupId != null)
            {
                foreach (var o in comps)
                {
                    var c = (Dictionary<string, object>)o;
                    if (c.ContainsKey("group_id") && (c["group_id"] as string) == groupId)
                        map[(string)c["name"]] = (string)c["status"];
                }
            }
            return map;
        }

        JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        void UpdateUI(LsResult ls, Dictionary<string, string> comp, bool ok, DateTime stamp)
        {
            if (ls != null)
            {
                if (ls.Launchable) SetHero(T.GREEN, "Server Online");
                else SetHero(T.RED, "Server Offline");
            }
            else SetHero(T.GREY, "Unknown");

            if (comp != null)
            {
                foreach (var g in GROUPS)
                {
                    var gui = groups[g.Item1];
                    var keyParts = new List<string>();
                    foreach (var m in g.Item2)
                    {
                        string st = comp.ContainsKey(m) ? comp[m] : "";
                        var info = STATUS.ContainsKey(st) ? STATUS[st] : new StatusInfo("Unknown", T.GREY);
                        var sr = rows[m];
                        if (sr.Status != st)
                        {
                            sr.Status = st;
                            sr.PillText.Text = info.Text;
                            sr.PillText.Foreground = info.Color;
                            sr.PillDot.Fill = info.Color;
                        }
                        if (enabled.Contains(m)) keyParts.Add(info.Color.ToString());
                    }
                    string key = string.Join(",", keyParts);
                    if (gui.DotKey != key)
                    {
                        gui.DotKey = key;
                        gui.Dots.Children.Clear();
                        foreach (var m in g.Item2)
                        {
                            if (!enabled.Contains(m)) continue;
                            string st = comp.ContainsKey(m) ? comp[m] : "";
                            var info = STATUS.ContainsKey(st) ? STATUS[st] : new StatusInfo("Unknown", T.GREY);
                            gui.Dots.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = info.Color, Margin = new Thickness(0, 0, 5, 0) });
                        }
                    }
                }
            }

            lastOk = ok;
            lastPing = stamp;
            FormatPing();
        }

        void FormatPing()
        {
            string prefix = lastOk ? "last pinged " : "ping failed ";
            string fmt = GetBool(config, "clock_24h") ? "HH:mm:ss" : "hh:mm:ss tt";
            pingText.Text = prefix + lastPing.ToString(fmt, CultureInfo.InvariantCulture) + " " + tzAbbrev;
        }

        static string Abbrev(TimeZoneInfo tz)
        {
            string name = tz.IsDaylightSavingTime(DateTime.Now) ? tz.DaylightName : tz.StandardName;
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var sb = new StringBuilder();
                foreach (var p in parts) if (char.IsLetter(p[0])) sb.Append(char.ToUpper(p[0]));
                if (sb.Length >= 2) return sb.ToString();
            }
            return name;
        }

        void SetHero(Brush color, string text)
        {
            if (heroText.Text == text) return;
            heroText.Text = text;
            heroText.Foreground = color;
            heroDot.Fill = color;
            heroBar.Background = color;
        }

        Dictionary<string, object> LoadConfig()
        {
            try
            {
                if (System.IO.File.Exists(cfgPath))
                {
                    var d = (Dictionary<string, object>)Json().DeserializeObject(System.IO.File.ReadAllText(cfgPath));
                    return d ?? new Dictionary<string, object>();
                }
            }
            catch { }
            return new Dictionary<string, object>();
        }

        void SaveConfig()
        {
            try { System.IO.File.WriteAllText(cfgPath, Json().Serialize(config)); } catch { }
        }

        static bool GetBool(Dictionary<string, object> d, string k)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v is bool) return (bool)v;
            return false;
        }

        static IEnumerable<string> GetStrings(Dictionary<string, object> d, string k)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v is object[])
                foreach (var o in (object[])v) if (o is string) yield return (string)o;
        }
    }
}
