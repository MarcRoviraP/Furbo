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
    //  Scraping Cache Data
    // ═══════════════════════════════════════════════════════════════════
    public class ScrapingCacheEntry
    {
        public string MatchId { get; set; } = "";
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        public string League { get; set; } = "";
        public string LeagueCountry { get; set; } = "";
        public string HomeImg { get; set; } = "";
        public string AwayImg { get; set; } = "";
        public string LeagueUrl { get; set; } = "";
        public string LeagueImgSrc { get; set; } = "";
        public long CachedAtMs { get; set; } = 0;
        public long ExpiresAtMs { get; set; } = 0;

        public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > ExpiresAtMs;
    }

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
        public string LeagueImgSrc { get; set; } = "";

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

        // ── Cache configuration ──
        private const long SCRAPING_CACHE_DURATION_MS = 3600000; // 1 hora

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

        // ── Scraping cache ──
        private readonly ConcurrentDictionary<string, ScrapingCacheEntry> _scrapingCache = new();
        private readonly HashSet<string> _cachedMatchIds = new();  // Track cached match IDs
        private long _cacheStatsHits = 0;
        private long _cacheStatsMisses = 0;

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

            // Initialize Playwright, then start the scrape timer once the browser is ready.
            Console.WriteLine("[PLAYWRIGHT] Initializing...");
            _ = InitPlaywrightAsync().ContinueWith(_ =>
            {
                Console.WriteLine("[PLAYWRIGHT] Starting scrape timer now that browser is ready.");
                _scrapeTimer = new System.Threading.Timer(async _ => await ScrapeAllMatches(), null, 0, 10000);
            });
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
                    InvalidateCacheForMatch(rmId);
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
            // Wait up to 10 s for the browser to finish initialising (relevant when a
            // WebSocket sync message arrives before Playwright has fully launched).
            if (_browser == null)
            {
                Console.WriteLine("[SCRAPE] Browser not ready yet — waiting up to 10 s...");
                int waited = 0;
                while (_browser == null && waited < 10000)
                {
                    await Task.Delay(500);
                    waited += 500;
                }
                if (_browser == null) { Console.WriteLine("[SCRAPE] Skipping — browser still not ready after 10 s"); return; }
                Console.WriteLine("[SCRAPE] Browser became ready after {0} ms", waited);
            }
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

                // ─ Clean expired cache entries ─
                ClearExpiredCacheEntries();

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
                            // Only trigger alert if BOTH old and new scores are valid (not "-")
                            // This avoids false alerts from failed score extraction
                            bool oldScoreValid = existing.HomeScore != "-" && existing.AwayScore != "-";
                            bool newScoreValid = nd.HomeScore != "-" && nd.AwayScore != "-";

                            if (oldScoreValid && newScoreValid && 
                                (existing.HomeScore != nd.HomeScore || existing.AwayScore != nd.AwayScore))
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
                PrintCacheStatistics();
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
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // ═══════════════════════════════════════════════════════════
                // ESPERAR A QUE LA PÁGINA ESTÉ COMPLETAMENTE CARGADA
                // ═══════════════════════════════════════════════════════════

                // Esperar a que el network esté idle (todas las peticiones AJAX terminadas)
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // Esperar a que el contenedor de scores exista (indica que el JS renderizó)
                await page.WaitForSelectorAsync(".detailScore__wrapper, .detailScore, .duelParticipant", new()
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 10000
                });

                // Pequeña espera adicional para asegurar que el JS haya poblado los datos
                await Task.Delay(500);

                // ─ Check cache for STATIC data only ─
                bool cacheHit = false;
                if (_scrapingCache.TryGetValue(matchId, out var cachedEntry) && !cachedEntry.IsExpired)
                {
                    Interlocked.Increment(ref _cacheStatsHits);
                    Console.WriteLine($"[CACHE] ✓ HIT for {matchId}");
                    cacheHit = true;

                    // Use cached static data
                    data.HomeTeam = cachedEntry.HomeTeam;
                    data.AwayTeam = cachedEntry.AwayTeam;
                    data.League = cachedEntry.League;
                    data.LeagueCountry = cachedEntry.LeagueCountry;
                    data.HomeImg = cachedEntry.HomeImg;
                    data.AwayImg = cachedEntry.AwayImg;
                    data.LeagueUrl = cachedEntry.LeagueUrl;
                    data.LeagueImgSrc = cachedEntry.LeagueImgSrc;
                }
                else
                {
                    Interlocked.Increment(ref _cacheStatsMisses);
                    Console.WriteLine($"[CACHE] ✗ MISS for {matchId}");

                    // League info — from breadcrumbs
                    var breadcrumbItems = await page.QuerySelectorAllAsync(".wcl-breadcrumbs_0ZcSd li");
                    if (breadcrumbItems.Count >= 3)
                    {
                        var countryElement = breadcrumbItems[1];
                        data.LeagueImgSrc = await ExtractLeagueFlagImage(page, countryElement);
                        data.LeagueCountry = (await countryElement.TextContentAsync())?.Trim() ?? "LEAGUE";
                        data.League = (await breadcrumbItems[2].TextContentAsync())?.Trim() ?? "NAME";

                        var leagueLink = await breadcrumbItems[2].QuerySelectorAsync("a");
                        var href = leagueLink != null ? await leagueLink.GetAttributeAsync("href") : null;
                        data.LeagueUrl = href != null ? $"https://www.flashscore.es{href}" : "";
                    }
                    else
                    {
                        // Fallback...
                        var ogDesc = await SafeAttribute(page, "meta[property='og:description']", "content");
                        if (!string.IsNullOrEmpty(ogDesc) && ogDesc.Contains(":"))
                        {
                            var parts = ogDesc.Split(':', 2);
                            data.LeagueCountry = parts[0].Trim();
                            data.League = parts.Length > 1 ? parts[1].Trim() : "NAME";
                        }
                        data.LeagueUrl = "";
                        data.LeagueImgSrc = await ExtractLeagueFlagImageGlobal(page);
                    }

                    // Teams
                    data.HomeTeam = await SafeTextContent(page, ".duelParticipant__home .participant__participantName") ?? "Home";
                    data.AwayTeam = await SafeTextContent(page, ".duelParticipant__away .participant__participantName") ?? "Away";

                    // Logos
                    data.HomeImg = await SafeAttribute(page, ".duelParticipant__home img.participant__image", "src") ?? "";
                    data.AwayImg = await SafeAttribute(page, ".duelParticipant__away img.participant__image", "src") ?? "";
                }

                // ═══════════════════════════════════════════════════════════
                // EXTRACCIÓN DE SCORES CON ESPERAS Y RETRIES
                // ═══════════════════════════════════════════════════════════

                data.HomeScore = "-";
                data.AwayScore = "-";

                // Intentar obtener scores con retry
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    var scores = await TryExtractScores(page);
                    if (scores != null)
                    {
                        data.HomeScore = scores.Value.Home;
                        data.AwayScore = scores.Value.Away;
                        Console.WriteLine($"[SCORE] ✓ {data.HomeScore} - {data.AwayScore} (attempt {attempt + 1})");
                        break;
                    }

                    // Esperar antes de reintentar
                    await Task.Delay(200);
                }

                if (data.HomeScore == "-" && data.AwayScore == "-")
                {
                    Console.WriteLine($"[SCORE] ✗ Could not extract scores after retries");
                }

                // ═══════════════════════════════════════════════════════════
                // EXTRACCIÓN DE TIEMPO CON ESPERAS
                // ═══════════════════════════════════════════════════════════

                data.MatchTime = await ExtractMatchTime(page);

                // ─ Cache the scraped data ─
                if (!cacheHit)
                {
                    var cacheEntry = new ScrapingCacheEntry
                    {
                        MatchId = data.MatchId,
                        HomeTeam = data.HomeTeam,
                        AwayTeam = data.AwayTeam,
                        League = data.League,
                        LeagueCountry = data.LeagueCountry,
                        HomeImg = data.HomeImg,
                        AwayImg = data.AwayImg,
                        LeagueUrl = data.LeagueUrl,
                        LeagueImgSrc = data.LeagueImgSrc,
                        CachedAtMs = nowMs,
                        ExpiresAtMs = nowMs + SCRAPING_CACHE_DURATION_MS
                    };
                    _scrapingCache.AddOrUpdate(matchId, cacheEntry, (_, __) => cacheEntry);
                    RegisterCachedMatchId(matchId);
                    Console.WriteLine($"[CACHE] ✓ STORED {matchId}");
                }

                return data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SCRAPE] ✗ ExtractMatchData error: {ex.Message}");
                return null;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // MÉTODO AUXILIAR: Extraer scores con múltiples estrategias
        // ═════════════════════════════════════════════════════════════════

        private async Task<(string Home, string Away)?> TryExtractScores(IPage page)
        {
            // Estrategia 1: detailScore__wrapper
            var scoreWrapper = await page.QuerySelectorAsync(".detailScore__wrapper");
            if (scoreWrapper != null)
            {
                var spans = await scoreWrapper.QuerySelectorAllAsync("span");
                if (spans.Count >= 3)
                {
                    var home = (await spans[0].TextContentAsync())?.Trim();
                    var away = (await spans[2].TextContentAsync())?.Trim();

                    if (IsValidScore(home) && IsValidScore(away))
                        return (home!, away!);
                }
            }

            // Estrategia 2: duelParticipant__score individual
            var homeScoreEl = await page.QuerySelectorAsync(".duelParticipant__home .duelParticipant__score");
            var awayScoreEl = await page.QuerySelectorAsync(".duelParticipant__away .duelParticipant__score");

            if (homeScoreEl != null && awayScoreEl != null)
            {
                var home = (await homeScoreEl.TextContentAsync())?.Trim();
                var away = (await awayScoreEl.TextContentAsync())?.Trim();

                if (IsValidScore(home) && IsValidScore(away))
                    return (home!, away!);
            }

            // Estrategia 3: detailScore general
            var detailScore = await page.QuerySelectorAsync(".detailScore");
            if (detailScore != null)
            {
                // Usar EvaluateAsync para obtener texto directamente del DOM
                var scores = await detailScore.EvaluateAsync<string[]>(@"
            el => {
                const spans = el.querySelectorAll(':scope > span');
                return [spans[0]?.innerText, spans[2]?.innerText];
            }
        ");

                if (scores != null && scores.Length >= 2 &&
                    IsValidScore(scores[0]) && IsValidScore(scores[1]))
                    return (scores[0]!, scores[1]!);
            }

            return null;
        }

        private bool IsValidScore(string? score)
        {
            if (string.IsNullOrEmpty(score)) return false;
            // Acepta números o guiones (para partidos no iniciados)
            return score == "-" || int.TryParse(score, out _);
        }

        // ═════════════════════════════════════════════════════════════════
        // MÉTODO AUXILIAR: Extraer tiempo del partido
        // ═════════════════════════════════════════════════════════════════

        private async Task<string> ExtractMatchTime(IPage page)
        {
            // Esperar a que alguno de los elementos de tiempo exista
            await page.WaitForSelectorAsync(".eventTime, .detailScore__status, .duelParticipant__startTime", new()
            {
                Timeout = 5000
            });

            // ═══════════════════════════════════════════════════════════
            // ESTRATEGIA 1: eventTime (ej: <span class="eventTime">26<span class="event__timeIndicator">'</span></span>)
            // ═══════════════════════════════════════════════════════════
            var eventTimeEl = await page.QuerySelectorAsync(".eventTime");
            if (eventTimeEl != null)
            {
                // Obtener solo el texto del span principal (ignorar el timeIndicator)
                var timeText = await eventTimeEl.TextContentAsync();
                if (!string.IsNullOrEmpty(timeText))
                {
                    // Limpiar: quitar el apóstrofe y espacios
                    var cleaned = timeText.Trim().TrimEnd('\'').Trim();

                    // Verificar que sea un número válido
                    if (int.TryParse(cleaned, out _))
                    {
                        Console.WriteLine($"[TIME] ✓ From eventTime: {cleaned}");
                        return cleaned;
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════
            // ESTRATEGIA 2: spans dentro de detailScore__status
            // ═══════════════════════════════════════════════════════════
            var statusWrapper = await page.QuerySelectorAsync(".detailScore__status");
            if (statusWrapper != null)
            {
                var spans = await statusWrapper.QuerySelectorAllAsync("span");
                var texts = new List<string>();

                foreach (var span in spans)
                {
                    var txt = (await span.TextContentAsync())?.Trim();
                    if (!string.IsNullOrEmpty(txt)) texts.Add(txt);
                }

                if (texts.Count > 0)
                {
                    // Buscar número al final (tiempo)
                    var joined = string.Join(" ", texts);
                    var parts = joined.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length > 0)
                    {
                        var last = parts.Last().TrimEnd('\'');
                        if (int.TryParse(last, out _)) return last;
                    }

                    return joined;
                }
            }

            // ═══════════════════════════════════════════════════════════
            // ESTRATEGIA 3: texto directo de detailScore__status
            // ═══════════════════════════════════════════════════════════
            var statusText = await SafeTextContent(page, ".detailScore__status");
            if (!string.IsNullOrEmpty(statusText)) return statusText;

            // ═══════════════════════════════════════════════════════════
            // ESTRATEGIA 4: duelParticipant__startTime
            // ═══════════════════════════════════════════════════════════
            var startTime = await SafeTextContent(page, ".duelParticipant__startTime");
            if (!string.IsNullOrEmpty(startTime)) return startTime;

            return "";
        }

        // Mantener tus métodos auxiliares existentes
        private async Task<string?> SafeTextContent(IPage page, string selector)
        {
            var el = await page.QuerySelectorAsync(selector);
            return el != null ? (await el.TextContentAsync())?.Trim() : null;
        }

        private async Task<string?> SafeAttribute(IPage page, string selector, string attribute)
        {
            var el = await page.QuerySelectorAsync(selector);
            return el != null ? await el.GetAttributeAsync(attribute) : null;
        }


        // ─ Extract league flag image with multiple strategies ─
        private async Task<string> ExtractLeagueFlagImage(IPage page, IElementHandle countryElement)
        {
            try
            {
                // Strategy 1: Direct img in country element
                var imgElement = await countryElement.QuerySelectorAsync("img");
                if (imgElement != null)
                {
                    var srcAttr = await imgElement.GetAttributeAsync("src");
                    if (!string.IsNullOrEmpty(srcAttr))
                    {
                        Console.WriteLine($"[FLAG] Strategy 1 (direct img): Found {(srcAttr.StartsWith("data:") ? "base64" : "URL")}");
                        return srcAttr;
                    }
                }

                // Strategy 2: Try data-src (lazy loading)
                if (imgElement != null)
                {
                    var dataSrcAttr = await imgElement.GetAttributeAsync("data-src");
                    if (!string.IsNullOrEmpty(dataSrcAttr))
                    {
                        Console.WriteLine($"[FLAG] Strategy 2 (data-src): Found");
                        return dataSrcAttr;
                    }
                }

                // Strategy 3: Extract via style background-image
                if (imgElement != null)
                {
                    var styleAttr = await imgElement.GetAttributeAsync("style");
                    if (!string.IsNullOrEmpty(styleAttr) && styleAttr.Contains("background-image"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(styleAttr, @"background-image:\s*url\(([^)]+)\)");
                        if (match.Success)
                        {
                            var url = match.Groups[1].Value.Trim('\'', '"');
                            if (!string.IsNullOrEmpty(url))
                            {
                                Console.WriteLine($"[FLAG] Strategy 3 (style background): Found");
                                return url;
                            }
                        }
                    }
                }

                // Strategy 4: Search for any img with wcl-flag class
                var flagImg = await countryElement.QuerySelectorAsync("img[class*='wcl-flag']");
                if (flagImg != null)
                {
                    var srcAttr = await flagImg.GetAttributeAsync("src");
                    if (!string.IsNullOrEmpty(srcAttr))
                    {
                        Console.WriteLine($"[FLAG] Strategy 4 (wcl-flag class): Found");
                        return srcAttr;
                    }
                }

                Console.WriteLine($"[FLAG] No flag found using any strategy");
                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FLAG] Error extracting flag: {ex.Message}");
                return "";
            }
        }

        // ─ Global flag search (fallback) ─
        private async Task<string> ExtractLeagueFlagImageGlobal(IPage page)
        {
            try
            {
                // Try to find any flag image on the page
                var flagImg = await page.QuerySelectorAsync("img[class*='wcl-flag']");
                if (flagImg != null)
                {
                    var srcAttr = await flagImg.GetAttributeAsync("src");
                    if (!string.IsNullOrEmpty(srcAttr))
                    {
                        Console.WriteLine($"[FLAG] Global fallback: Found");
                        return srcAttr;
                    }
                }

                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FLAG] Error in global search: {ex.Message}");
                return "";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Scraping Cache Management
        // ═══════════════════════════════════════════════════════════════
        private void ClearExpiredCacheEntries()
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var expiredKeys = _scrapingCache
                .Where(kvp => kvp.Value.ExpiresAtMs < nowMs)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                if (_scrapingCache.TryRemove(key, out var removed))
                {
                    Console.WriteLine($"[CACHE] Removed expired entry: {key}");
                }
            }

            if (expiredKeys.Count > 0)
            {
                Console.WriteLine($"[CACHE] Cleaned {expiredKeys.Count} expired entries. Remaining: {_scrapingCache.Count}");
            }
        }

        private void PrintCacheStatistics()
        {
            long totalRequests = _cacheStatsHits + _cacheStatsMisses;
            double hitRate = totalRequests > 0 ? (_cacheStatsHits * 100.0) / totalRequests : 0;

            Console.WriteLine($"[CACHE] ═══════════════════════════════════════");
            Console.WriteLine($"[CACHE] Cache Statistics:");
            Console.WriteLine($"[CACHE]   Total Hits:    {_cacheStatsHits}");
            Console.WriteLine($"[CACHE]   Total Misses:  {_cacheStatsMisses}");
            Console.WriteLine($"[CACHE]   Hit Rate:      {hitRate:F1}%");
            Console.WriteLine($"[CACHE]   Cached Items:  {_scrapingCache.Count}");
            Console.WriteLine($"[CACHE] ═══════════════════════════════════════");
        }

        public void ClearScrapingCache()
        {
            _scrapingCache.Clear();
            _cacheStatsHits = 0;
            _cacheStatsMisses = 0;
            Console.WriteLine($"[CACHE] Cache cleared completely");
        }

        private void InvalidateCacheForMatch(string matchId)
        {
            if (_scrapingCache.TryRemove(matchId, out _))
            {
                _cachedMatchIds.Remove(matchId);
                Console.WriteLine($"[CACHE] Invalidated cache for {matchId}");
            }
        }

        private void RegisterCachedMatchId(string matchId)
        {
            lock (_cachedMatchIds)
            {
                _cachedMatchIds.Add(matchId);
            }
        }

        public List<string> GetCachedMatchIds()
        {
            lock (_cachedMatchIds)
            {
                return _cachedMatchIds.ToList();
            }
        }

        public bool IsCachedMatch(string matchId)
        {
            lock (_cachedMatchIds)
            {
                return _cachedMatchIds.Contains(matchId);
            }
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

            //Console.WriteLine($"[PAINT] snapshot.Count = {snapshot.Count}, FormSize = {this.Width}x{this.Height}");
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

                Image flagImage = null;

                // League text
                var firstMatch = group.First();
                // Opción 1: Si tienes la imagen en base64 (desde el scraping)
                if (!string.IsNullOrEmpty(firstMatch.LeagueImgSrc))
                {
                    var base64Data = firstMatch.LeagueImgSrc.Replace("data:image/png;base64,", "");
                    byte[] imageBytes = Convert.FromBase64String(base64Data);
                    using var ms = new MemoryStream(imageBytes);
                    flagImage = Image.FromStream(ms);
                }
                // Dibujar la bandera si existe
                int flagSize = 16; // Tamaño de la bandera
                int flagX = PADDING_H + 5;
                int flagY = y + (HEADER_HEIGHT - flagSize) / 2; // Centrar verticalmente

                if (flagImage != null)
                {
                    // Dibujar imagen redimensionada
                    g.DrawImage(flagImage, flagX, flagY, flagSize, flagSize);
                    flagImage.Dispose(); // Liberar si no la necesitas más
                }
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
                        timeText = timeText.Replace("'","");
                        Console.WriteLine($"[MARC] timeText = {timeText}");

                        var timeSize = g.MeasureString(timeText, timeFont);
                        float apoWidth = _blinkOn ? g.MeasureString("'", timeFont).Width : 0;
                        float totalTimeWidth = timeSize.Width + apoWidth;

                        // Center time text within available space
                        float timeX = cx + (TIME_COL_W - totalTimeWidth) / 2;
                        float timeY = y + MATCH_ROW_HEIGHT / 2 - timeSize.Height / 2;

                        g.DrawString(timeText, timeFont, new SolidBrush(timeColor), timeX, timeY);

                        if (_blinkOn)
                        {
                            g.DrawString("'", timeFont, new SolidBrush(timeColor), timeX + timeSize.Width - 2, timeY);
                        }
                    }
                    else
                    {
                        // Measure actual text size and center it
                        var timeSize = g.MeasureString(timeText, timeFont);
                        float timeX = cx + (TIME_COL_W - timeSize.Width) / 2;
                        float timeY = y + MATCH_ROW_HEIGHT / 2 - timeSize.Height / 2;

                        g.DrawString(timeText, timeFont, new SolidBrush(timeColor), timeX, timeY);
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
