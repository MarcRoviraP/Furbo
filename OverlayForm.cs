using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Playwright;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace FlashscoreOverlay
{
    // ═══════════════════════════════════════════════════════════════════
    //  Data models
    // ═══════════════════════════════════════════════════════════════════
    public class MatchData
    {
        public string MatchId { get; set; } = "";
        public string HomeTeam { get; set; } = "Home";
        public string AwayTeam { get; set; } = "Away";
        public string HomeScore { get; set; } = "-";
        public string AwayScore { get; set; } = "-";
        public string MatchTime { get; set; } = "";
        public string League { get; set; } = "";
        public string LeagueCountry { get; set; } = "";
        public string HomeImg { get; set; } = "";
        public string AwayImg { get; set; } = "";
        public string LeagueUrl { get; set; } = "";

        // Alert state
        public string PrevHomeScore { get; set; } = "-";
        public string PrevAwayScore { get; set; } = "-";
        public long AlertExpiresMs { get; set; } = 0;
        public string StageCategory { get; set; } = "";
        public long StageAlertExpiresMs { get; set; } = 0;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  WebSocket behavior
    // ═══════════════════════════════════════════════════════════════════
    public class FlashscoreBehavior : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            OverlayForm.Instance?.HandleWebSocketMessage(e.Data);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Main Overlay Form
    // ═══════════════════════════════════════════════════════════════════
    public class OverlayForm : Form
    {
        public static OverlayForm? Instance { get; private set; }

        // ── Win32 interop ──
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int SC_MOVE = 0xF010;
        private const int MA_NOACTIVATE = 3;
        private const int HTCAPTION = 2;

        // ── Colors (matching original CSS) ──
        private static readonly Color BgHeader = ColorTranslator.FromHtml("#001e28");
        private static readonly Color BgMatch = ColorTranslator.FromHtml("#00141e");
        private static readonly Color BgMatchHover = ColorTranslator.FromHtml("#0b1e28");
        private static readonly Color BgAlert = ColorTranslator.FromHtml("#3D0314");
        private static readonly Color BgStageFlair = ColorTranslator.FromHtml("#3D0314");
        private static readonly Color TextHeader = ColorTranslator.FromHtml("#accbd9");
        private static readonly Color TextWhite = Color.White;
        private static readonly Color TextLive = ColorTranslator.FromHtml("#FF0046");
        private static readonly Color TextScore = ColorTranslator.FromHtml("#ff0046");
        private static readonly Color TextMuted = ColorTranslator.FromHtml("#667788");
        private static readonly Color TextAlertName = ColorTranslator.FromHtml("#C80037");
        private static readonly Color BorderColor = ColorTranslator.FromHtml("#0b1e28");

        // ── Layout constants ──
        private const int FORM_WIDTH = 560;
        private const int HEADER_HEIGHT = 30;
        private const int MATCH_ROW_HEIGHT = 56;
        private const int MIN_HEIGHT = 80;
        private const int STAR_COL_W = 30;
        private const int TIME_COL_W = 45;
        private const int SCORE_COL_W = 35;
        private const int LOGO_SIZE = 14;
        private const int PADDING_H = 8;

        // ── State ──
        private WebSocketServer? _wssv;
        private System.Threading.Timer? _scrapeTimer;
        private System.Windows.Forms.Timer? _blinkTimer;
        private bool _blinkOn = true;
        private int _hoverMatchIndex = -1;

        private ConcurrentDictionary<string, string> _trackedIds = new();
        private ConcurrentQueue<string> _idsToRemove = new();
        private readonly List<MatchData> _matches = new();
        private readonly object _matchLock = new();
        private HashSet<string> _removedIds = new();

        // ── Playwright ──
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private bool _isScraping = false;

        // ── Image cache ──
        private static readonly HttpClient _httpClient = new();
        private readonly ConcurrentDictionary<string, Image?> _imageCache = new();
        private readonly ConcurrentDictionary<string, bool> _imageLoading = new();

        // ── Fonts ──
        private Font _fontHeader = new("Segoe UI", 9f, FontStyle.Bold);
        private Font _fontTeam = new("Segoe UI", 10f, FontStyle.Regular);
        private Font _fontScore = new("Segoe UI", 10f, FontStyle.Bold);
        private Font _fontTime = new("Segoe UI", 9f, FontStyle.Bold);
        private Font _fontTimeLive = new("Segoe UI", 9f, FontStyle.Bold);
        private Font _fontStar = new("Segoe UI", 12f, FontStyle.Regular);
        private Font _fontMuted = new("Segoe UI", 9f, FontStyle.Regular);

        // ═══════════════════════════════════════════════════════════════
        //  Constructor
        // ═══════════════════════════════════════════════════════════════
        public OverlayForm()
        {
            Instance = this;

            // Form setup
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen!.WorkingArea.Width - FORM_WIDTH - 20, 100);
            this.Size = new Size(FORM_WIDTH, MIN_HEIGHT);
            this.BackColor = BgMatch;
            this.DoubleBuffered = true;

            // Make form click-through-able for dragging
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            Console.WriteLine("[OVERLAY] Form initialized: {0}x{1} at ({2},{3})", Width, Height, Location.X, Location.Y);

            // Blink timer for live minute apostrophe
            _blinkTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _blinkTimer.Tick += (s, e) => { _blinkOn = !_blinkOn; Invalidate(); };
            _blinkTimer.Start();

            // Start WebSocket server
            StartWebSocketServer();

            // Initialize Playwright + start scrape timer
            Console.WriteLine("[PLAYWRIGHT] Initializing...");
            _ = InitPlaywrightAsync();
            _scrapeTimer = new System.Threading.Timer(async _ => await ScrapeAllMatches(), null, 3000, 10000);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Playwright initialization
        // ═══════════════════════════════════════════════════════════════
        private async Task InitPlaywrightAsync()
        {
            try
            {
                Console.WriteLine("[PLAYWRIGHT] Creating Playwright instance...");
                _playwright = await Playwright.CreateAsync();
                Console.WriteLine("[PLAYWRIGHT] Launching Chromium headless...");
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                });
                Console.WriteLine("[PLAYWRIGHT] ✓ Browser ready");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLAYWRIGHT] ✗ Init error: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  WebSocket Server
        // ═══════════════════════════════════════════════════════════════
        private void StartWebSocketServer()
        {
            try
            {
                _wssv = new WebSocketServer("ws://localhost:19000");
                _wssv.AddWebSocketService<FlashscoreBehavior>("/flashscore");
                _wssv.Start();
                Console.WriteLine("[WS] ✓ WebSocket server started on ws://localhost:19000/flashscore");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WS] ✗ WebSocket error: {ex.Message}");
            }
        }

        public void HandleWebSocketMessage(string message)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (!root.TryGetProperty("action", out var actionProp)) return;
                string action = actionProp.GetString() ?? "";
                Console.WriteLine($"[WS] Received action: {action} | message: {message}");

                if (action == "sync" && root.TryGetProperty("ids", out var idsProp))
                {
                    var ids = idsProp.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                    var currentKeys = _trackedIds.Keys.ToList();
                    foreach (var key in currentKeys) if (!ids.Contains(key)) _trackedIds.TryRemove(key, out _);
                    foreach (var id in ids) _trackedIds.TryAdd(id, id);
                    Console.WriteLine($"[WS] Synced {ids.Count} match IDs: [{string.Join(", ", ids)}]");
                    _ = ScrapeAllMatches();
                }
                else if (action == "add" && root.TryGetProperty("matchId", out var addIdProp))
                {
                    Console.WriteLine($"[WS] Adding match: {addIdProp.GetString()}");
                    _trackedIds.TryAdd(addIdProp.GetString() ?? "", addIdProp.GetString() ?? "");
                    _ = ScrapeAllMatches();
                }
                else if (action == "remove" && root.TryGetProperty("matchId", out var rmIdProp))
                {
                    string rmId = rmIdProp.GetString() ?? "";
                    Console.WriteLine($"[WS] Removing match: {rmId}");
                    _trackedIds.TryRemove(rmId, out _);
                    lock (_matchLock) { _matches.RemoveAll(m => m.MatchId == rmId); }
                    _removedIds.Add(rmId);
                    this.BeginInvoke(() => { RecalcHeight(); Invalidate(); });
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Scraping with Playwright
        // ═══════════════════════════════════════════════════════════════
        private async Task ScrapeAllMatches()
        {
            if (_browser == null) { Console.WriteLine("[SCRAPE] Skipping — browser not ready"); return; }
            if (_isScraping) { Console.WriteLine("[SCRAPE] Skipping — already scraping"); return; }
            _isScraping = true;

            try
            {
                // Process remove queue
                while (_idsToRemove.TryDequeue(out var idToRemove))
                {
                    _trackedIds.TryRemove(idToRemove, out _);
                    lock (_matchLock) { _matches.RemoveAll(m => m.MatchId == idToRemove); }
                    _removedIds.Add(idToRemove);
                    try { _wssv?.WebSocketServices["/flashscore"]?.Sessions.Broadcast($@"{{ ""action"": ""remove"", ""matchId"": ""{idToRemove}"" }}"); } catch { }
                }

                if (_trackedIds.IsEmpty)
                {
                    lock (_matchLock) { _matches.Clear(); }
                    this.BeginInvoke(() => { RecalcHeight(); Invalidate(); });
                    return;
                }

                Console.WriteLine($"[SCRAPE] Starting scrape of {_trackedIds.Count} matches: [{string.Join(", ", _trackedIds.Keys)}]");
                var newMatches = new List<MatchData>();

                foreach (var matchId in _trackedIds.Keys.ToList())
                {
                    if (_removedIds.Contains(matchId)) continue;

                    try
                    {
                        string idPart = matchId.Contains("_") ? matchId.Split('_').Last() : matchId;
                        string url = $"https://www.flashscore.es/partido/{idPart}/#/resumen-del-partido";
                        Console.WriteLine($"[SCRAPE] Navigating to {url}");

                        var page = await _browser.NewPageAsync();
                        try
                        {
                            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });

                            // Wait for content to render
                            try
                            {
                                await page.WaitForSelectorAsync(".duelParticipant__home", new PageWaitForSelectorOptions { Timeout = 8000 });
                                Console.WriteLine($"[SCRAPE] ✓ Page loaded for {matchId}");
                            }
                            catch
                            {
                                Console.WriteLine($"[SCRAPE] ⚠ Timeout waiting for .duelParticipant__home on {matchId}, extracting anyway");
                            }

                            Console.WriteLine($"[SCRAPE] Extracting data for {matchId}...");
                            var data = await ExtractMatchData(page, matchId);
                            if (data != null)
                            {
                                newMatches.Add(data);
                                Console.WriteLine($"[SCRAPE] ✓ {data.HomeTeam} vs {data.AwayTeam} | {data.HomeScore}-{data.AwayScore} | {data.MatchTime} | {data.LeagueCountry}: {data.League}");
                            }
                            else
                            {
                                Console.WriteLine($"[SCRAPE] ✗ No data extracted for {matchId}");
                            }
                        }
                        finally
                        {
                            await page.CloseAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SCRAPE] ✗ Error scraping {matchId}: {ex.Message}");
                    }
                }

                // Merge with existing (preserve alert state)
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lock (_matchLock)
                {
                    foreach (var nd in newMatches)
                    {
                        var existing = _matches.FirstOrDefault(m => m.MatchId == nd.MatchId);
                        if (existing != null)
                        {
                            // Check score changes for alerts
                            if (existing.HomeScore != nd.HomeScore || existing.AwayScore != nd.AwayScore)
                            {
                                nd.AlertExpiresMs = nowMs + 10000;
                            }
                            else
                            {
                                nd.AlertExpiresMs = existing.AlertExpiresMs;
                            }

                            // Check stage changes
                            string newCat = GetStageCategory(nd.MatchTime);
                            if (existing.StageCategory != "" && existing.StageCategory != newCat)
                            {
                                nd.StageAlertExpiresMs = nowMs + 10000;
                            }
                            else
                            {
                                nd.StageAlertExpiresMs = existing.StageAlertExpiresMs;
                            }
                            nd.StageCategory = newCat;
                            nd.PrevHomeScore = existing.HomeScore;
                            nd.PrevAwayScore = existing.AwayScore;

                            _matches.Remove(existing);
                        }
                        else
                        {
                            nd.StageCategory = GetStageCategory(nd.MatchTime);
                        }
                        _matches.Add(nd);
                    }

                    // Remove matches no longer tracked
                    _matches.RemoveAll(m => !_trackedIds.ContainsKey(m.MatchId));
                }

                // Preload images
                lock (_matchLock)
                {
                    foreach (var m in _matches)
                    {
                        PreloadImage(m.HomeImg);
                        PreloadImage(m.AwayImg);
                    }
                }

                Console.WriteLine($"[SCRAPE] Done. Total matches: {_matches.Count}");
                this.BeginInvoke(() => { RecalcHeight(); Invalidate(); });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SCRAPE] ✗ Fatal error: {ex.Message}");
            }
            finally
            {
                _isScraping = false;
            }
        }

        private async Task<MatchData?> ExtractMatchData(IPage page, string matchId)
        {
            try
            {
                var data = new MatchData { MatchId = matchId };

                // League info — from breadcrumbs (wcl-breadcrumbs)
                // Breadcrumb items: [Fútbol] → [España] → [LaLiga Hypermotion - Jornada 25]
                var breadcrumbItems = await page.QuerySelectorAllAsync(".wcl-breadcrumbs_0ZcSd li");
                if (breadcrumbItems.Count >= 3)
                {
                    // country = 2nd breadcrumb (e.g. "España")
                    data.LeagueCountry = (await breadcrumbItems[1].TextContentAsync())?.Trim() ?? "LEAGUE";
                    // league = 3rd breadcrumb (e.g. "LaLiga Hypermotion - Jornada 25")
                    data.League = (await breadcrumbItems[2].TextContentAsync())?.Trim() ?? "NAME";
                    // league URL from last breadcrumb link
                    var leagueLink = await breadcrumbItems[2].QuerySelectorAsync("a");
                    var href = leagueLink != null ? await leagueLink.GetAttributeAsync("href") : null;
                    data.LeagueUrl = href != null ? $"https://www.flashscore.es{href}" : "";
                }
                else
                {
                    // Fallback: try og:description meta tag (e.g. "ESPAÑA: LaLiga Hypermotion - Jornada 25")
                    var ogDesc = await SafeAttribute(page, "meta[property='og:description']", "content");
                    if (!string.IsNullOrEmpty(ogDesc) && ogDesc.Contains(":"))
                    {
                        var parts = ogDesc.Split(':', 2);
                        data.LeagueCountry = parts[0].Trim();
                        data.League = parts.Length > 1 ? parts[1].Trim() : "NAME";
                    }
                    data.LeagueUrl = "";
                }

                // Teams
                data.HomeTeam = await SafeTextContent(page, ".duelParticipant__home .participant__participantName") ?? "Home";
                data.AwayTeam = await SafeTextContent(page, ".duelParticipant__away .participant__participantName") ?? "Away";

                // Logos
                data.HomeImg = await SafeAttribute(page, ".duelParticipant__home img.participant__image", "src") ?? "";
                data.AwayImg = await SafeAttribute(page, ".duelParticipant__away img.participant__image", "src") ?? "";

                // Scores — check detailScore__wrapper first, then duelParticipant__score
                var scoreSpans = await page.QuerySelectorAllAsync(".detailScore__wrapper span");
                if (scoreSpans.Count >= 3)
                {
                    data.HomeScore = (await scoreSpans[0].TextContentAsync())?.Trim() ?? "-";
                    data.AwayScore = (await scoreSpans[2].TextContentAsync())?.Trim() ?? "-";
                }
                else
                {
                    // Match not started or score area structured differently
                    var scoreText = await SafeTextContent(page, ".duelParticipant__score");
                    if (!string.IsNullOrEmpty(scoreText) && scoreText.Contains("-"))
                    {
                        var parts = scoreText.Split('-');
                        data.HomeScore = parts[0].Trim();
                        data.AwayScore = parts.Length > 1 ? parts[1].Trim() : "-";
                    }
                    // else stays as default "-"
                }

                // Match time/status
                var timeSpans = await page.QuerySelectorAllAsync(".detailScore__status span");
                var timeTexts = new List<string>();
                foreach (var span in timeSpans)
                {
                    var txt = (await span.TextContentAsync())?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(txt)) timeTexts.Add(txt);
                }

                if (timeTexts.Count == 0)
                {
                    data.MatchTime = await SafeTextContent(page, ".detailScore__status") ?? "";
                    if (string.IsNullOrEmpty(data.MatchTime))
                        data.MatchTime = await SafeTextContent(page, ".duelParticipant__startTime") ?? "";
                }
                else if (timeTexts.Count == 2 && int.TryParse(timeTexts[1], out _))
                {
                    data.MatchTime = timeTexts[1]; // e.g. "2º tiempo" + "52" → show "52"
                }
                else if (timeTexts.Count == 1 && int.TryParse(timeTexts[0].TrimEnd('\''), out _))
                {
                    data.MatchTime = timeTexts[0].TrimEnd('\'');
                }
                else
                {
                    var joined = string.Join(" ", timeTexts);
                    var parts2 = joined.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var lastWord = parts2.Last().TrimEnd('\'');
                    if (int.TryParse(lastWord, out _)) data.MatchTime = lastWord;
                    else data.MatchTime = joined;
                }

                return data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SCRAPE] ✗ ExtractMatchData error: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> SafeTextContent(IPage page, string selector)
        {
            var el = await page.QuerySelectorAsync(selector);
            return el != null ? (await el.TextContentAsync())?.Trim() : null;
        }

        private async Task<string?> SafeAttribute(IPage page, string selector, string attr)
        {
            var el = await page.QuerySelectorAsync(selector);
            return el != null ? await el.GetAttributeAsync(attr) : null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Stage category (matches original JS logic)
        // ═══════════════════════════════════════════════════════════════
        private static string GetStageCategory(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "empty";
            var t = text.Trim();
            var lower = t.ToLowerInvariant();
            if (t == "HT" || lower.Contains("descanso")) return "halftime";
            if (lower.Contains("fin") || t == "F" || lower.Contains("post")) return "finished";
            if (t.Contains(':')) return "scheduled";
            if (t.Any(char.IsDigit)) return "live";
            return "other";
        }

        private static bool IsLiveOrHalftime(string time)
        {
            var cat = GetStageCategory(time);
            return cat == "live" || cat == "halftime";
        }

        // ═══════════════════════════════════════════════════════════════
        //  Image preloading
        // ═══════════════════════════════════════════════════════════════
        private void PreloadImage(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (_imageCache.ContainsKey(url)) return;
            if (_imageLoading.ContainsKey(url)) return;

            _imageLoading.TryAdd(url, true);
            _ = Task.Run(async () =>
            {
                try
                {
                    var bytes = await _httpClient.GetByteArrayAsync(url);
                    using var ms = new MemoryStream(bytes);
                    var img = Image.FromStream(ms);
                    _imageCache.TryAdd(url, img);
                }
                catch
                {
                    _imageCache.TryAdd(url, null);
                }
                finally
                {
                    _imageLoading.TryRemove(url, out _);
                }
                this.BeginInvoke(() => Invalidate());
            });
        }

        // ═══════════════════════════════════════════════════════════════
        //  Height calculation
        // ═══════════════════════════════════════════════════════════════
        private void RecalcHeight()
        {
            lock (_matchLock)
            {
                Console.WriteLine($"[RECALC] _matches.Count = {_matches.Count}");
                if (_matches.Count == 0)
                {
                    this.Height = MIN_HEIGHT;
                    return;
                }

                // Count unique leagues for headers
                var leagues = _matches.Select(m => $"{m.LeagueCountry}: {m.League}").Distinct().ToList();
                int totalHeight = leagues.Count * HEADER_HEIGHT + _matches.Count * MATCH_ROW_HEIGHT;
                int screenH = Screen.PrimaryScreen!.WorkingArea.Height;
                this.Height = Math.Max(MIN_HEIGHT, Math.Min(totalHeight, screenH - 100));
                Console.WriteLine($"[RECALC] New height: {this.Height} (leagues={leagues.Count}, matches={_matches.Count})");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  GDI+ Painting
        // ═══════════════════════════════════════════════════════════════
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            List<MatchData> snapshot;
            lock (_matchLock) { snapshot = _matches.ToList(); }

            Console.WriteLine($"[PAINT] snapshot.Count = {snapshot.Count}, FormSize = {this.Width}x{this.Height}");
            if (snapshot.Count == 0)
            {
                // "Selecciona partidos" placeholder
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("Selecciona partidos", _fontMuted, new SolidBrush(TextMuted), this.ClientRectangle, sf);
                return;
            }

            // Group by league
            var grouped = snapshot.GroupBy(m => $"{m.LeagueCountry}: {m.League}").ToList();
            int y = 0;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int matchGlobalIdx = 0;

            foreach (var group in grouped)
            {
                // ── Draw league header ──
                var headerRect = new Rectangle(0, y, this.Width, HEADER_HEIGHT);
                using (var headerBrush = new SolidBrush(BgHeader))
                    g.FillRectangle(headerBrush, headerRect);


                // League text
                var firstMatch = group.First();
                string leagueText = $"{firstMatch.LeagueCountry}: {firstMatch.League}";
                float leagueX = PADDING_H + 24;
                using var headerSf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
                var leagueTextRect = new RectangleF(leagueX, y + 7, this.Width - leagueX - 30, HEADER_HEIGHT - 8);
                g.DrawString(leagueText, _fontHeader, new SolidBrush(TextHeader), leagueTextRect, headerSf);


                // Border bottom
                using var borderPen = new Pen(BorderColor, 1);
                g.DrawLine(borderPen, 0, y + HEADER_HEIGHT - 1, this.Width, y + HEADER_HEIGHT - 1);

                y += HEADER_HEIGHT;

                // ── Draw each match row ──
                foreach (var match in group)
                {
                    bool isAlertActive = match.AlertExpiresMs > nowMs;
                    bool isStageAlert = match.StageAlertExpiresMs > nowMs;
                    bool isLive = IsLiveOrHalftime(match.MatchTime);
                    bool isHovering = matchGlobalIdx == _hoverMatchIndex;

                    var rowRect = new Rectangle(0, y, this.Width, MATCH_ROW_HEIGHT);

                    // Row background
                    Color rowBg = isAlertActive ? BgAlert : (isHovering ? BgMatchHover : BgMatch);
                    using (var rowBrush = new SolidBrush(rowBg))
                        g.FillRectangle(rowBrush, rowRect);

                    // Border bottom
                    g.DrawLine(borderPen, 0, y + MATCH_ROW_HEIGHT - 1, this.Width, y + MATCH_ROW_HEIGHT - 1);

                    int cx = PADDING_H;

                    // Col 2: Time/Status
                    Color timeColor = isLive ? TextLive : TextWhite;
                    Font timeFont = isLive ? _fontTimeLive : _fontTime;
                    string timeText = match.MatchTime;
                    Console.WriteLine($"[MARC] match = {match.MatchTime}");    

                    if (isLive && timeText.Any(char.IsDigit) && !timeText.Contains(':'))
                    {
                        // Draw minute + blinking apostrophe
                        timeText = timeText.Replace("'","").Split(" ")[2];
                        Console.WriteLine($"[MARC] timeText = {timeText}");

                        var timeSize = g.MeasureString(timeText, timeFont);
                        float timeY = y + MATCH_ROW_HEIGHT / 2 - timeSize.Height / 2;
                        g.DrawString(timeText, timeFont, new SolidBrush(timeColor), cx, timeY);

                        if (_blinkOn)
                        {
                            g.DrawString("'", timeFont, new SolidBrush(timeColor), cx + timeSize.Width - 2, timeY);
                        }
                    }
                    else
                    {
                        string tmp = "";
                        try
                        {
                            tmp = timeText.Split(' ')[1];
                        }
                        catch
                        {
                            tmp = timeText;
                        }
                        finally
                        {
                            timeText = tmp;
                        }
                        var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                        var timeRect = new RectangleF(cx, y, TIME_COL_W, MATCH_ROW_HEIGHT);
                        g.DrawString(timeText, timeFont, new SolidBrush(timeColor), timeRect, sf);
                    }

                    // Stage flash background
                    if (isStageAlert)
                    {
                        var stageFlashRect = new Rectangle(cx - 2, y + 5, TIME_COL_W + 4, MATCH_ROW_HEIGHT - 10);
                        using var flashBrush = new SolidBrush(BgStageFlair);
                        g.FillRectangle(flashBrush, stageFlashRect);
                        // Redraw time text on top of flash bg
                        var sfOverlay = new StringFormat { LineAlignment = StringAlignment.Center };
                        g.DrawString(timeText, timeFont, new SolidBrush(timeColor), new RectangleF(cx, y, TIME_COL_W, MATCH_ROW_HEIGHT), sfOverlay);
                    }

                    cx += TIME_COL_W;

                    // Col 3: Teams (two rows)
                    int teamAreaW = this.Width - cx - SCORE_COL_W - PADDING_H * 2;
                    int halfH = MATCH_ROW_HEIGHT / 2;

                    // Home team (top half)
                    DrawTeamRow(g, match.HomeImg, match.HomeTeam, cx, y + 2, teamAreaW, halfH - 2);

                    // Away team (bottom half)
                    DrawTeamRow(g, match.AwayImg, match.AwayTeam, cx, y + halfH, teamAreaW, halfH - 2);

                    // Col 4: Scores (right-aligned)
                    int scoreX = this.Width - SCORE_COL_W - PADDING_H;
                    Color scoreColor = isLive ? TextLive : TextWhite;

                    var sfScore = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    var homeScoreRect = new RectangleF(scoreX, y + 2, SCORE_COL_W, halfH - 2);
                    var awayScoreRect = new RectangleF(scoreX, y + halfH, SCORE_COL_W, halfH - 2);
                    g.DrawString(match.HomeScore, _fontScore, new SolidBrush(scoreColor), homeScoreRect, sfScore);
                    g.DrawString(match.AwayScore, _fontScore, new SolidBrush(scoreColor), awayScoreRect, sfScore);

                    y += MATCH_ROW_HEIGHT;
                    matchGlobalIdx++;
                }
            }
        }

        private void DrawTeamRow(Graphics g, string imgUrl, string teamName, int x, int y, int width, int height)
        {
            int textX = x;

            // Team logo
            if (!string.IsNullOrEmpty(imgUrl) && _imageCache.TryGetValue(imgUrl, out var img) && img != null)
            {
                int logoY = y + (height - LOGO_SIZE) / 2;
                g.DrawImage(img, new Rectangle(x, logoY, LOGO_SIZE, LOGO_SIZE));
                textX += LOGO_SIZE + 5;
            }

            // Team name
            var sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            };
            var nameRect = new RectangleF(textX, y, width - (textX - x), height);
            g.DrawString(teamName, _fontTeam, new SolidBrush(TextWhite), nameRect, sf);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Mouse interactions
        // ═══════════════════════════════════════════════════════════════
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                // Drag window
                ReleaseCapture();
                SendMessage(this.Handle, WM_SYSCOMMAND, new IntPtr(SC_MOVE + HTCAPTION), IntPtr.Zero);
            }
            else if (e.Button == MouseButtons.Right)
            {
                // Remove match under cursor
                var matchId = GetMatchIdAtPoint(e.Location);
                if (matchId != null)
                {
                    _idsToRemove.Enqueue(matchId);
                    _ = ScrapeAllMatches();
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            // Double-click on header → open league URL
            var leagueUrl = GetLeagueUrlAtPoint(e.Location);
            if (leagueUrl != null)
            {
                try
                {
                    string url = leagueUrl.StartsWith("http") ? leagueUrl : "https://www.flashscore.es" + leagueUrl;
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int newIdx = GetMatchIndexAtPoint(e.Location);
            if (newIdx != _hoverMatchIndex) { _hoverMatchIndex = newIdx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverMatchIndex != -1) { _hoverMatchIndex = -1; Invalidate(); }
        }

        // ── Hit-testing helpers ──
        private string? GetMatchIdAtPoint(Point pt)
        {
            List<MatchData> snapshot;
            lock (_matchLock) { snapshot = _matches.ToList(); }
            var grouped = snapshot.GroupBy(m => $"{m.LeagueCountry}: {m.League}").ToList();
            int y = 0;
            foreach (var group in grouped)
            {
                y += HEADER_HEIGHT;
                foreach (var match in group)
                {
                    if (pt.Y >= y && pt.Y < y + MATCH_ROW_HEIGHT) return match.MatchId;
                    y += MATCH_ROW_HEIGHT;
                }
            }
            return null;
        }

        private int GetMatchIndexAtPoint(Point pt)
        {
            List<MatchData> snapshot;
            lock (_matchLock) { snapshot = _matches.ToList(); }
            var grouped = snapshot.GroupBy(m => $"{m.LeagueCountry}: {m.League}").ToList();
            int y = 0;
            int idx = 0;
            foreach (var group in grouped)
            {
                y += HEADER_HEIGHT;
                foreach (var match in group)
                {
                    if (pt.Y >= y && pt.Y < y + MATCH_ROW_HEIGHT) return idx;
                    y += MATCH_ROW_HEIGHT;
                    idx++;
                }
            }
            return -1;
        }

        private string? GetLeagueUrlAtPoint(Point pt)
        {
            List<MatchData> snapshot;
            lock (_matchLock) { snapshot = _matches.ToList(); }
            var grouped = snapshot.GroupBy(m => $"{m.LeagueCountry}: {m.League}").ToList();
            int y = 0;
            foreach (var group in grouped)
            {
                if (pt.Y >= y && pt.Y < y + HEADER_HEIGHT)
                {
                    return group.First().LeagueUrl;
                }
                y += HEADER_HEIGHT;
                y += group.Count() * MATCH_ROW_HEIGHT;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  WndProc — prevent window activation on click
        // ═══════════════════════════════════════════════════════════════
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Cleanup
        // ═══════════════════════════════════════════════════════════════
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _scrapeTimer?.Dispose();
            _blinkTimer?.Stop();
            _blinkTimer?.Dispose();
            _wssv?.Stop();
            _browser?.CloseAsync().GetAwaiter().GetResult();
            _playwright?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
