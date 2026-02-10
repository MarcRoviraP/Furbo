using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;

namespace Furbo
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            MainWindow main = new MainWindow();
            this.MainWindow = main;
        }
    }

    public partial class MainWindow : Window
    {
        private HttpListener listener;
        private Thread listenerThread;
        private bool isInitialized = false;
        private bool isShowingSnippet = false;
        private int currentPort = 0;
        private readonly System.Threading.SemaphoreSlim navigationLock = new System.Threading.SemaphoreSlim(1, 1);

        public MainWindow()
        {
            InitializeComponent();
            StartHttpListener();

            this.ShowInTaskbar = false;
            this.Visibility = Visibility.Hidden;

            this.Loaded += (s, e) =>
            {
                this.Topmost = true;
            };
        }

        async System.Threading.Tasks.Task SetupWebView2Async()
        {
            if (isInitialized) return;

            try
            {
                await webView.EnsureCoreWebView2Async(null);

                // Fondo negro por defecto para evitar parpadeos blancos
                webView.DefaultBackgroundColor = System.Drawing.Color.Black;

                webView.SourceChanged += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"SourceChanged: {webView.Source}");
                };

                webView.CoreWebView2.NavigationStarting += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[WebView] Iniciando navegación a: {e.Uri}");
                    // Si estamos en modo fragmento, bloqueamos cualquier navegación externa
                    if (isShowingSnippet && !string.IsNullOrEmpty(e.Uri) && e.Uri != "about:blank" && !e.Uri.StartsWith("data:"))
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebView] BLOQUEADO: Intento de navegación externa en modo fragmento a {e.Uri}");
                        e.Cancel = true;
                    }
                };

                // Mover el registro del evento aquí para que solo se haga una vez
                webView.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[WebView] Navegación completada (Éxito: {e.IsSuccess}, SnippetMode: {isShowingSnippet}, URI: {webView.Source})");

                    if (isShowingSnippet)
                    {
                        Title = "Furbo - Vista de Partido (Fragmento cargado)";
                        System.Diagnostics.Debug.WriteLine("[WebView] Manteniendo fragmento, ignorando lógica de aislamiento.");
                        return;
                    }

                    Title = $"Furbo - {webView.CoreWebView2.DocumentTitle}";

                    // Si hay HTML pendiente para inyectar, hacerlo ahora que la página cargó
                    if (shouldInjectOnLoad && !string.IsNullOrEmpty(pendingDivHtml))
                    {
                        System.Diagnostics.Debug.WriteLine("[WebView] Página cargada, inyectando HTML pendiente...");
                        await System.Threading.Tasks.Task.Delay(500); // Pequeño delay para asegurar que CSS esté listo
                        await InjectDivHtml(pendingDivHtml);
                        pendingDivHtml = null;
                        shouldInjectOnLoad = false;
                    }
                    // Solo aplicar aislamiento si estamos en la web de flashscore y no hay inyección pendiente
                    else if (webView.Source.ToString().Contains("flashscore.es"))
                    {
                        await System.Threading.Tasks.Task.Delay(1000);
                        await IsolateMatchElement();
                    }
                };

                isInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar WebView2: {ex.Message}");
            }
        }

        private string pendingDivHtml = null;
        private bool shouldInjectOnLoad = false;

        async System.Threading.Tasks.Task NavigateToFlashscore()
        {
            isShowingSnippet = false;
            await SetupWebView2Async();

            // Habilitar scripts para la web original
            webView.CoreWebView2.Settings.IsScriptEnabled = true;

            System.Diagnostics.Debug.WriteLine("[App] Navegando a Flashscore...");
            webView.CoreWebView2.Navigate("https://www.flashscore.es/");
        }

        private async System.Threading.Tasks.Task InjectDivHtml(string divHtml)
        {
            if (string.IsNullOrEmpty(divHtml))
            {
                System.Diagnostics.Debug.WriteLine("[App] No se proporcionó HTML del div");
                return;
            }

            // Escapar el HTML para JavaScript (reemplazar comillas y saltos de línea)
            string escapedHtml = divHtml
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

            string script = $@"
                (function() {{
                    console.log('[Furbo] Inyectando HTML del div...');
                    
                    // Ocultar todo el contenido existente del body
                    const allBodyChildren = Array.from(document.body.children);
                    allBodyChildren.forEach(child => {{
                        child.style.display = 'none';
                    }});
                    
                    // Configurar el body
                    document.body.style.margin = '0';
                    document.body.style.padding = '0';
                    document.body.style.overflow = 'hidden';
                    document.body.style.backgroundColor = '#000000';
                    
                    // Crear un overlay absoluto que cubra toda la pantalla
                    const overlay = document.createElement('div');
                    overlay.id = 'furbo-overlay';
                    overlay.style.position = 'fixed';
                    overlay.style.top = '0';
                    overlay.style.left = '0';
                    overlay.style.width = '100vw';
                    overlay.style.height = '100vh';
                    overlay.style.backgroundColor = '#000000';
                    overlay.style.display = 'flex';
                    overlay.style.alignItems = 'center';
                    overlay.style.justifyContent = 'center';
                    overlay.style.zIndex = '999999';
                    overlay.style.padding = '40px';
                    overlay.style.boxSizing = 'border-box';
                    
                    // Crear un wrapper para el contenido con ancho completo
                    const contentWrapper = document.createElement('div');
                    contentWrapper.style.width = '100%';
                    contentWrapper.style.maxWidth = '1200px';
                    contentWrapper.style.transform = 'scale(1.8)';
                    contentWrapper.style.transformOrigin = 'center center';
                    
                    // Insertar el HTML del div
                    contentWrapper.innerHTML = '{escapedHtml}';
                    
                    // Asegurar que el div inyectado tenga el ancho completo
                    const injectedDiv = contentWrapper.firstElementChild;
                    if (injectedDiv) {{
                        injectedDiv.style.width = '100%';
                        injectedDiv.style.minHeight = '60px';
                    }}
                    
                    // Agregar el contenido al overlay
                    overlay.appendChild(contentWrapper);
                    
                    // Agregar el overlay al body
                    document.body.appendChild(overlay);
                    
                    console.log('[Furbo] HTML inyectado correctamente');
                }})();
            ";

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(script);
                System.Diagnostics.Debug.WriteLine("[App] Script de inyección ejecutado correctamente");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Error al ejecutar script de inyección: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task ShowHtmlSnippet(string html)
        {
            System.Diagnostics.Debug.WriteLine("[App] Preparando para mostrar fragmento HTML...");
            isShowingSnippet = true;
            await SetupWebView2Async();

            // Detener cualquier navegación actual antes de inyectar el fragmento
            webView.CoreWebView2.Stop();

            // Deshabilitar scripts para evitar que el fragmento intente recargar o redirigir
            webView.CoreWebView2.Settings.IsScriptEnabled = true;
            System.Diagnostics.Debug.WriteLine("[App] Scripts habilitados, inyectando HTML...");
            Debug.WriteLine(html);

            // CSS básico para que el div se vea bien sobre fondo negro
            string styledHtml = $@"
                
<!DOCTYPE html>
<html lang=""es"">
    <head>
        <meta charset=""utf-8"">
        <title>Resultados de fútbol en directo, LaLiga EA Sports - MisMarcadores | Flashscore.es</title>
        <script defer src=""https://cdn.cookielaw.org/scripttemplates/otSDKStub.js""
            type=""text/javascript""
            charset=""UTF-8""
            data-domain-script=""5ef38f05-1491-407f-b997-0589f9ea8c92"" data-document-language=""true""
        >
        </script>
<link rel=""preconnect"" href=""https://global.ds.lsapp.eu/pq_graphql"" crossorigin>
<link rel=""preconnect"" href=""https://13.flashscore.ninja"" crossorigin>
<link rel=""preload"" href=""https://static.flashscore.com/res/font/LivesportFinderLatin-Regular_Static.woff2"" as=""font"" type=""font/woff2"" crossorigin>
<link rel=""preload"" href=""https://static.flashscore.com/res/font/LivesportFinderLatin-Bold_Static.woff2"" as=""font"" type=""font/woff2"" crossorigin>
        <noscript>
            <meta http-equiv=""refresh"" content=""0;url=https://m.flashscore.es/"" />
        </noscript>
        <meta name=""description"" content=""El servicio de marcadores y resultados de fútbol en directo de Flashscore.es ofrece los resultados de más de 1000 competiciones de fútbol, entre ellas LaLiga EA Sports, Premier League, Bundesliga, Serie A, Ligue 1, Champions League, etc. Consulta los marcadores en directo, resultados, clasificaciones, alineaciones y detalles de los partidos."">
        <meta name=""copyright"" content=""Copyright (c) 2006-2026 Livesport s.r.o."">
        <meta name=""robots"" content=""index,follow"" />
        <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
	    <meta property=""og:title"" content=""Resultados de fútbol en directo, LaLiga EA Sports - MisMarcadores | Flashscore.es"">
	    <meta property=""og:description"" content=""El servicio de marcadores y resultados de fútbol en directo de Flashscore.es ofrece los resultados de más de 1000 competiciones de fútbol, entre ellas LaLiga EA Sports, Premier League, Bundesliga, Serie A, Ligue 1, Champions League, etc. Consulta los marcadores en directo, resultados, clasificaciones, alineaciones y detalles de los partidos."">
	    <meta property=""og:type"" content=""website"">
	    <meta property=""og:url"" content=""https://www.flashscore.es/"">
	    <meta property=""og:image"" content=""https://www.flashscore.es/res/_fs/image/og/flashscore.png"">
	    <meta name=""fb:app_id"" content=""125754474284594"">
            <link rel=""shortcut icon"" href=""https://static.flashscore.com/res/_fs/image/4_favicons/_fs/favicon.ico?v=8"">
            <link rel=""apple-touch-icon"" sizes=""180x180"" href=""https://static.flashscore.com/res/_fs/image/4_favicons/_fs/touch-icon-180x180.png?v=8"">
            <link rel=""icon"" type=""image/png"" sizes=""32x32"" href=""https://static.flashscore.com/res/_fs/image/4_favicons/_fs/favicon-32x32.png?v=8"">
            <link rel=""icon"" type=""image/png"" sizes=""16x16"" href=""https://static.flashscore.com/res/_fs/image/4_favicons/_fs/favicon-16x16.png?v=8"">
        <link rel=""manifest"" href=""/manifest/1/?v=7"">
        <meta name=""theme-color"" content=""#001e28"">
        <meta name=""apple-itunes-app"" content=""app-id=766443283"">
            <meta name=""google-site-verification"" content=""-alMNhNXn5-JVw6Sv8eXWIGyti89oH9f089LwQuZ9ig"" />
<meta name=""facebook-domain-verification"" content=""5lv6dkf7twm5obe4eptqje9pat0002"" />

        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/LivesportFinderLatin.b5b9ae1.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/core.d3330bf.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/variables.5e5bd96.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/themes.a4e5af4.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/common.dd5a8cc.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/components_shared.7255990.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/cookie.bd3eb7d.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/multiLang.e42395f.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/single_page_app_temp.344cf30.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/core_common.852f6a3.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/lsid.4ca23f3.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/componentLibraryTheme2021.1e9608d.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/live_header.eacd0eb.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/live_sidemenu.b72290c.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/live_sections.c156322.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/league_onboarding.205502f.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/live_footer.8dcd350.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/tabs_filters.a0b0bc9.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/live_tabs.ed02cca.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/headline.5c06c67.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/heading.d35ceaa.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/fsnews_scores.6c2d2b4.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/rssnews.b0bfd58.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/rssnews_scores.aee54d5.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/player_table_spa.e6485eb.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/rest_player_tables.4875de6.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/ranking.4f1631d.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/seasonCalendar.871df01.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/common_category.d6af2ef.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/standings_draw.9c2a659.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/banner.9044da6.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/storeBadge.f227bb4.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/soccer_template.70cbe2c.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/flashfootball.c17c95d.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/sport_templates_layouts.fb2c9d0.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/styles/container.13.css"">
        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/styles/container.0.css"">
            
    <link rel=""canonical"" href=""https://www.flashscore.es/"">
            <link rel=""alternate"" hreflang=""es"" href=""https://www.flashscore.es/"">
            <link rel=""alternate"" hreflang=""es-es"" href=""https://www.flashscore.es/"">
            <link rel=""alternate"" hreflang=""es-pe"" href=""https://www.flashscore.pe/"">
            <link rel=""alternate"" hreflang=""es-co"" href=""https://www.flashscore.co/"">
            <link rel=""alternate"" hreflang=""es-mx"" href=""https://www.flashscore.com.mx/"">
            <link rel=""alternate"" hreflang=""es-ar"" href=""https://www.flashscore.com.ar/"">
            <link rel=""alternate"" hreflang=""es-ve"" href=""https://www.flashscore.com.ve/"">
            <link rel=""alternate"" hreflang=""es-cl"" href=""https://www.flashscore.cl/"">

        <script type=""text/javascript"" src=""/x/js/browsercompatibility_5.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/framework.3391317.js""></script>
        <script type=""text/javascript"" defer src=""/x/js/core_13_2292000000.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/internalTools.5d41de3.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/legalAgeConfirmation.689fb27.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/initBannerHandler.6446bc0.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/vendors.35319d8.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/modules.76d5911.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/serviceStatusBox.187ba10.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/liveTable.29f5ac3.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/myLeaguesMenu.8f6b6fd.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/mainPageScripts.56d5944.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/leftMenuCategory.6d8ffae.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/globalEvents.5106c22.js""></script>
        <script type=""text/javascript"" defer src=""/res/_fs/build/notifications.91994f2.js""></script>
        <script type=""text/javascript"" src=""/x/js/translations-livetable.13.29291a80.js""></script>
        <script type=""text/javascript"" src=""/x/js/translations-myteamsmenu.13.3d51451f.js""></script>
        <script type=""text/javascript"" src=""/x/js/translations-headermenu.13.08a289f8.js""></script>
        <script type=""text/javascript"" src=""/x/js/translations-headerpromobar.13.51776c1a.js""></script>
        <script type=""text/javascript"" src=""/res/_fs/build/runtime.d3472c5.js""></script>
        <script type=""text/javascript"" src=""/res/_fs/build/constants.b9c71a8.js""></script>
        <script type=""text/javascript"" src=""/res/_fs/build/loader.f9ac463.js""></script>
        <script type=""text/javascript"" src=""/res/_fs/build/myTeamsMenu.ce07ba0.js""></script>
                <script>
        window.loggingServiceConfig = {{""enable"":true,""server"":""https:\/\/logging-service.livesport.services\/"",""token"":""Y3uhIv5Ges46mMdAZm53akso95sYOogk"",""percentage_of_sessions_to_log"":1}};
    </script>
    <script defer src=""/res/_fs/build/frontendLogger.d1da8fc.js""></script>

        <script type=""text/javascript"">
            // <![CDATA[
                cjs.Api.loader.get('cjs').call(function(_cjs) {{
                    _cjs.bookmakerSettings = {{ bookmakersData: {{""default"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""default"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""406"",""project_id"":""13"",""geo_ip"":""default"",""name"":""Sportium.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""392"",""project_id"":""13"",""geo_ip"":""default"",""name"":""bwin.es"",""premium_status_id"":""2""}}],""UY"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""UY"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""417"",""project_id"":""13"",""geo_ip"":""UY"",""name"":""1xBet"",""premium_status_id"":""1""}}],""US"":[],""ES"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""406"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""Sportium.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""883"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""Winamax.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1003"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""1xBet.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""991"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""Versus.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""26"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""Betway"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""392"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""bwin.es"",""premium_status_id"":""2""}}],""FR"":[{{""main_bookmaker_id"":""141"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""Betclic.fr"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""264"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""Winamax"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""160"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""Unibet.fr"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""484"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""ParionsSport"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""905"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""Betsson.fr"",""premium_status_id"":""1""}}],""EC"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""EC"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""661"",""project_id"":""13"",""geo_ip"":""EC"",""name"":""Betano.ec"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""417"",""project_id"":""13"",""geo_ip"":""EC"",""name"":""1xBet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""597"",""project_id"":""13"",""geo_ip"":""EC"",""name"":""Latribet"",""premium_status_id"":""2""}}],""GR"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""GR"",""name"":""bet365"",""premium_status_id"":""2""}}],""CZ"":[{{""main_bookmaker_id"":""49"",""project_id"":""13"",""geo_ip"":""CZ"",""name"":""Tipsport.cz"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""657"",""project_id"":""13"",""geo_ip"":""CZ"",""name"":""Betano.cz"",""premium_status_id"":""1""}}],""US:USDC"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USDC"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USDC"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USIA"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USIA"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USIA"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USIA"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USIL"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USIL"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USIL"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USIL"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USKS"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USKS"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USKS"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USKS"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USKY"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USKY"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USKY"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USKY"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USMS"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USMS"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USNC"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USNC"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USNC"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USNC"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USNJ"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USNJ"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USNJ"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USNJ"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USNV"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USNV"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USNY"":[{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USNY"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USNY"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USOH"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USOH"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USOH"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USOH"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USTN"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USTN"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USTN"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USTN"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USVT"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USVT"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USVT"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USWY"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USWY"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USWY"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""DO"":[{{""main_bookmaker_id"":""1137"",""project_id"":""13"",""geo_ip"":""DO"",""name"":""Orobet"",""premium_status_id"":""1""}}],""US:USIN"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USIN"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USIN"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USIN"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USVA"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USVA"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USVA"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USVA"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USWV"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USWV"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USWV"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USMO"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USMO"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USMO"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USMO"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""BR"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""574"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Betano.br"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""833"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Estrelabet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""933"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Superbet.br"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""417"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""1xBet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""863"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""BetEsporte"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""959"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Esportivabet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""995"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Betnacional"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""999"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""BR4Bet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""943"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Betboom.br"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1069"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Bet7k"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1161"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""BrasilBet"",""premium_status_id"":""1""}}],""US:USMD"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USMD"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USMD"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USMD"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""HR"":[],""TR"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""TR"",""name"":""bet365"",""premium_status_id"":""2""}}]}} }};
                }});
                //used in LiveTableStaticLeagues
                var pageType = ""sport_page"", sportId = 1;
		                    var dataLayer = dataLayer || [];

		function otAfterCallback() {{
		    window.setTimeout(() => {{
		        try {{
		            if (!window.hasOTAfterCallbackProceeded) {{
		                dataLayer.push({{event:'gdpr_consent',user_consent:'agree'}});
		                document.dispatchEvent(new Event('onetrust'));
		            }}
		        }} catch(e) {{
		            console.error(e);
		        }}
		        window.hasOTAfterCallbackProceeded = true;
		    }}, 0);
		}};

		function otCallback() {{
		    document.dispatchEvent(new Event(""oneTrustLoaded""));
		    window.oneTrustLoaded = true;

		    if (typeof cjs === 'undefined') {{
		        return;
		    }}

		    if (!window.hasOTCallbackProceeded) {{
		        cjs.Api.loader.get('cjs').call(function(_cjs) {{
		            _cjs.Api.loader.get('onetrust').fulfill(cb => {{ cb() }});
		        }});
		    }}

		    window.hasOTCallbackProceeded = true;
		}};

		function OptanonWrapper() {{
		    dataLayer.push({{event:""OneTrustGroupsUpdated""}});
		    try {{
		        if (typeof __tcfapi === ""function"") {{
		            __tcfapi(""getTCData"",2,(e) => {{
		                if (e !== null) {{
		                    otCallback();
		                    if (e.eventStatus === 'useractioncomplete' || e.eventStatus === 'tcloaded') {{
		                        otAfterCallback();
		                    }};
		                }};
		            }});
		        }} else if (typeof OneTrust === ""object"") {{
		            otCallback();
		            if (!OneTrust.IsAlertBoxClosed()) {{
		                OneTrust.OnConsentChanged(() => otAfterCallback());
		            }}
		        }} else {{
		            otCallback();
		        }}
		    }} catch(e) {{
		        console.error(e);
		    }}
		}};
            // ]]>
        </script>
    </head>
<body class=""responsive background-add-off brand--flashscore soccer _fs flat pid_13 mgc hasFsNews  twoLineLayout isSportPage fcp-skeleton  light-bg-1 v3 bg3 seoTopWrapperHidden"" data-analytics-page-type=""sport_page"">
<div class=""otPlaceholder otPlaceholder--hidden"">
    <div class=""skOT skOT--ot"">
        <div class=""skOT__co"">
            <div class=""skOT__ti""></div>
            <div class=""skOT__te""></div>
            <div class=""skOT__te""></div>
            <div class=""skOT__te""></div>
            <div class=""skOT__te""></div>
        </div>
        <div class=""skOT__co"">
            <div class=""skOT__ti""></div>
            <div class=""skOT__te""></div>
            <div class=""skOT__te""></div>
            <div class=""skOT__te""></div>
        </div>
    </div>
</div>
<script type=""text/javascript"">
    if(!document.cookie.match(/^(.*;)?\s*OptanonAlertBoxClosed\s*=\s*[^;]+(.*)?$/) && !window.localStorage.getItem(""onetrust_placeholder"")){{
        document.getElementsByClassName(""otPlaceholder"")[0].classList.remove(""otPlaceholder--hidden"");
    }}

    document.addEventListener(""click"", function (e) {{

        var element = e.target.parentNode;
    if(element === document) element = document.body;

        if (element !== null && (element.classList.contains(""ot-button-group"") ||
                (element.classList.contains(""ot-btn-subcntr"")) ||
                (element.classList.contains(""ot-btn-container"")) ||
                (element.id === ""onetrust-button-group"") ||
                (element.id === ""onetrust-close-btn-container"") ||
                (element.id === ""ot-pc-content"") ||
                (e.target.closest("".otPlaceholder"")))

        )  {{
            window.localStorage.setItem(""onetrust_placeholder"", 1);
            document.getElementsByClassName(""otPlaceholder"")[0].classList.add(""otPlaceholder--hidden"");
        }}
    }}, false);

</script>
<script type=""text/javascript"">
    const defaultTheme = """";
    const usersTheme = window.localStorage.getItem(""theme"");
    if(!usersTheme && defaultTheme) {{
        cjs.Api.darkModeLocal.setDarkThemeAsDefault();
    }}

    cjs.Api.darkModeLocal.setThemeClass()
</script>

<div id=""zoneContainer-background"" data-zone-group=""background""></div>
<div class=""seoAdWrapper""><div class=""seoTop"">
    <div class=""seoTop__content"">
        <h1>LaLiga EA Sports 2026, LaLiga Hypermotion 2026, resultados de fútbol en directo, partidos de fútbol en vivo, online</h1>
    </div>
</div>

<script>
    cjs.Api.localLsid.beforeLoad((isLoggedIn) => {{
        if (isLoggedIn) {{
            const seoTopElement = document.getElementsByClassName('seoTop')[0];
            seoTopElement.style.display = 'none';
            seoTopElement.classList.add('seoTopHidden');
            document.body.classList.add('isLoggedIn');
            document.body.classList.remove('seoTopWrapperHidden');
        }}
    }});
</script>

<div id=""zoneContainer-top"" data-zone-group=""top""></div>

<div id=""zoneContainer-responsive_fixed_bottom"" data-zone-group=""responsive_fixed_bottom""></div>
</div>
<header class=""header"">
    <img src=""https://static.flashscore.com/res/_fs/image/2_others/bg.png"" alt="""" fetchpriority=""high"" class=""header__bg"">
    <div class=""header__content"">
        <a class=""header__logoWrapper"" href=""/"">
            <svg class=""header__logo"" preserveAspectRatio=""xMinYMid meet"" enable-background=""new 0 0 615 100"" height=""100"" viewBox=""0 0 615 100"" width=""615"" xmlns=""http://www.w3.org/2000/svg""><g clip-rule=""evenodd"" fill-rule=""evenodd""><g fill=""#fff""><path d=""m180.8 24.9h-29.3c-.9 0-1.8.4-2.4 1l-6.6 6.6c-.6.6-1 1.5-1 2.4v39.6c0 .2.2.3.3.3h7.9c.2 0 .3-.2.3-.3v-18.6c0-1 .8-1.7 1.7-1.7h25.5c.2 0 .3-.2.3-.3v-7.9c0-.2-.2-.3-.3-.3h-25.5c-1 0-1.7-.8-1.7-1.7v-8.6c0-1 .8-1.7 1.7-1.7h29c.2 0 .3-.2.3-.3v-7.9c.1-.5 0-.6-.2-.6""/><path d=""m264.4 47.3c0 1-.8 1.7-1.7 1.7h-22.4c-1 0-1.7-.8-1.7-1.7v-12.1c0-1 .8-1.7 1.7-1.7h22.4c1 0 1.7.8 1.7 1.7zm7.6-14.8-6.6-6.6c-.6-.6-1.5-1-2.4-1h-23c-.9 0-1.8.4-2.4 1l-6.6 6.6c-.6.6-1 1.5-1 2.4v39.6c0 .2.2.3.3.3h7.9c.2 0 .3-.2.3-.3v-15.2c0-1 .8-1.7 1.7-1.7h22.4c1 0 1.7.8 1.7 1.7v15.2c0 .2.2.3.3.3h7.9c.2 0 .3-.2.3-.3v-39.6c.2-.9-.2-1.8-.8-2.4z""/><path d=""m222.4 74.8h-24.1c-.9 0-1.8-.4-2.4-1l-6.6-6.6c-.6-.6-1-1.5-1-2.4v-39.6c0-.2.2-.3.3-.3h7.9c.2 0 .3.2.3.3v39.3c0 1 .8 1.7 1.7 1.7h23.8c.2 0 .3.2.3.3v7.9c.1.3 0 .4-.2.4""/><path d=""m319.8 53.1-6.6-6.6c-.6-.6-1.5-1-2.4-1h-19.2c-1 0-1.7-.8-1.7-1.7v-8.6c0-1 .8-1.7 1.7-1.7h27.2c.2 0 .3-.2.3-.3v-7.9c0-.2-.2-.3-.3-.3h-27.5c-.9 0-1.8.4-2.4 1l-6.6 6.6c-.6.6-1 1.5-1 2.4v9.2c0 .9.4 1.8 1 2.4l6.6 6.6c.6.6 1.5 1 2.4 1h19.2c1 0 1.7.8 1.7 1.7v8.6c0 1-.8 1.7-1.7 1.7h-27.2c-.2 0-.3.2-.3.3v7.9c0 .2.2.3.3.3h27.5c.9 0 1.8-.4 2.4-1l6.6-6.6c.6-.6 1-1.5 1-2.4v-9.2c0-.8-.3-1.7-1-2.4""/><path d=""m419 53.1-6.6-6.6c-.6-.6-1.5-1-2.4-1h-19.2c-.9 0-1.7-.8-1.7-1.7v-8.6c0-1 .8-1.7 1.7-1.7h27.2c.2 0 .3-.2.3-.3v-7.9c0-.2-.2-.3-.3-.3h-27.5c-.9 0-1.8.4-2.4 1l-6.6 6.6c-.6.6-1 1.5-1 2.4v9.2c0 .9.4 1.8 1 2.4l6.6 6.6c.6.6 1.5 1 2.4 1h19.2c1 0 1.7.8 1.7 1.7v8.6c0 1-.8 1.7-1.7 1.7h-27.2c-.2 0-.3.2-.3.3v7.9c0 .2.2.3.3.3h27.5c.9 0 1.8-.4 2.4-1l6.6-6.6c.6-.6 1-1.5 1-2.4v-9.2c0-.8-.4-1.7-1-2.4""/><path d=""m436.8 35.2c0-1 .8-1.7 1.7-1.7h25.5c.2 0 .3-.2.3-.3v-7.9c0-.2-.2-.3-.3-.3h-25.8c-.9 0-1.8.4-2.4 1l-6.6 6.6c-.6.6-1 1.5-1 2.4v29.9c0 .9.4 1.8 1 2.4l6.6 6.6c.6.6 1.5 1 2.4 1h25.8c.2 0 .3-.2.3-.3v-7.9c0-.2-.2-.3-.3-.3h-25.5c-1 0-1.7-.8-1.7-1.7z""/><path d=""m507.1 64.5c0 1-.8 1.7-1.7 1.7h-22.4c-1 0-1.7-.8-1.7-1.7v-29.3c0-1 .8-1.7 1.7-1.7h22.4c1 0 1.7.8 1.7 1.7zm7.6-32-6.6-6.6c-.6-.6-1.5-1-2.4-1h-23c-.9 0-1.8.4-2.4 1l-6.6 6.6c-.6.6-1 1.5-1 2.4v29.9c0 .9.4 1.8 1 2.4l6.6 6.6c.6.6 1.5 1 2.4 1h23c.9 0 1.8-.4 2.4-1l6.6-6.6c.6-.6 1-1.5 1-2.4v-29.9c0-.9-.3-1.8-1-2.4z""/><path d=""m371.8 24.9h-7.9c-.2 0-.3.2-.3.3v18.6c0 1-.8 1.7-1.7 1.7h-22.4c-1 0-1.7-.8-1.7-1.7v-18.6c0-.2-.2-.3-.3-.3h-7.9c-.2 0-.3.2-.3.3v49.3c0 .2.2.3.3.3h7.9c.2 0 .3-.2.3-.3v-18.6c0-1 .8-1.7 1.7-1.7h22.4c1 0 1.7.8 1.7 1.7v18.6c0 .2.2.3.3.3h7.9c.2 0 .3-.2.3-.3v-49.3c0-.2-.1-.3-.3-.3""/><path d=""m558.4 43.8c0 1-.8 1.7-1.7 1.7h-22.4c-.9 0-1.7-.8-1.7-1.7v-8.6c0-.9.8-1.7 1.7-1.7h22.4c1 0 1.7.8 1.7 1.7zm8.6-8.9c0-.9-.4-1.8-1-2.4l-6.5-6.6c-.6-.6-1.5-1-2.4-1h-32.8c-.2 0-.3.2-.3.3v49.3c0 .2.2.3.3.3h7.9c.2 0 .3-.2.3-.3v-18.6c0-.9.7-1.6 1.6-1.7h11.1l11.9 20.7h9.9l-11.9-20.7h1.9c.9 0 1.8-.4 2.4-1l6.5-6.6c.6-.6 1-1.5 1-2.4v-9.3z""/><path d=""m585.7 33.5h28.9c.2 0 .3-.2.3-.3v-7.9c0-.2-.2-.3-.3-.3h-29.2c-.9 0-1.8.4-2.4 1l-6.6 6.6c-.6.6-1 1.5-1 2.4v29.9c0 .9.4 1.8 1 2.4l6.6 6.6c.6.6 1.5 1 2.4 1h29.2c.2 0 .3-.2.3-.3v-7.9c0-.2-.2-.3-.3-.3h-28.9c-1 0-1.7-.8-1.7-1.7v-8.6c0-1 .8-1.7 1.7-1.7h20.3c.2 0 .3-.2.3-.3v-7.9c0-.2-.2-.3-.3-.3h-20.3c-1 0-1.7-.8-1.7-1.7v-8.6c0-1.4.7-2.1 1.7-2.1""/><path d=""m21.1 55.1c-.5-2.6-.6-5.1-.3-7.6l-20.6-1.9c-.4 4.3-.2 8.6.6 13s2.1 8.6 3.9 12.5l18.7-8.7c-1-2.3-1.8-4.7-2.3-7.3""/><path d=""m27.6 68.8-15.9 13.3c4.7 5.6 10.6 10.1 17.2 13.2l8.7-18.7c-3.8-1.9-7.3-4.5-10-7.8""/><path d=""m55.1 78.9c-2.6.5-5.2.6-7.6.3l-1.8 20.6c4.3.4 8.6.2 13-.6 1.4-.3 2.9-.6 4.3-.9l-5.4-20c-.8.2-1.7.4-2.5.6""/><path d=""m44.9 21.1c3.5-.6 7.1-.6 10.4 0l8.9-19.1c-7.2-2.1-15-2.7-22.9-1.3-19.7 3.5-34.7 18.2-39.6 36.4l20 5.4c2.9-10.7 11.6-19.3 23.2-21.4""/><path d=""m68.8 72.5 13.3 15.8c3.3-2.8 6.3-6.1 8.8-9.6l-16.9-11.9c-1.5 2.1-3.2 4-5.2 5.7""/><path d=""m99.8 45.6-20.6 1.8c.2 1.7.2 3.4 0 5.1l20.6 1.8c.3-2.8.3-5.7 0-8.7""/></g><path d=""m73.3 0-19.2 41.3 83.1-41.3z"" fill=""#ff0046""/></g></svg>
        </a>
            <div class=""header__items"">
                <a href=""/"" class=""header__item--active header__item"">
                    <svg class=""header__itemIcon"">
                        <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#live-table""></use>
                    </svg>
                    <div class=""header__itemText"" data-tag="""">
                        Resultados
                    </div>
                </a>
                <a href=""/noticias/"" class=""header__item"">
                    <svg class=""header__itemIcon"">
                        <use xlink:href=""/res/_fs/image/13_symbols/bottom-nav.svg#news""></use>
                    </svg>
                    <div class=""header__itemText"">
                        Noticias
                    </div>
                </a>
            </div>
            <a id=""bonus-comparison-gift"" href=""#"" class=""header__block header__block--gift"">
                <div class=""header__button header__button"">
                    <svg class=""header__icon header__icon"">
                        <use xlink:href=""/res/_fs/image/13_symbols/action.svg?serial=1743#gift""></use>
                    </svg>
                </div>
            </a>
            <script>
                cjs.Api.loader.get(""geoIpResolver"").call((geoIp) => {{
                    const geoUrls = {{""CO"":""https://www.flashscore.co/apuestas/apuestas-ofertas-bonos/"",""ES"":""/apuestas/bonos-apuestas/""}};
                    if (geoUrls[geoIp]) {{
                        const link = document.getElementById(""bonus-comparison-gift"");
                        link.setAttribute(""href"", geoUrls[geoIp]);
                        link.classList.add(""isVisible"");
                    }}
                }});
            </script>
            <div id=""searchWindow"" class=""header__block header__block--search"">
                <div role=""button"" class=""header__button"">
                    <svg class=""header__icon header__icon--search"">
                        <use xlink:href=""/res/_fs/image/13_symbols/action.svg?serial=1743#search""></use>
                    </svg>
                </div>
            </div>
            <div id=""header__block--user-menu"" class=""header__block header__block--user"">
                <script>
                    cjs.Api.localLsid.beforeLoad((isLoggedIn, name) => {{
                        document.write('' +
                                '<div id=""user-menu"" role=""button"" class=""header__button header__button--user"">' +
                                '<svg class=""header__icon header__icon--user"">' +
                                '<use xlink:href=""' + (""/res/_fs/image/13_symbols/action.svg?serial=1743#user-logged-in"") + '""/>' +
                                '</svg>' +
                                (isLoggedIn
                                        ? '<span class=""header__text header__text--user header__text--loggedIn"">' + name + '</span>'
                                        : '<span class=""header__text header__text--user"">Conectar</span>') +
                                '</div>');
                    }});
                </script>
            </div>
        <div id=""hamburger-menu""  class=""header__block header__block--menu"">
            <div role=""button"" class=""header__button"">
                <svg class=""header__icon header__icon--menu"">
                    <use xlink:href=""/res/_fs/image/13_symbols/action.svg?serial=1743#menu""></use>
                </svg>
            </div>
        </div>
            <script defer type=""text/javascript"" src=""/res/_fs/build/autotrack.ff73da9.js""></script>
        <script defer type=""text/javascript"" src=""/res/_fs/build/loginClient.33e86c1.js""></script>
        <script defer type=""text/javascript"" src=""/res/_fs/build/headerMenu.3e5702a.js""></script>
        <script defer type=""text/javascript"" src=""/res/_fs/build/bonusComparisonGift.9f4e902.js""></script>
        <script type=""text/javascript"">
            window.headerMenuEnvironment = {{""lsidEnabled"":true,""langBoxEnabled"":false,""displayLangFlagInsteadIcon"":false,""langBoxData"":[],""langBoxDataByGeoIps"":{{""US"":{{""shortName"":""en-usa"",""fullName"":""English"",""localLangName"":""United States"",""list"":[]}},""BR"":{{""shortName"":""pt-br"",""fullName"":""Português (Brasil)"",""localLangName"":""Português (Brasil)"",""list"":[]}},""FR"":{{""shortName"":""fr"",""fullName"":""Français"",""localLangName"":""France"",""list"":[]}},""IN"":{{""shortName"":""en-india"",""fullName"":""English"",""localLangName"":""India"",""list"":[{{""id"":261,""projectId"":13,""shortName"":""hi"",""url"":""\/hi\/"",""localLang"":""हिंदी"",""localLangName"":""Hindi"",""onclickUrl"":""hi""}},{{""id"":262,""projectId"":13,""shortName"":""bn"",""url"":""\/bn\/"",""localLang"":""বাংলা"",""localLangName"":""Bengali"",""onclickUrl"":""bn""}},{{""id"":265,""projectId"":13,""shortName"":""te"",""url"":""\/te\/"",""localLang"":""తెలుగు"",""localLangName"":""Telugu"",""onclickUrl"":""te""}},{{""id"":263,""projectId"":13,""shortName"":""ta"",""url"":""\/ta\/"",""localLang"":""தமிழ்"",""localLangName"":""Tamil"",""onclickUrl"":""ta""}},{{""id"":264,""projectId"":13,""shortName"":""kn"",""url"":""\/kn\/"",""localLang"":""ಕನ್ನಡ"",""localLangName"":""Kannada"",""onclickUrl"":""kn""}}]}},""PL"":{{""shortName"":""pl"",""fullName"":""Polski"",""localLangName"":""Polska"",""list"":[]}}}}}};
            window.isFlashfootball = false;
            window.isDetail = false;
            window.mobileBannerConfig = null;
        </script>
    </div>
</header>
    <script type=""text/javascript"">
        cjs.defaultTopLeagues = [""6_100_SW9D1eZo"",""6_128_Mg9H0Flh"",""6_200_zcDLaZ3b"",""6_8_0UPxbDYA"",""6_66_KpY5LErp"",""6_106_boA2KUSu"",""6_205_rFyapk4H"",""6_8_pUAv7KCe"",""1_198_dYlOSQOD"",""1_6_xGrwqq16"",""1_6_KQMVOQ0g"",""1_6_ClDjv3V5"",""1_77_KIShoMk3"",""1_81_W6BOzpK2"",""1_98_COuk57Ci"",""1_139_Or1bBrWD"",""1_176_lnb8EJRp"",""1_176_QVmLl54o"",""1_176_vZiPmPJi"",""1_176_YTYRo1YM"",""1_8_lvUBR5F8"",""1_6_A9yxE9Ke"",""1_6_GfRbsVWM"",""2_9011_tItR6sEf"",""2_9011_nZi4fKds"",""2_9011_65k5lHxU"",""2_9012_Sd2Q088D"",""2_9012_hl1W8RZs"",""2_9012_6g0xhggi"",""2_9011_MP4jLdJh"",""2_9012_0G3fKGYb"",""3_6_naL1J006"",""3_6_fT0n14Vt"",""3_6_YJaj0Opm"",""3_77_nD0vn2bU"",""3_81_ncAkL5qn"",""3_83_xn32I3T4"",""3_98_h2HoKRSi"",""3_176_0fiHAulF"",""3_176_Q5poAJIR"",""3_191_MLmY2yB1"",""3_200_IBmris38"",""3_8_OQpzcCnS"",""3_176_IsRBNS56"",""3_6_nVvz91uS"",""4_62_QR1GYbvD"",""4_6_Cnt5FMOg"",""4_76_CnmCUGyG"",""4_81_nVp0wiqd"",""4_181_ObxFt3lm"",""4_200_G2Op923t"",""4_8_C06aJvIB"",""4_8_Q3A3IbXH"",""4_8_SCGVmKHb"",""4_176_j7wm55rf"",""4_6_63di6Zed"",""5_47_MZFZnvX4"",""5_200_rJVAIaHo"",""5_6_ClosTMJi"",""7_6_KK4FaFV3"",""7_6_nNlLsRUr"",""7_77_rBi9iqU7"",""7_81_Mmsc26yL"",""7_176_nVpEwOrl"",""7_8_zkpajjvm"",""19_24_ETdxjU8a"",""19_198_QRQyQVpP"",""19_8_EHbj07Ys"",""19_8_rNL5LJER"",""8_198_za7D2lO5"",""8_6_G8FL0ShI"",""8_6_faEPan8O"",""8_77_SzD3Lkgt"",""8_8_Stv0V7h5"",""8_8_nmjJVR7B"",""8_176_S8Iks1Vd"",""8_8_SExTbVeC"",""9_76_WxHfCw7j"",""9_181_UJRjmLT9"",""9_8_CrHenuqG"",""9_8_hbCfpabM"",""9_182_Ywy81Djb"",""10_76_nLBbqJDS"",""10_181_jacSiHjd"",""10_8_8K9IG0Td"",""12_6_6ecm9Xlr"",""12_6_CvPuKVY0"",""12_98_nm8RF0ON"",""12_154_jNqF318i"",""12_176_EVqSBe2f"",""12_176_hMrWAFH0"",""12_8_hjY9yg16"",""12_176_OM2S8m83"",""12_176_zm3MSezs"",""12_176_QqQxhNGO"",""12_8_Sp51ptwk"",""11_6_MFZy7Eom"",""11_6_tMoe7Y0g"",""11_176_joO7tfhP"",""11_39_CUZB0X54"",""11_8_UwAwNo2E"",""11_176_l8vAWx7K"",""11_3_rZBAZLMT"",""11_3_Kr4UBrQ3"",""14_6_2RABlYFn"",""14_8_jXzWoWa5"",""14_8_KGO4pUqO"",""14_8_0SwtclaU"",""14_8_U7TfIXUu"",""14_197_8bSbHipn"",""14_8_hGLC5Bah"",""14_8_W6KG4VEb"",""14_8_hxHR9kGl"",""14_8_byRjyCJO"",""15_8_GS36K259"",""15_197_MRDsXMKF"",""15_8_42FbPIs2"",""15_8_Mmkx9baa"",""13_8_xjQ9xGBl"",""13_8_b5EIzft1"",""13_8_OG7nzYAD"",""13_8_AkPEBy3K"",""13_8_2i0B6Zul"",""13_93_KfDQ6H86"",""13_8_KhWRqihE"",""17_8064_pSDwFmA2"",""17_8065_YwouxX6p"",""18_24_OICsE7P8"",""18_24_lnHbQKrJ"",""18_24_A9VciAso"",""18_24_GYMw4gKo"",""24_176_zXrc8SIB"",""24_8_ttMTnaKq"",""24_8_z3LXoJZk"",""24_8_vXupZVde"",""24_8_z3VAZkC1"",""24_8_8xWQf8rq"",""24_8_nTUUgSck"",""26_8_ruJ9pBzd"",""25_9995_EJ1XGOEs"",""25_9996_Oj29TrUm"",""22_8_f7ITstK5"",""22_6_CtMYh31I"",""23_8150_v5mY2VHL"",""23_8150_0WT9Phuh"",""23_8150_nqOdP4Wh"",""23_8150_CrmQoWqj"",""23_8150_WQvE7HHH"",""23_8150_buZKLqDG"",""23_8150_4K0lj5hO"",""23_8150_2N8xUvQK"",""23_8150_YVEWtJhI"",""30_76_xKNhAJXb"",""30_76_viM3lKQ8"",""30_76_p6fbtlPC"",""30_8_b3e31ohC"",""34_7300_EcSVXVwf"",""34_7300_lptFeFBL"",""34_7300_ABz7kU4b"",""35_197_biXWRQSN"",""35_197_j3ZUJ1y7"",""35_197_lptXr60I"",""35_197_KbeZZGu8"",""35_197_vmEZ5XXJ"",""36_7402_8CN3d6SA"",""36_7404_zF9M0iH9"",""42_93_2mjPD8xq"",""42_5_tfrUHIzn"",""42_5_WK02yCWs"",""42_8_zmOsQ2kA""];
    </script>
<nav class=""menuTop menuTop--soccer"">
    <div class=""menuTop__content menuTop__group"">
        <a href=""/favoritos/"" class=""menuTop__item menuTop__myfs"">
            <svg class=""menuTop__icon menuTop__icon--star"">
                <use xlink:href=""/res/_fs/image/13_symbols/action.svg?serial=1743#star""></use>
            </svg>
            <div class=""menuTop__text"">Favoritos</div>
        </a>
        <div class=""menuTop__items"">
            <a href=""/"" class=""menuTop__item--active menuTop__item""
               data-sport-id=""1"">
                <svg class=""menuTop__icon"">
                    <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#soccer""></use>
                </svg>
                <div class=""menuTop__text"">Fútbol</div>
            </a>
            <a href=""/tenis/"" class=""menuTop__item""
               data-sport-id=""2"">
                <svg class=""menuTop__icon"">
                    <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#tennis""></use>
                </svg>
                <div class=""menuTop__text"">Tenis</div>
            </a>
            <a href=""/baloncesto/"" class=""menuTop__item""
               data-sport-id=""3"">
                <svg class=""menuTop__icon"">
                    <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#basketball""></use>
                </svg>
                <div class=""menuTop__text"">Baloncesto</div>
            </a>
            <a href=""/golf/"" class=""menuTop__item""
               data-sport-id=""23"">
                <svg class=""menuTop__icon"">
                    <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#golf""></use>
                </svg>
                <div class=""menuTop__text"">Golf</div>
            </a>
            <a href=""/hockey/"" class=""menuTop__item""
               data-sport-id=""4"">
                <svg class=""menuTop__icon"">
                    <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#hockey""></use>
                </svg>
                <div class=""menuTop__text"">Hockey</div>
            </a>
            <a href=""/beisbol/"" class=""menuTop__item""
               data-sport-id=""6"">
                <svg class=""menuTop__icon"">
                    <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#baseball""></use>
                </svg>
                <div class=""menuTop__text"">Béisbol</div>
            </a>
            <a href=""/snooker/"" class=""menuTop__item""
               data-sport-id=""15"">
                <svg class=""menuTop__icon"">
                    <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#snooker""></use>
                </svg>
                <div class=""menuTop__text"">Snooker</div>
            </a>
            <a href=""/balonmano/"" class=""menuTop__item""
               data-sport-id=""7"">
                <svg class=""menuTop__icon"">
                    <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#handball""></use>
                </svg>
                <div class=""menuTop__text"">Balonmano</div>
            </a>
        </div>
<div class=""menuMinority"">
    <div class=""menuMinority__title"" onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});"">
        <svg class=""menuMinority__arrow"">
            <use xlink:href=""/res/_fs/image/13_symbols/action.svg?serial=1743#dropdown""></use>
        </svg>
        <div class=""menuMinority__text""
             data-text-long=""Más deportes"">
        </div>
    </div>
    <div class=""menuMinority__content"" data-mobile-headline=""Más deportes"">
        <a href=""/badminton/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""21"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#badminton""></use>
            </svg>
            <div class=""menuMinority__text"" >Bádminton</div>
        </a>
        <a href=""/baloncesto/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""3"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#basketball""></use>
            </svg>
            <div class=""menuMinority__text"" >Baloncesto</div>
        </a>
        <a href=""/balonmano/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""7"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#handball""></use>
            </svg>
            <div class=""menuMinority__text"" >Balonmano</div>
        </a>
        <a href=""/bandy/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""10"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#bandy""></use>
            </svg>
            <div class=""menuMinority__text"" >Bandy</div>
        </a>
        <a href=""/beisbol/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""6"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#baseball""></use>
            </svg>
            <div class=""menuMinority__text"" >Béisbol</div>
        </a>
        <a href=""/boxeo/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""16"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#boxing""></use>
            </svg>
            <div class=""menuMinority__text"" >Boxeo</div>
        </a>
        <a href=""/carreras-de-caballos/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""35"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#horse-racing""></use>
            </svg>
            <div class=""menuMinority__text"" >Caballos</div>
        </a>
        <a href=""/ciclismo/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""34"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#cycling""></use>
            </svg>
            <div class=""menuMinority__text"" >Ciclismo</div>
        </a>
        <a href=""/cricket/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""13"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#cricket""></use>
            </svg>
            <div class=""menuMinority__text"" >Cricket</div>
        </a>
        <a href=""/dardos/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""14"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#darts""></use>
            </svg>
            <div class=""menuMinority__text"" >Dardos</div>
        </a>
        <a href=""/deportes-de-invierno/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""37"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#winter-sports""></use>
            </svg>
            <div class=""menuMinority__text"" >Deportes invierno</div>
        </a>
        <a href=""/esports/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""36"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#esports""></use>
            </svg>
            <div class=""menuMinority__text"" >eSports</div>
        </a>
        <a href=""/futbol/"" class=""menuMinority__item--active menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""1"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#soccer""></use>
            </svg>
            <div class=""menuMinority__text"" >Fútbol</div>
        </a>
        <a href=""/futbol-americano/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""5"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#american-football""></use>
            </svg>
            <div class=""menuMinority__text"" >Fútbol Amer.</div>
        </a>
        <a href=""/futbol-australiano/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""18"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#aussie-rules""></use>
            </svg>
            <div class=""menuMinority__text"" >Fútbol Aus.</div>
        </a>
        <a href=""/futbol-playa/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""26"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#beach-soccer""></use>
            </svg>
            <div class=""menuMinority__text"" >Fútbol playa</div>
        </a>
        <a href=""/futbol-sala/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""11"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#futsal""></use>
            </svg>
            <div class=""menuMinority__text"" >Fútbol Sala</div>
        </a>
        <a href=""/golf/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""23"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#golf""></use>
            </svg>
            <div class=""menuMinority__text"" >Golf</div>
        </a>
        <a href=""/hockey/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""4"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#hockey""></use>
            </svg>
            <div class=""menuMinority__text"" >Hockey</div>
        </a>
        <a href=""/hockey-hierba/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""24"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#field-hockey""></use>
            </svg>
            <div class=""menuMinority__text"" >Hockey hierba</div>
        </a>
        <a href=""/kabaddi/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""42"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#kabaddi""></use>
            </svg>
            <div class=""menuMinority__text"" >Kabaddi</div>
        </a>
        <a href=""/mma/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""28"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#mma""></use>
            </svg>
            <div class=""menuMinority__text"" >MMA</div>
        </a>
        <a href=""/motor/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""31"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#motorsport""></use>
            </svg>
            <div class=""menuMinority__text"" >Motor</div>
        </a>
        <a href=""/netball/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""29"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#netball""></use>
            </svg>
            <div class=""menuMinority__text"" >Netball</div>
        </a>
        <a href=""/pesapallo/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""30"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#pesapallo""></use>
            </svg>
            <div class=""menuMinority__text"" >Pesäpallo</div>
        </a>
        <a href=""/rugby/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""8"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#rugby-union""></use>
            </svg>
            <div class=""menuMinority__text"" >Rugby</div>
        </a>
        <a href=""/rugby-league/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""19"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#rugby-league""></use>
            </svg>
            <div class=""menuMinority__text"" >Rugby League</div>
        </a>
        <a href=""/snooker/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""15"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#snooker""></use>
            </svg>
            <div class=""menuMinority__text"" >Snooker</div>
        </a>
        <a href=""/tenis/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""2"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#tennis""></use>
            </svg>
            <div class=""menuMinority__text"" >Tenis</div>
        </a>
        <a href=""/tenis-de-mesa/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""25"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#table-tennis""></use>
            </svg>
            <div class=""menuMinority__text"" >Tenis de mesa</div>
        </a>
        <a href=""/unihockey/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""9"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#floorball""></use>
            </svg>
            <div class=""menuMinority__text"" >Unihockey</div>
        </a>
        <a href=""/voleibol/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""12"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#volleyball""></use>
            </svg>
            <div class=""menuMinority__text"" >Voleibol</div>
        </a>
        <a href=""/voley-playa/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""17"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#beach-volleyball""></use>
            </svg>
            <div class=""menuMinority__text"" >Voley Playa</div>
        </a>
        <a href=""/waterpolo/"" class=""menuMinority__item""
           onclick=""cjs.Api.loader.get('sportMenu').call(function(sportMenu) {{ sportMenu.toggleMinority() }});""
           data-sport-id=""22"">
            <svg class=""menuMinority__icon"">
                <use xlink:href=""/res/_fs/image/13_symbols/sport.svg#water-polo""></use>
            </svg>
            <div class=""menuMinority__text"" >Waterpolo</div>
        </a>
    </div>
</div>
    </div>
</nav>
<div class=""container"">
<div class=""container__content content"">
<div class=""container__main"" id=""main"">
<div class=""container__mainInner"" id=""tc"">
<div class=""container__bannerZone"" id=""rc-top""><div id=""rccontent"">
<div id=""zoneContainer-right_top"" data-zone-group=""right_top""></div>
<div class=""scrolling-banner-wrap"">
<div id=""zoneContainer-right_zone_1"" data-zone-group=""right_zone_1""></div>

<div id=""zoneContainer-right_zone_2"" data-zone-group=""right_zone_2""></div>
</div><div></div></div></div><main class=""container__liveTableWrapper sport_page"" id=""mc"">
<div id=""box-over-content-revive"" class=""boxOverContentRevive"">
    <div class=""boxOverContentRevive__placeholder"">
        <span class=""boxOverContentRevive__placeholderText"">AD</span>
    </div>
    
<div id=""zoneContainer-box_over_content"" data-zone-group=""box_over_content""></div>

</div>
<script>
    (typeof window.initBoxOverContentIframe == 'function' || function() {{
        window.initBoxOverContentIframe = true
    }})();
</script>
<div id=""box-over-content-b"" class=""boxOverContent--b""><script type=""text/javascript"">cjs.Api.loader.get(""cjs"").call(function(_cjs) {{ _cjs.Api.loader.get(""boxContentManager"").call(function(boxContentManager) {{ boxContentManager.setSupportedGeoIPGroups([""default"",""UY"",""US"",""ES"",""FR"",""EC"",""GR"",""CZ"",""US:USDC"",""US:USIA"",""US:USIL"",""US:USKS"",""US:USKY"",""US:USMS"",""US:USNC"",""US:USNJ"",""US:USNV"",""US:USNY"",""US:USOH"",""US:USTN"",""US:USVT"",""US:USWY"",""US"",""DO"",""US:USIN"",""US:USVA"",""US:USWV"",""US:USMO"",""BR"",""US:USMD"",""HR"",""TR""]); _cjs.Api.boxOverContentHandler.showPlaceholders(); }}); }});</script></div><script>
            cjs.Api.boxOverContentHandler.showPlaceholders(true);
            cjs.Api.loader.get(""geoIpResolver"").call(function () {{
                if (!cjs.geoIP) {{
                    cjs.Api.boxOverContentHandler.clearPlaceholders();
                }}
            }});
          </script><div id=""notifications-alert-wrapper"" style=""display: none;""></div><div id=""legalAgeContainer"" class=""legalAgeConfirmation""></div><div class=""container__livetable""><div class=""container__heading""><div id=""fscon""></div>
<h2 class=""breadcrumb breadcrumb--hidden""><svg class=""breadcrumb__icon""><use xlink:href=""/res/_fs/image/13_symbols/sport.svg#soccer""/></svg><a class=""breadcrumb__link"" href=""/futbol/"">Fútbol</a></h2></div>
<div class=""container__fsbody"" id=""fsbody"">
<div id=""live-table"">
    <script>
        document.body.classList.toggle(""loading"", true);
    </script>
    <div class=""loadingOverlay"">
    <div class=""loadingAnimation"">
        <div class=""loadingAnimation__text"">Loading...</div>
    </div>
</div>
<div class=""sk"">
    <div class=""sk__bl"">
        <div class=""sk__w"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
            <div></div>
            <div></div>
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
        <div class=""sk__h""></div>
        <div class=""sk__r ska__chb"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
        <div class=""sk__r sk__r--a ska__chb"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
        <div class=""sk__r sk__r--a ska__chb"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
        <div class=""sk__h""></div>
        <div class=""sk__r ska__chb"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
        <div class=""sk__r sk__r--a ska__chb"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
    </div>
    <div class=""sk__bl sk__blnw"">
        <div class=""sk__nwh ska__ch"">
            <div></div>
            <div></div>
        </div>
        <div class=""sk__nws"">
            <div class=""sk__nwa"">
                <div class=""sk__nwi ska__di""></div>
                <div class=""sk__nwt ska__ch"">
                    <div></div>
                    <div></div>
                    <div></div>
                    <div></div>
                </div>
            </div>
            <div class=""sk__nwa"">
                <div class=""sk__nwi ska__di""></div>
                <div class=""sk__nwt ska__ch"">
                    <div></div>
                    <div></div>
                    <div></div>
                    <div></div>
                </div>
            </div>
            <div class=""sk__nwa"">
                <div class=""sk__nwi ska__di""></div>
                <div class=""sk__nwt ska__ch"">
                    <div></div>
                    <div></div>
                    <div></div>
                    <div></div>
                </div>
            </div>
        </div>
        <div class=""sk__nwf ska__ch"">
            <div></div>
        </div>
    </div>
    <div class=""sk__bl"">
        <div class=""sk__h""></div>
        <div class=""sk__r ska__chb"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
        <div class=""sk__r ska__chb"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
        <div class=""sk__r sk__r--a ska__chb"">
            <div></div>
            <div></div>
            <div></div>
            <div></div>
        </div>
        <div class=""sk__b"">
            <div class=""sk__h""></div>
            <div class=""sk__r ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r sk__r--a ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r sk__r--a ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r sk__r--a ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__h""></div>
            <div class=""sk__r ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r sk__r--a ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r sk__r--a ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
            <div class=""sk__r sk__r--a ska__chb"">
                <div></div>
                <div></div>
                <div></div>
                <div></div>
            </div>
        </div>
    </div>
</div>

<script>
    const fsNewsVisible = window.localStorage.getItem(""liveTableFsNewsVisible"");

    if(fsNewsVisible && fsNewsVisible === ""false""){{
        document.getElementsByClassName(""sk__nws"")[0].style.display = ""none"";
        document.getElementsByClassName(""sk__nwf"")[0].style.display = ""none"";
    }}
</script>

</div>
<script type=""text/javascript"">
    cjs.Api.loader.get('cjs').call(function(_cjs) {{
        country_id = 0;tournament_id = 0;series_id = 0;sentences = [];sentences_parts = [];default_tz = 1;matches = null;mpe_alias = ""p1tt2:100, p2tt2:100, p3tt2:100, p4tt2:100, p5tt2:100, p6tt2:100, p7tt2:100, p8tt2:100, p9tt2:100, p10tt2:100"";mpe_debug = false;mpe_delivery = ""p"";odds_enable = true;project_id = 13;prev_category = null;prev_date = null;push_fail_logging = false;sport = ""soccer"";tudate = 1770681600;stats_live_enable = 1;participant_id = 0;
        try {{
            matches = /^([^#]+)#(.*)\breload:([0-9]+)\-([0-9])(.*)$/.exec(parent.location.href);
        }} catch (e) {{}}

        if(matches)
        {{
            prev_date = matches[3];
            prev_category = matches[4];
            // cut out reload message from url bookmark
            parent.location.href = matches[1] + ""#"" +
                    (matches[2].substr(matches[2].length - 1) == "";"" ? matches[2].substr(0, matches[2].length - 1) : matches[2]) +
                    ((matches[5].substr(0, 1) == "";"" && !matches[2].length) ? matches[5].substr(1) : matches[5]);
        }}

        const utilPage = _cjs.dic.get(""util_page"");
        utilPage.setMixedFeed(false);
        utilPage.setParentSportId(0);
        utilPage.setPageType(""sport_page"");

        _cjs.fromGlobalScope.init({{
            sportId: 1,
            sport_name: ""soccer"",
            country_id: 0,
            tournament_id: 0,
            country_tournament_order_fin: true,
            prev_category: null,
            prev_date: null,
            startUpdater: true,
            participant_id: 0,
            seriesId: 0
        }});
        _cjs.pageTab = """";
        _cjs.allowedTvs = [12,13,14,15,16,17,18,19,21,48,62,74,75,91,92,103,104,148,207,213,214,371,372,373,374,375,376,377,378,456,470,481,482,500,502,550,583,584,655,656,660,701,702,703,725,747,778,780,805,835,953,954,1021,1161,1371,1619,1621,1623,1627,1633,1635,1637,1639,1789,1803,1807,1971,1973,1977,1979,1981,1983,1985,1987,1995,1997,1999,2001,2003,2005,2007,2009,2013,2015,2017,2021,2117,2119,2121,2123,2125,2127,2129,2131,2133,2135,2137,2139,2213,2389,2409,2413,2525,2527,2531,2533,2535,2537,2541,2543,2545,2759,2853,2963,2971,2973,3037,3039,3041,3043,3045,3047,3049,3051,3053,3055,3073,3075,3077,3079,3081,3083,3085,3087,3089,3091,3109,3139,3163,3165,3187,3191,3211,3243,3247,3249,3277,3321,3325,3333,3355,3581,3587,3593,3733,3741,3747,3763,3775,3789,3793,3799,3801,3815,3827,3977,3979,4023,4073,4075,4159,4203,4205,4233,4243,4327,4473,4487,4489,4563,4673,4937,4951,4953,4955,4985,4987,5051,5053,5069,5083,5091,5095,5097,5099,5105,5123,5157,5201,5203,5205,5211,5283,5301,5319,5321,5335,5403,5405,5417,5429,5455,5457,5469,5561,5583,5617,5935,6151,6165,6181,6195,6309,6311,6457,6463,6469,6471,6473,6475,6477,6479,6481,6601,6689,6791,6937,6953,7043,7107,7165,7317,7473,7609,7615,7693,7695,7751,7815,7891,7893,7925,7927,7929,7931,7933,7935,7937,7939,7941,7947,8213,8215,8219,8221,8395,8397,8399,8619,8787,8799,8909,9159,9297,9343,9345,9373,9521,9543,9597,9685,9705,9707,9715,9717,9721,9761,9773,9781,9783,9787,9805,9843,9871,9895];
        _cjs.bookmakerSettings = {{
            ""bookmakersData"": {{""default"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""default"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""406"",""project_id"":""13"",""geo_ip"":""default"",""name"":""Sportium.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""392"",""project_id"":""13"",""geo_ip"":""default"",""name"":""bwin.es"",""premium_status_id"":""2""}}],""UY"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""UY"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""417"",""project_id"":""13"",""geo_ip"":""UY"",""name"":""1xBet"",""premium_status_id"":""1""}}],""US"":[],""ES"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""406"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""Sportium.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""883"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""Winamax.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1003"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""1xBet.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""991"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""Versus.es"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""26"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""Betway"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""392"",""project_id"":""13"",""geo_ip"":""ES"",""name"":""bwin.es"",""premium_status_id"":""2""}}],""FR"":[{{""main_bookmaker_id"":""141"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""Betclic.fr"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""264"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""Winamax"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""160"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""Unibet.fr"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""484"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""ParionsSport"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""905"",""project_id"":""13"",""geo_ip"":""FR"",""name"":""Betsson.fr"",""premium_status_id"":""1""}}],""EC"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""EC"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""661"",""project_id"":""13"",""geo_ip"":""EC"",""name"":""Betano.ec"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""417"",""project_id"":""13"",""geo_ip"":""EC"",""name"":""1xBet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""597"",""project_id"":""13"",""geo_ip"":""EC"",""name"":""Latribet"",""premium_status_id"":""2""}}],""GR"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""GR"",""name"":""bet365"",""premium_status_id"":""2""}}],""CZ"":[{{""main_bookmaker_id"":""49"",""project_id"":""13"",""geo_ip"":""CZ"",""name"":""Tipsport.cz"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""657"",""project_id"":""13"",""geo_ip"":""CZ"",""name"":""Betano.cz"",""premium_status_id"":""1""}}],""US:USDC"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USDC"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USDC"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USIA"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USIA"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USIA"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USIA"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USIL"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USIL"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USIL"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USIL"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USKS"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USKS"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USKS"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USKS"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USKY"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USKY"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USKY"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USKY"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USMS"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USMS"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USNC"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USNC"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USNC"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USNC"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USNJ"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USNJ"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USNJ"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USNJ"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USNV"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USNV"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USNY"":[{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USNY"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USNY"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USOH"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USOH"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USOH"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USOH"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USTN"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USTN"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USTN"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USTN"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USVT"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USVT"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USVT"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USWY"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USWY"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USWY"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""DO"":[{{""main_bookmaker_id"":""1137"",""project_id"":""13"",""geo_ip"":""DO"",""name"":""Orobet"",""premium_status_id"":""1""}}],""US:USIN"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USIN"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USIN"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USIN"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USVA"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USVA"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USVA"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USVA"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""US:USWV"":[{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USWV"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USWV"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""US:USMO"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USMO"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USMO"",""name"":""BetMGM.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USMO"",""name"":""Fanduel"",""premium_status_id"":""1""}}],""BR"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""bet365"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""574"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Betano.br"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""833"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Estrelabet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""933"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Superbet.br"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""417"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""1xBet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""863"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""BetEsporte"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""959"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Esportivabet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""995"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Betnacional"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""999"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""BR4Bet"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""943"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Betboom.br"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1069"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""Bet7k"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1161"",""project_id"":""13"",""geo_ip"":""BR"",""name"":""BrasilBet"",""premium_status_id"":""1""}}],""US:USMD"":[{{""main_bookmaker_id"":""549"",""project_id"":""13"",""geo_ip"":""US:USMD"",""name"":""bet365.us"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""1133"",""project_id"":""13"",""geo_ip"":""US:USMD"",""name"":""Fanduel"",""premium_status_id"":""1""}},{{""main_bookmaker_id"":""851"",""project_id"":""13"",""geo_ip"":""US:USMD"",""name"":""BetMGM.us"",""premium_status_id"":""1""}}],""HR"":[],""TR"":[{{""main_bookmaker_id"":""16"",""project_id"":""13"",""geo_ip"":""TR"",""name"":""bet365"",""premium_status_id"":""2""}}]}},
            ""availableBookmakers"": {{""default"":[""16"",""406"",""392"",""26""],""UY"":[""16"",""417""],""US"":[],""ES"":[""16"",""406"",""883"",""1003"",""991"",""26"",""1121"",""392"",""526"",""1087""],""FR"":[""141"",""160"",""264"",""484"",""905"",""398"",""129""],""EC"":[""16"",""661"",""417"",""597""],""GR"":[""16""],""CZ"":[""49"",""657""],""US:USDC"":[""851"",""1133""],""US:USIA"":[""549"",""851"",""1133""],""US:USIL"":[""549"",""1133"",""851""],""US:USKS"":[""549"",""851"",""1133""],""US:USKY"":[""549"",""851"",""1133""],""US:USMS"":[""851""],""US:USNC"":[""549"",""851"",""1133""],""US:USNJ"":[""549"",""851"",""1133""],""US:USNV"":[""851""],""US:USNY"":[""1133"",""851""],""US:USOH"":[""549"",""851"",""1133""],""US:USTN"":[""549"",""851"",""1133""],""US:USVT"":[""851"",""1133""],""US:USWY"":[""851"",""1133""],""DO"":[""1137""],""US:USIN"":[""549"",""851"",""1133""],""US:USVA"":[""549"",""851"",""1133""],""US:USWV"":[""851"",""1133""],""US:USMO"":[""549"",""851"",""1133""],""BR"":[""16"",""574"",""833"",""933"",""417"",""863"",""959"",""995"",""999"",""943"",""1069"",""1161"",""1023"",""1063"",""429"",""953"",""935"",""955"",""973"",""1047"",""1079"",""1091"",""1049"",""1153"",""1163""],""US:USMD"":[""549"",""851"",""1133""],""HR"":[],""TR"":[]}},
            ""geoGroups"": {{""default"":{{""geo_ip"":""default"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""0"",""all_live_bookmakers_in_match_summary"":""0"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""UY"":{{""geo_ip"":""UY"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""0"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US"":{{""geo_ip"":""US"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""ES"":{{""geo_ip"":""ES"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""FR"":{{""geo_ip"":""FR"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""0"",""all_live_bookmakers_in_match_summary"":""0"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""EC"":{{""geo_ip"":""EC"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""GR"":{{""geo_ip"":""GR"",""clickable"":""0"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""0"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""0"",""all_live_bookmakers_in_match_summary"":""0"",""show_bookmaker_logo_in_summary"":""0"",""clickable_bookmaker_logo_in_summary"":""0"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""0"",""clickable_bookmaker_logo_odds_comparison"":""0""}},""CZ"":{{""geo_ip"":""CZ"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""0"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""0"",""all_live_bookmakers_in_match_summary"":""0"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USDC"":{{""geo_ip"":""US:USDC"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USIA"":{{""geo_ip"":""US:USIA"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USIL"":{{""geo_ip"":""US:USIL"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USKS"":{{""geo_ip"":""US:USKS"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USKY"":{{""geo_ip"":""US:USKY"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USMS"":{{""geo_ip"":""US:USMS"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USNC"":{{""geo_ip"":""US:USNC"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USNJ"":{{""geo_ip"":""US:USNJ"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USNV"":{{""geo_ip"":""US:USNV"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USNY"":{{""geo_ip"":""US:USNY"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USOH"":{{""geo_ip"":""US:USOH"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USTN"":{{""geo_ip"":""US:USTN"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USVT"":{{""geo_ip"":""US:USVT"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USWY"":{{""geo_ip"":""US:USWY"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""DO"":{{""geo_ip"":""DO"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USIN"":{{""geo_ip"":""US:USIN"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USVA"":{{""geo_ip"":""US:USVA"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USWV"":{{""geo_ip"":""US:USWV"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USMO"":{{""geo_ip"":""US:USMO"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""BR"":{{""geo_ip"":""BR"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""1"",""all_live_bookmakers_in_match_summary"":""1"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""US:USMD"":{{""geo_ip"":""US:USMD"",""clickable"":""1"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""1"",""all_prematch_bookmakers_in_match_summary"":""0"",""all_live_bookmakers_in_match_summary"":""0"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""HR"":{{""geo_ip"":""HR"",""clickable"":""0"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""0"",""all_live_bookmakers_in_match_summary"":""0"",""show_bookmaker_logo_in_summary"":""1"",""clickable_bookmaker_logo_in_summary"":""1"",""show_odds_comparison_tab"":""1"",""show_bookmaker_logo_odds_comparison"":""1"",""clickable_bookmaker_logo_odds_comparison"":""1""}},""TR"":{{""geo_ip"":""TR"",""clickable"":""0"",""logo_to_text_match_summary"":""0"",""logo_to_text_odds_comparison"":""0"",""logo_to_text_bonus"":""0"",""odds_background_in_odds_comparison"":""1"",""all_bookmakers_in_odds_comparison"":""0"",""all_prematch_bookmakers_in_match_summary"":""0"",""all_live_bookmakers_in_match_summary"":""0"",""show_bookmaker_logo_in_summary"":""0"",""clickable_bookmaker_logo_in_summary"":""0"",""show_odds_comparison_tab"":""0"",""show_bookmaker_logo_odds_comparison"":""0"",""clickable_bookmaker_logo_odds_comparison"":""0""}}}},
            ""logos"": {{""urls"":{{""15"":""https://static.flashscore.com/res/image/data/bookmakers/17-15.png"",""16"":""https://static.flashscore.com/res/image/data/bookmakers/17-16.png"",""392"":""https://static.flashscore.com/res/image/data/bookmakers/17-392.png"",""406"":""https://static.flashscore.com/res/image/data/bookmakers/17-406.png"",""417"":""https://static.flashscore.com/res/image/data/bookmakers/17-417.png"",""141"":""https://static.flashscore.com/res/image/data/bookmakers/17-141.png"",""398"":""https://static.flashscore.com/res/image/data/bookmakers/17-398.png"",""597"":""https://static.flashscore.com/res/image/data/bookmakers/17-597.png"",""160"":""https://static.flashscore.com/res/image/data/bookmakers/17-160.png"",""129"":""https://static.flashscore.com/res/image/data/bookmakers/17-129.png"",""661"":""https://static.flashscore.com/res/image/data/bookmakers/17-661.png"",""49"":""https://static.flashscore.com/res/image/data/bookmakers/17-49.png"",""657"":""https://static.flashscore.com/res/image/data/bookmakers/17-657.png"",""26"":""https://static.flashscore.com/res/image/data/bookmakers/17-26.png"",""883"":""https://static.flashscore.com/res/image/data/bookmakers/17-883.png"",""526"":""https://static.flashscore.com/res/image/data/bookmakers/17-526.png"",""991"":""https://static.flashscore.com/res/image/data/bookmakers/17-991.png"",""851"":""https://static.flashscore.com/res/image/data/bookmakers/17-851.png"",""549"":""https://static.flashscore.com/res/image/data/bookmakers/17-549.png"",""1003"":""https://static.flashscore.com/res/image/data/bookmakers/17-1003.png"",""1121"":""https://static.flashscore.com/res/image/data/bookmakers/17-1121.png"",""574"":""https://static.flashscore.com/res/image/data/bookmakers/17-574.png"",""833"":""https://static.flashscore.com/res/image/data/bookmakers/17-833.png"",""863"":""https://static.flashscore.com/res/image/data/bookmakers/17-863.png"",""933"":""https://static.flashscore.com/res/image/data/bookmakers/17-933.png"",""959"":""https://static.flashscore.com/res/image/data/bookmakers/17-959.png"",""995"":""https://static.flashscore.com/res/image/data/bookmakers/17-995.png"",""999"":""https://static.flashscore.com/res/image/data/bookmakers/17-999.png"",""943"":""https://static.flashscore.com/res/image/data/bookmakers/17-943.png"",""1023"":""https://static.flashscore.com/res/image/data/bookmakers/17-1023.png"",""1063"":""https://static.flashscore.com/res/image/data/bookmakers/17-1063.png"",""429"":""https://static.flashscore.com/res/image/data/bookmakers/17-429.png"",""953"":""https://static.flashscore.com/res/image/data/bookmakers/17-953.png"",""935"":""https://static.flashscore.com/res/image/data/bookmakers/17-935.png"",""955"":""https://static.flashscore.com/res/image/data/bookmakers/17-955.png"",""973"":""https://static.flashscore.com/res/image/data/bookmakers/17-973.png"",""1047"":""https://static.flashscore.com/res/image/data/bookmakers/17-1047.png"",""1069"":""https://static.flashscore.com/res/image/data/bookmakers/17-1069.png"",""1079"":""https://static.flashscore.com/res/image/data/bookmakers/17-1079.png"",""1091"":""https://static.flashscore.com/res/image/data/bookmakers/17-1091.png"",""1049"":""https://static.flashscore.com/res/image/data/bookmakers/17-1049.png"",""1133"":""https://static.flashscore.com/res/image/data/bookmakers/17-1133.png"",""1087"":""https://static.flashscore.com/res/image/data/bookmakers/17-1087.png"",""264"":""https://static.flashscore.com/res/image/data/bookmakers/17-264.png"",""484"":""https://static.flashscore.com/res/image/data/bookmakers/17-484.png"",""905"":""https://static.flashscore.com/res/image/data/bookmakers/17-905.png"",""1161"":""https://static.flashscore.com/res/image/data/bookmakers/17-1161.png"",""1153"":""https://static.flashscore.com/res/image/data/bookmakers/17-1153.png"",""1163"":""https://static.flashscore.com/res/image/data/bookmakers/17-1163.png"",""1137"":""https://static.flashscore.com/res/image/data/bookmakers/17-1137.png""}}}},
        }};
        _cjs.Api.loader.get('bookmakersData').fulfill(function(callback) {{
            callback(_cjs.bookmakerSettings);
        }});
    }});
</script>
</div></div><script type=""text/javascript"">
    var sport_url = '/futbol/';
    document.ifa = function () {{
        return true;
    }};
    var showMoreMenu = function (menuId) {{
        document.querySelectorAll(menuId).forEach(menu => {{
            menu.querySelectorAll(""div.leftMenu__item"").forEach(element => {{
                if (element.classList.contains(""leftMenu__item--hidden"")) {{
                    element.classList.remove(""leftMenu__item--hidden"");
                }}
            }});
            menu.querySelectorAll("".leftMenu__item--more"").forEach(element => {{
                element.className = 'leftMenu__item--hidden';
            }});
        }});

        return false;
    }};
</script>
<div class=""banner--underContent"">
    
<div id=""zoneContainer-content_bottom"" data-zone-group=""content_bottom""></div>

</div>
</main><aside class=""container__myMenu"" id=""lc""><div class=""container__overlay""><div class=""userControls"" id=""userControls""></div>
<div class=""menu country-list my-leagues leftMenu myTeamsWrapper""><div class=""leftMenu__head""><svg class=""leftMenu__icon leftMenu__icon--pin""><use xlink:href=""/res/_fs/image/13_symbols/action.svg?serial=1743#pin""/></svg><span class=""leftMenu__title"">Ligas Fijadas</span></div><div id=""my-leagues-list"" class=""menu leftMenu__list"">
<div class=""leftSkel__cont ska__chp--dark"">
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__banner"">
            <div class=""leftSkel__banner--item""></div>
            <div class=""leftSkel__banner--text""></div>
        </div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div></div></div>
<div class=""banner"">
<div id=""zoneContainer-left_menu_1"" data-zone-group=""left_menu_1""></div>
</div>
</div>    <script type=""text/javascript"">
        cjs.leftMenuTopLeagues = {{""1_81_W6BOzpK2"":{{""id"":""1_81_W6BOzpK2"",""menuOrder"":4,""flagId"":""81"",""title"":""ALEMANIA: Bundesliga"",""name"":""Bundesliga"",""url"":""\/futbol\/alemania\/bundesliga\/"",""from"":1770731156}},""1_176_QVmLl54o"":{{""id"":""1_176_QVmLl54o"",""menuOrder"":430,""flagId"":""176"",""title"":""ESPAÑA: LaLiga EA Sports"",""name"":""LaLiga EA Sports"",""url"":""\/futbol\/espana\/laliga-ea-sports\/"",""from"":1770731156}},""1_176_vZiPmPJi"":{{""id"":""1_176_vZiPmPJi"",""menuOrder"":431,""flagId"":""176"",""title"":""ESPAÑA: LaLiga Hypermotion"",""name"":""LaLiga Hypermotion"",""url"":""\/futbol\/espana\/laliga-hypermotion\/"",""from"":1770731156}},""1_176_lnb8EJRp"":{{""id"":""1_176_lnb8EJRp"",""menuOrder"":462,""flagId"":""176"",""title"":""ESPAÑA: Copa del Rey"",""name"":""Copa del Rey"",""url"":""\/futbol\/espana\/copa-del-rey\/"",""from"":1770731156}},""1_176_YTYRo1YM"":{{""id"":""1_176_YTYRo1YM"",""menuOrder"":464,""flagId"":""176"",""title"":""ESPAÑA: Supercopa"",""name"":""Supercopa"",""url"":""\/futbol\/espana\/supercopa\/"",""from"":1770731156}},""1_77_KIShoMk3"":{{""id"":""1_77_KIShoMk3"",""menuOrder"":507,""flagId"":""77"",""title"":""FRANCIA: Ligue 1"",""name"":""Ligue 1"",""url"":""\/futbol\/francia\/ligue-1\/"",""from"":1770731156}},""1_198_dYlOSQOD"":{{""id"":""1_198_dYlOSQOD"",""menuOrder"":587,""flagId"":""198"",""title"":""INGLATERRA: Premier League"",""name"":""Premier League"",""url"":""\/futbol\/inglaterra\/premier-league\/"",""from"":1770731156}},""1_98_COuk57Ci"":{{""id"":""1_98_COuk57Ci"",""menuOrder"":659,""flagId"":""98"",""title"":""ITALIA: Serie A"",""name"":""Serie A"",""url"":""\/futbol\/italia\/serie-a\/"",""from"":1770731156}},""1_139_Or1bBrWD"":{{""id"":""1_139_Or1bBrWD"",""menuOrder"":826,""flagId"":""139"",""title"":""PAÍSES BAJOS: Eredivisie"",""name"":""Eredivisie"",""url"":""\/futbol\/paises-bajos\/eredivisie\/"",""from"":1770731156}},""1_6_KQMVOQ0g"":{{""id"":""1_6_KQMVOQ0g"",""menuOrder"":1149,""flagId"":""6"",""title"":""EUROPA: Eurocopa"",""name"":""Eurocopa"",""url"":""\/futbol\/europa\/eurocopa\/"",""from"":1770731156}},""1_6_xGrwqq16"":{{""id"":""1_6_xGrwqq16"",""menuOrder"":1150,""flagId"":""6"",""title"":""EUROPA: Champions League"",""name"":""Champions League"",""url"":""\/futbol\/europa\/champions-league\/"",""from"":1770731156}},""1_6_ClDjv3V5"":{{""id"":""1_6_ClDjv3V5"",""menuOrder"":1151,""flagId"":""6"",""title"":""EUROPA: Europa League"",""name"":""Europa League"",""url"":""\/futbol\/europa\/europa-league\/"",""from"":1770731156}},""1_6_GfRbsVWM"":{{""id"":""1_6_GfRbsVWM"",""menuOrder"":1152,""flagId"":""6"",""title"":""EUROPA: Conference League"",""name"":""Conference League"",""url"":""\/futbol\/europa\/conference-league\/"",""from"":1770731156}},""1_6_A9yxE9Ke"":{{""id"":""1_6_A9yxE9Ke"",""menuOrder"":1153,""flagId"":""6"",""title"":""EUROPA: UEFA Nations League"",""name"":""UEFA Nations League"",""url"":""\/futbol\/europa\/uefa-nations-league\/"",""from"":1770731156}},""1_8_lvUBR5F8"":{{""id"":""1_8_lvUBR5F8"",""menuOrder"":1178,""flagId"":""8"",""title"":""MUNDIAL: Mundial"",""name"":""Mundial"",""url"":""\/futbol\/mundial\/copa-del-mundo\/"",""from"":1770731156}}}};
        cjs.Api.loader.get('cjs').callPriority(function (_cjs) {{
            _cjs.fromGlobalScope.my_leagues_init(1);
        }});
    </script>
    <div id='my-teams-left-menu' class='myTeamsWrapper'><div class=""leftMenu__head""><svg class=""leftMenu__icon leftMenu__icon--star""><use xlink:href=""/res/_fs/image/13_symbols/action.svg?serial=1743#pin""/></svg><span class=""leftMenu__title"">Mis Equipos </span></div>
</div>
            <script type=""text/javascript"">
                cjs.Api.loader.get('cjs').callPriority(function(_cjs) {{
                    _cjs.fromGlobalScope.myTeamsInit(1);
                }});
            </script>
            <div id=""category-left-menu""></div><div class=""left_menu_categories_seo"">
<a href=""/futbol/albania/"">Albania</a>
<a href=""/futbol/alemania/"">Alemania</a>
<a href=""/futbol/alemania/bundesliga/"">Bundesliga</a>
<a href=""/futbol/alemania/2-bundesliga/"">2. Bundesliga</a>
<a href=""/futbol/andorra/"">Andorra</a>
<a href=""/futbol/angola/"">Angola</a>
<a href=""/futbol/antigua-barbuda/"">Antigua y Barbuda</a>
<a href=""/futbol/arabia-saudi/"">Arabia Saudí</a>
<a href=""/futbol/argelia/"">Argelia</a>
<a href=""/futbol/argentina/"">Argentina</a>
<a href=""/futbol/armenia/"">Armenia</a>
<a href=""/futbol/aruba/"">Aruba</a>
<a href=""/futbol/australia/"">Australia</a>
<a href=""/futbol/austria/"">Austria</a>
<a href=""/futbol/austria/bundesliga/"">Bundesliga</a>
<a href=""/futbol/azerbaiyan/"">Azerbaiyán</a>
<a href=""/futbol/bahrein/"">Bahréin</a>
<a href=""/futbol/bangladesh/"">Bangladés</a>
<a href=""/futbol/barbados/"">Barbados</a>
<a href=""/futbol/belgica/"">Bélgica</a>
<a href=""/futbol/belgica/jupiler-pro-league/"">Jupiler Pro League</a>
<a href=""/futbol/benin/"">Benín</a>
<a href=""/futbol/bermudas/"">Bermudas</a>
<a href=""/futbol/bielorrusia/"">Bielorrusia</a>
<a href=""/futbol/bolivia/"">Bolivia</a>
<a href=""/futbol/bosnia-y-herzegovina/"">Bosnia y Herzegovina</a>
<a href=""/futbol/botswana/"">Botsuana</a>
<a href=""/futbol/brasil/"">Brasil</a>
<a href=""/futbol/brasil/serie-a-betano/"">Serie A Betano</a>
<a href=""/futbol/bulgaria/"">Bulgaria</a>
<a href=""/futbol/burkina-faso/"">Burkina Faso</a>
<a href=""/futbol/burundi/"">Burundi</a>
<a href=""/futbol/bhutan/"">Bután</a>
<a href=""/futbol/cabo-verde/"">Cabo Verde</a>
<a href=""/futbol/camboya/"">Camboya</a>
<a href=""/futbol/camerun/"">Camerún</a>
<a href=""/futbol/canada/"">Canadá</a>
<a href=""/futbol/qatar/"">Catar</a>
<a href=""/futbol/chad/"">Chad</a>
<a href=""/futbol/chile/"">Chile</a>
<a href=""/futbol/china/"">China</a>
<a href=""/futbol/chipre/"">Chipre</a>
<a href=""/futbol/colombia/"">Colombia</a>
<a href=""/futbol/congo/"">Congo</a>
<a href=""/futbol/corea-del-sur/"">Corea del Sur</a>
<a href=""/futbol/costa-de-marfil/"">Costa de Marfil</a>
<a href=""/futbol/costa-rica/"">Costa Rica</a>
<a href=""/futbol/croacia/"">Croacia</a>
<a href=""/futbol/dinamarca/"">Dinamarca</a>
<a href=""/futbol/ecuador/"">Ecuador</a>
<a href=""/futbol/egipto/"">Egipto</a>
<a href=""/futbol/el-salvador/"">El Salvador</a>
<a href=""/futbol/emiratos-arabes-unidos/"">Emiratos Árabes Unidos</a>
<a href=""/futbol/escocia/"">Escocia</a>
<a href=""/futbol/eslovaquia/"">Eslovaquia</a>
<a href=""/futbol/eslovenia/"">Eslovenia</a>
<a href=""/futbol/espana/"">España</a>
<a href=""/futbol/espana/laliga-ea-sports/"">LaLiga EA Sports</a>
<a href=""/futbol/espana/laliga-hypermotion/"">LaLiga Hypermotion</a>
<a href=""/futbol/espana/copa-del-rey/"">Copa del Rey</a>
<a href=""/futbol/usa/"">Estados Unidos</a>
<a href=""/futbol/usa/mls/"">MLS</a>
<a href=""/futbol/estonia/"">Estonia</a>
<a href=""/futbol/eswatini/"">Eswatini</a>
<a href=""/futbol/etiopia/"">Etiopía</a>
<a href=""/futbol/filipinas/"">Filipinas</a>
<a href=""/futbol/finlandia/"">Finlandia</a>
<a href=""/futbol/fiyi/"">Fiyi</a>
<a href=""/futbol/francia/"">Francia</a>
<a href=""/futbol/francia/ligue-1/"">Ligue 1</a>
<a href=""/futbol/gabon/"">Gabón</a>
<a href=""/futbol/gales/"">Gales</a>
<a href=""/futbol/gambia/"">Gambia</a>
<a href=""/futbol/georgia/"">Georgia</a>
<a href=""/futbol/ghana/"">Ghana</a>
<a href=""/futbol/gibraltar/"">Gibraltar</a>
<a href=""/futbol/grecia/"">Grecia</a>
<a href=""/futbol/guatemala/"">Guatemala</a>
<a href=""/futbol/guinea/"">Guinea</a>
<a href=""/futbol/haiti/"">Haití</a>
<a href=""/futbol/honduras/"">Honduras</a>
<a href=""/futbol/hong-kong/"">Hong Kong</a>
<a href=""/futbol/hungria/"">Hungría</a>
<a href=""/futbol/india/"">India</a>
<a href=""/futbol/indonesia/"">Indonesia</a>
<a href=""/futbol/inglaterra/"">Inglaterra</a>
<a href=""/futbol/inglaterra/premier-league/"">Premier League</a>
<a href=""/futbol/inglaterra/championship/"">Championship</a>
<a href=""/futbol/inglaterra/league-one/"">League One</a>
<a href=""/futbol/inglaterra/league-two/"">League Two</a>
<a href=""/futbol/inglaterra/fa-cup/"">FA Cup</a>
<a href=""/futbol/inglaterra/efl-cup/"">EFL Cup</a>
<a href=""/futbol/irak/"">Irak</a>
<a href=""/futbol/iran/"">Irán</a>
<a href=""/futbol/irlanda/"">Irlanda</a>
<a href=""/futbol/irlanda-del-norte/"">Irlanda del Norte</a>
<a href=""/futbol/islandia/"">Islandia</a>
<a href=""/futbol/islas-feroe/"">Islas Feroe</a>
<a href=""/futbol/israel/"">Israel</a>
<a href=""/futbol/italia/"">Italia</a>
<a href=""/futbol/italia/serie-a/"">Serie A</a>
<a href=""/futbol/italia/serie-b/"">Serie B</a>
<a href=""/futbol/italia/copa-italia/"">Copa Italia</a>
<a href=""/futbol/jamaica/"">Jamaica</a>
<a href=""/futbol/japon/"">Japón</a>
<a href=""/futbol/jordania/"">Jordania</a>
<a href=""/futbol/kazajistan/"">Kazajistán</a>
<a href=""/futbol/kenia/"">Kenia</a>
<a href=""/futbol/kyrgyzstan/"">Kirguistán</a>
<a href=""/futbol/kosovo/"">Kosovo</a>
<a href=""/futbol/kuwait/"">Kuwait</a>
<a href=""/futbol/laos/"">Laos</a>
<a href=""/futbol/lesotho/"">Lesoto</a>
<a href=""/futbol/letonia/"">Letonia</a>
<a href=""/futbol/libano/"">Líbano</a>
<a href=""/futbol/liberia/"">Liberia</a>
<a href=""/futbol/libia/"">Libia</a>
<a href=""/futbol/liechtenstein/"">Liechtenstein</a>
<a href=""/futbol/lituania/"">Lituania</a>
<a href=""/futbol/luxemburgo/"">Luxemburgo</a>
<a href=""/futbol/macao/"">Macao</a>
<a href=""/futbol/macedonia-del-norte/"">Macedonia del Norte</a>
<a href=""/futbol/malasia/"">Malasia</a>
<a href=""/futbol/malawi/"">Malaui</a>
<a href=""/futbol/mali/"">Mali</a>
<a href=""/futbol/malta/"">Malta</a>
<a href=""/futbol/marruecos/"">Marruecos</a>
<a href=""/futbol/martinica/"">Martinica</a>
<a href=""/futbol/mauricio/"">Mauricio</a>
<a href=""/futbol/mauritania/"">Mauritania</a>
<a href=""/futbol/mexico/"">México</a>
<a href=""/futbol/moldavia/"">Moldavia</a>
<a href=""/futbol/mongolia/"">Mongolia</a>
<a href=""/futbol/montenegro/"">Montenegro</a>
<a href=""/futbol/mozambique/"">Mozambique</a>
<a href=""/futbol/myanmar/"">Myanmar</a>
<a href=""/futbol/nicaragua/"">Nicaragua</a>
<a href=""/futbol/niger/"">Níger</a>
<a href=""/futbol/nigeria/"">Nigeria</a>
<a href=""/futbol/noruega/"">Noruega</a>
<a href=""/futbol/nueva-zelanda/"">Nueva Zelanda</a>
<a href=""/futbol/oman/"">Omán</a>
<a href=""/futbol/paises-bajos/"">Países Bajos</a>
<a href=""/futbol/paises-bajos/eredivisie/"">Eredivisie</a>
<a href=""/futbol/pakistan/"">Pakistán</a>
<a href=""/futbol/palestina/"">Palestina</a>
<a href=""/futbol/panama/"">Panamá</a>
<a href=""/futbol/paraguay/"">Paraguay</a>
<a href=""/futbol/peru/"">Perú</a>
<a href=""/futbol/polonia/"">Polonia</a>
<a href=""/futbol/portugal/"">Portugal</a>
<a href=""/futbol/portugal/liga-portugal/"">Liga Portugal</a>
<a href=""/futbol/rd-congo/"">RD Congo</a>
<a href=""/futbol/republica-checa/"">República Checa</a>
<a href=""/futbol/republica-dominicana/"">República Dominicana</a>
<a href=""/futbol/ruanda/"">Ruanda</a>
<a href=""/futbol/rumania/"">Rumanía</a>
<a href=""/futbol/rusia/"">Rusia</a>
<a href=""/futbol/san-marino/"">San Marino</a>
<a href=""/futbol/senegal/"">Senegal</a>
<a href=""/futbol/serbia/"">Serbia</a>
<a href=""/futbol/seychelles/"">Seychelles</a>
<a href=""/futbol/sierra-leona/"">Sierra Leona</a>
<a href=""/futbol/singapur/"">Singapur</a>
<a href=""/futbol/siria/"">Siria</a>
<a href=""/futbol/somalia/"">Somalia</a>
<a href=""/futbol/sri-lanka/"">Sri Lanka</a>
<a href=""/futbol/sudafrica/"">Sudáfrica</a>
<a href=""/futbol/sudafrica/betway-premiership/"">Betway Premiership</a>
<a href=""/futbol/sudan/"">Sudán</a>
<a href=""/futbol/suecia/"">Suecia</a>
<a href=""/futbol/suiza/"">Suiza</a>
<a href=""/futbol/suriname/"">Surinam</a>
<a href=""/futbol/tailandia/"">Tailandia</a>
<a href=""/futbol/taiwan/"">Taiwán</a>
<a href=""/futbol/tanzania/"">Tanzania</a>
<a href=""/futbol/tayikistan/"">Tayikistán</a>
<a href=""/futbol/togo/"">Togo</a>
<a href=""/futbol/trinidad-y-tobago/"">Trinidad y Tobago</a>
<a href=""/futbol/tunez/"">Túnez</a>
<a href=""/futbol/turkmenistan/"">Turkmenistán</a>
<a href=""/futbol/turquia/"">Turquía</a>
<a href=""/futbol/ucrania/"">Ucrania</a>
<a href=""/futbol/uganda/"">Uganda</a>
<a href=""/futbol/uruguay/"">Uruguay</a>
<a href=""/futbol/uzbekistan/"">Uzbekistán</a>
<a href=""/futbol/venezuela/"">Venezuela</a>
<a href=""/futbol/venezuela/liga-futve/"">Liga FUTVE</a>
<a href=""/futbol/vietnam/"">Vietnam</a>
<a href=""/futbol/yemen/"">Yemen</a>
<a href=""/futbol/zambia/"">Zambia</a>
<a href=""/futbol/zimbabwe/"">Zimbabue</a>
<a href=""/futbol/africa/"">África</a>
<a href=""/futbol/africa/copa-de-africa-de-naciones/"">Copa de África de Naciones</a>
<a href=""/futbol/africa/copa-del-mundo/"">Mundial</a>
<a href=""/futbol/asia/"">Asia</a>
<a href=""/futbol/asia/copa-asiatica/"">Copa Asiática</a>
<a href=""/futbol/asia/copa-del-mundo/"">Mundial</a>
<a href=""/futbol/australia-oceania/"">Australia & Oceanía</a>
<a href=""/futbol/australia-oceania/copa-del-mundo/"">Mundial</a>
<a href=""/futbol/europa/"">Europa</a>
<a href=""/futbol/europa/eurocopa/"">Eurocopa</a>
<a href=""/futbol/europa/champions-league/"">Champions League</a>
<a href=""/futbol/europa/europa-league/"">Europa League</a>
<a href=""/futbol/europa/uefa-nations-league/"">UEFA Nations League</a>
<a href=""/futbol/europa/campeonato-de-europa-sub-21/"">Europeo Sub-21</a>
<a href=""/futbol/europa/campeonato-de-europa-sub-19/"">Europeo Sub-19</a>
<a href=""/futbol/europa/copa-del-mundo/"">Mundial</a>
<a href=""/futbol/europa/campeonato-de-europa-sub-17/"">Europeo Sub-17</a>
<a href=""/futbol/mundial/"">Mundial</a>
<a href=""/futbol/mundial/copa-del-mundo/"">Mundial</a>
<a href=""/futbol/mundial/juegos-olimpicos/"">Juegos Olímpicos</a>
<a href=""/futbol/mundial/copa-del-mundo-sub-20/"">Mundial Sub-20</a>
<a href=""/futbol/mundial/copa-del-mundo-sub-17/"">Mundial Sub-17</a>
<a href=""/futbol/mundial/amistosos-internacionales/"">Amistosos Internacionales</a>
<a href=""/futbol/mundial/amistosos-de-clubs/"">Amistosos de Clubs</a>
<a href=""/futbol/norte-centroamerica-y-caribe/"">Norte, Centroamérica y Caribe</a>
<a href=""/futbol/norte-centroamerica-y-caribe/copa-de-oro/"">Copa Oro</a>
<a href=""/futbol/norte-centroamerica-y-caribe/copa-del-mundo/"">Mundial</a>
<a href=""/futbol/sudamerica/"">Sudamérica</a>
<a href=""/futbol/sudamerica/copa-america/"">Copa América</a>
<a href=""/futbol/sudamerica/copa-libertadores/"">Copa Libertadores</a>
<a href=""/futbol/sudamerica/copa-del-mundo/"">Mundial</a>
</div><div id=""left_menu_content""><div id=""left_menu_skeleton""><div class=""leftSkel__cont ska__chp--dark""><div class=""leftSkel__head"">
            <div class=""leftSkel__item leftSkel__item--head ska__dip--dark""></div>
        </div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__banner"">
            <div class=""leftSkel__banner--item""></div>
            <div class=""leftSkel__banner--text""></div>
        </div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__banner"">
            <div class=""leftSkel__banner--item""></div>
            <div class=""leftSkel__banner--text""></div>
        </div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__item""></div>
<div class=""leftSkel__arrow"">
            <div class=""leftSkel__item leftSkel__item--more""></div>
        </div></div></div></div>
    <script>
        cjs.Api.loader.get('myLeagues').call(function (ml) {{
            var inputData = {{
                rawData: [{{""SCN"":""Pa\u00edses"",""SCU"":"""",""SCC"":[{{""MC"":17,""MCN"":""Albania"",""ML"":""\/futbol\/albania\/"",""CSI"":1}},{{""MC"":81,""MCN"":""Alemania"",""ML"":""\/futbol\/alemania\/"",""CSI"":1}},{{""MC"":19,""MCN"":""Andorra"",""ML"":""\/futbol\/andorra\/"",""CSI"":1}},{{""MC"":20,""MCN"":""Angola"",""ML"":""\/futbol\/angola\/"",""CSI"":1}},{{""MC"":21,""MCN"":""Antigua y Barbuda"",""ML"":""\/futbol\/antigua-barbuda\/"",""CSI"":1}},{{""MC"":165,""MCN"":""Arabia Saud\u00ed"",""ML"":""\/futbol\/arabia-saudi\/"",""CSI"":1}},{{""MC"":18,""MCN"":""Argelia"",""ML"":""\/futbol\/argelia\/"",""CSI"":1}},{{""MC"":22,""MCN"":""Argentina"",""ML"":""\/futbol\/argentina\/"",""CSI"":1}},{{""MC"":23,""MCN"":""Armenia"",""ML"":""\/futbol\/armenia\/"",""CSI"":1}},{{""MC"":229,""MCN"":""Aruba"",""ML"":""\/futbol\/aruba\/"",""CSI"":1}},{{""MC"":24,""MCN"":""Australia"",""ML"":""\/futbol\/australia\/"",""CSI"":1}},{{""MC"":25,""MCN"":""Austria"",""ML"":""\/futbol\/austria\/"",""CSI"":1}},{{""MC"":26,""MCN"":""Azerbaiy\u00e1n"",""ML"":""\/futbol\/azerbaiyan\/"",""CSI"":1}},{{""MC"":28,""MCN"":""Bahr\u00e9in"",""ML"":""\/futbol\/bahrein\/"",""CSI"":1}},{{""MC"":29,""MCN"":""Banglad\u00e9s"",""ML"":""\/futbol\/bangladesh\/"",""CSI"":1}},{{""MC"":30,""MCN"":""Barbados"",""ML"":""\/futbol\/barbados\/"",""CSI"":1}},{{""MC"":32,""MCN"":""B\u00e9lgica"",""ML"":""\/futbol\/belgica\/"",""CSI"":1}},{{""MC"":34,""MCN"":""Ben\u00edn"",""ML"":""\/futbol\/benin\/"",""CSI"":1}},{{""MC"":230,""MCN"":""Bermudas"",""ML"":""\/futbol\/bermudas\/"",""CSI"":1}},{{""MC"":31,""MCN"":""Bielorrusia"",""ML"":""\/futbol\/bielorrusia\/"",""CSI"":1}},{{""MC"":36,""MCN"":""Bolivia"",""ML"":""\/futbol\/bolivia\/"",""CSI"":1}},{{""MC"":37,""MCN"":""Bosnia y Herzegovina"",""ML"":""\/futbol\/bosnia-y-herzegovina\/"",""CSI"":1}},{{""MC"":38,""MCN"":""Botsuana"",""ML"":""\/futbol\/botswana\/"",""CSI"":1}},{{""MC"":39,""MCN"":""Brasil"",""ML"":""\/futbol\/brasil\/"",""CSI"":1}},{{""MC"":41,""MCN"":""Bulgaria"",""ML"":""\/futbol\/bulgaria\/"",""CSI"":1}},{{""MC"":42,""MCN"":""Burkina Faso"",""ML"":""\/futbol\/burkina-faso\/"",""CSI"":1}},{{""MC"":44,""MCN"":""Burundi"",""ML"":""\/futbol\/burundi\/"",""CSI"":1}},{{""MC"":35,""MCN"":""But\u00e1n"",""ML"":""\/futbol\/bhutan\/"",""CSI"":1}},{{""MC"":48,""MCN"":""Cabo Verde"",""ML"":""\/futbol\/cabo-verde\/"",""CSI"":1}},{{""MC"":45,""MCN"":""Camboya"",""ML"":""\/futbol\/camboya\/"",""CSI"":1}},{{""MC"":46,""MCN"":""Camer\u00fan"",""ML"":""\/futbol\/camerun\/"",""CSI"":1}},{{""MC"":47,""MCN"":""Canad\u00e1"",""ML"":""\/futbol\/canada\/"",""CSI"":1}},{{""MC"":156,""MCN"":""Catar"",""ML"":""\/futbol\/qatar\/"",""CSI"":1}},{{""MC"":50,""MCN"":""Chad"",""ML"":""\/futbol\/chad\/"",""CSI"":1}},{{""MC"":51,""MCN"":""Chile"",""ML"":""\/futbol\/chile\/"",""CSI"":1}},{{""MC"":52,""MCN"":""China"",""ML"":""\/futbol\/china\/"",""CSI"":1}},{{""MC"":61,""MCN"":""Chipre"",""ML"":""\/futbol\/chipre\/"",""CSI"":1}},{{""MC"":53,""MCN"":""Colombia"",""ML"":""\/futbol\/colombia\/"",""CSI"":1}},{{""MC"":56,""MCN"":""Congo"",""ML"":""\/futbol\/congo\/"",""CSI"":1}},{{""MC"":106,""MCN"":""Corea del Sur"",""ML"":""\/futbol\/corea-del-sur\/"",""CSI"":1}},{{""MC"":58,""MCN"":""Costa de Marfil"",""ML"":""\/futbol\/costa-de-marfil\/"",""CSI"":1}},{{""MC"":57,""MCN"":""Costa Rica"",""ML"":""\/futbol\/costa-rica\/"",""CSI"":1}},{{""MC"":59,""MCN"":""Croacia"",""ML"":""\/futbol\/croacia\/"",""CSI"":1}},{{""MC"":63,""MCN"":""Dinamarca"",""ML"":""\/futbol\/dinamarca\/"",""CSI"":1}},{{""MC"":68,""MCN"":""Ecuador"",""ML"":""\/futbol\/ecuador\/"",""CSI"":1}},{{""MC"":69,""MCN"":""Egipto"",""ML"":""\/futbol\/egipto\/"",""CSI"":1}},{{""MC"":70,""MCN"":""El Salvador"",""ML"":""\/futbol\/el-salvador\/"",""CSI"":1}},{{""MC"":196,""MCN"":""Emiratos \u00c1rabes Unidos"",""ML"":""\/futbol\/emiratos-arabes-unidos\/"",""CSI"":1}},{{""MC"":199,""MCN"":""Escocia"",""ML"":""\/futbol\/escocia\/"",""CSI"":1}},{{""MC"":171,""MCN"":""Eslovaquia"",""ML"":""\/futbol\/eslovaquia\/"",""CSI"":1}},{{""MC"":172,""MCN"":""Eslovenia"",""ML"":""\/futbol\/eslovenia\/"",""CSI"":1}},{{""MC"":176,""MCN"":""Espa\u00f1a"",""ML"":""\/futbol\/espana\/"",""CSI"":1}},{{""MC"":200,""MCN"":""Estados Unidos"",""ML"":""\/futbol\/usa\/"",""CSI"":1}},{{""MC"":73,""MCN"":""Estonia"",""ML"":""\/futbol\/estonia\/"",""CSI"":1}},{{""MC"":180,""MCN"":""Eswatini"",""ML"":""\/futbol\/eswatini\/"",""CSI"":1}},{{""MC"":74,""MCN"":""Etiop\u00eda"",""ML"":""\/futbol\/etiopia\/"",""CSI"":1}},{{""MC"":153,""MCN"":""Filipinas"",""ML"":""\/futbol\/filipinas\/"",""CSI"":1}},{{""MC"":76,""MCN"":""Finlandia"",""ML"":""\/futbol\/finlandia\/"",""CSI"":1}},{{""MC"":75,""MCN"":""Fiyi"",""ML"":""\/futbol\/fiyi\/"",""CSI"":1}},{{""MC"":77,""MCN"":""Francia"",""ML"":""\/futbol\/francia\/"",""CSI"":1}},{{""MC"":78,""MCN"":""Gab\u00f3n"",""ML"":""\/futbol\/gabon\/"",""CSI"":1}},{{""MC"":207,""MCN"":""Gales"",""ML"":""\/futbol\/gales\/"",""CSI"":1}},{{""MC"":79,""MCN"":""Gambia"",""ML"":""\/futbol\/gambia\/"",""CSI"":1}},{{""MC"":80,""MCN"":""Georgia"",""ML"":""\/futbol\/georgia\/"",""CSI"":1}},{{""MC"":82,""MCN"":""Ghana"",""ML"":""\/futbol\/ghana\/"",""CSI"":1}},{{""MC"":304,""MCN"":""Gibraltar"",""ML"":""\/futbol\/gibraltar\/"",""CSI"":1}},{{""MC"":83,""MCN"":""Grecia"",""ML"":""\/futbol\/grecia\/"",""CSI"":1}},{{""MC"":85,""MCN"":""Guatemala"",""ML"":""\/futbol\/guatemala\/"",""CSI"":1}},{{""MC"":86,""MCN"":""Guinea"",""ML"":""\/futbol\/guinea\/"",""CSI"":1}},{{""MC"":89,""MCN"":""Hait\u00ed"",""ML"":""\/futbol\/haiti\/"",""CSI"":1}},{{""MC"":90,""MCN"":""Honduras"",""ML"":""\/futbol\/honduras\/"",""CSI"":1}},{{""MC"":222,""MCN"":""Hong Kong"",""ML"":""\/futbol\/hong-kong\/"",""CSI"":1}},{{""MC"":91,""MCN"":""Hungr\u00eda"",""ML"":""\/futbol\/hungria\/"",""CSI"":1}},{{""MC"":93,""MCN"":""India"",""ML"":""\/futbol\/india\/"",""CSI"":1}},{{""MC"":228,""MCN"":""Indonesia"",""ML"":""\/futbol\/indonesia\/"",""CSI"":1}},{{""MC"":198,""MCN"":""Inglaterra"",""ML"":""\/futbol\/inglaterra\/"",""CSI"":1}},{{""MC"":95,""MCN"":""Irak"",""ML"":""\/futbol\/irak\/"",""CSI"":1}},{{""MC"":94,""MCN"":""Ir\u00e1n"",""ML"":""\/futbol\/iran\/"",""CSI"":1}},{{""MC"":96,""MCN"":""Irlanda"",""ML"":""\/futbol\/irlanda\/"",""CSI"":1}},{{""MC"":144,""MCN"":""Irlanda del Norte"",""ML"":""\/futbol\/irlanda-del-norte\/"",""CSI"":1}},{{""MC"":92,""MCN"":""Islandia"",""ML"":""\/futbol\/islandia\/"",""CSI"":1}},{{""MC"":231,""MCN"":""Islas Feroe"",""ML"":""\/futbol\/islas-feroe\/"",""CSI"":1}},{{""MC"":97,""MCN"":""Israel"",""ML"":""\/futbol\/israel\/"",""CSI"":1}},{{""MC"":98,""MCN"":""Italia"",""ML"":""\/futbol\/italia\/"",""CSI"":1}},{{""MC"":99,""MCN"":""Jamaica"",""ML"":""\/futbol\/jamaica\/"",""CSI"":1}},{{""MC"":100,""MCN"":""Jap\u00f3n"",""ML"":""\/futbol\/japon\/"",""CSI"":1}},{{""MC"":101,""MCN"":""Jordania"",""ML"":""\/futbol\/jordania\/"",""CSI"":1}},{{""MC"":102,""MCN"":""Kazajist\u00e1n"",""ML"":""\/futbol\/kazajistan\/"",""CSI"":1}},{{""MC"":103,""MCN"":""Kenia"",""ML"":""\/futbol\/kenia\/"",""CSI"":1}},{{""MC"":108,""MCN"":""Kirguist\u00e1n"",""ML"":""\/futbol\/kyrgyzstan\/"",""CSI"":1}},{{""MC"":212,""MCN"":""Kosovo"",""ML"":""\/futbol\/kosovo\/"",""CSI"":1}},{{""MC"":107,""MCN"":""Kuwait"",""ML"":""\/futbol\/kuwait\/"",""CSI"":1}},{{""MC"":109,""MCN"":""Laos"",""ML"":""\/futbol\/laos\/"",""CSI"":1}},{{""MC"":112,""MCN"":""Lesoto"",""ML"":""\/futbol\/lesotho\/"",""CSI"":1}},{{""MC"":110,""MCN"":""Letonia"",""ML"":""\/futbol\/letonia\/"",""CSI"":1}},{{""MC"":111,""MCN"":""L\u00edbano"",""ML"":""\/futbol\/libano\/"",""CSI"":1}},{{""MC"":113,""MCN"":""Liberia"",""ML"":""\/futbol\/liberia\/"",""CSI"":1}},{{""MC"":114,""MCN"":""Libia"",""ML"":""\/futbol\/libia\/"",""CSI"":1}},{{""MC"":115,""MCN"":""Liechtenstein"",""ML"":""\/futbol\/liechtenstein\/"",""CSI"":1}},{{""MC"":116,""MCN"":""Lituania"",""ML"":""\/futbol\/lituania\/"",""CSI"":1}},{{""MC"":117,""MCN"":""Luxemburgo"",""ML"":""\/futbol\/luxemburgo\/"",""CSI"":1}},{{""MC"":233,""MCN"":""Macao"",""ML"":""\/futbol\/macao\/"",""CSI"":1}},{{""MC"":118,""MCN"":""Macedonia del Norte"",""ML"":""\/futbol\/macedonia-del-norte\/"",""CSI"":1}},{{""MC"":121,""MCN"":""Malasia"",""ML"":""\/futbol\/malasia\/"",""CSI"":1}},{{""MC"":120,""MCN"":""Malaui"",""ML"":""\/futbol\/malawi\/"",""CSI"":1}},{{""MC"":123,""MCN"":""Mali"",""ML"":""\/futbol\/mali\/"",""CSI"":1}},{{""MC"":124,""MCN"":""Malta"",""ML"":""\/futbol\/malta\/"",""CSI"":1}},{{""MC"":134,""MCN"":""Marruecos"",""ML"":""\/futbol\/marruecos\/"",""CSI"":1}},{{""MC"":255,""MCN"":""Martinica"",""ML"":""\/futbol\/martinica\/"",""CSI"":1}},{{""MC"":127,""MCN"":""Mauricio"",""ML"":""\/futbol\/mauricio\/"",""CSI"":1}},{{""MC"":126,""MCN"":""Mauritania"",""ML"":""\/futbol\/mauritania\/"",""CSI"":1}},{{""MC"":128,""MCN"":""M\u00e9xico"",""ML"":""\/futbol\/mexico\/"",""CSI"":1}},{{""MC"":130,""MCN"":""Moldavia"",""ML"":""\/futbol\/moldavia\/"",""CSI"":1}},{{""MC"":132,""MCN"":""Mongolia"",""ML"":""\/futbol\/mongolia\/"",""CSI"":1}},{{""MC"":133,""MCN"":""Montenegro"",""ML"":""\/futbol\/montenegro\/"",""CSI"":1}},{{""MC"":135,""MCN"":""Mozambique"",""ML"":""\/futbol\/mozambique\/"",""CSI"":1}},{{""MC"":43,""MCN"":""Myanmar"",""ML"":""\/futbol\/myanmar\/"",""CSI"":1}},{{""MC"":141,""MCN"":""Nicaragua"",""ML"":""\/futbol\/nicaragua\/"",""CSI"":1}},{{""MC"":142,""MCN"":""N\u00edger"",""ML"":""\/futbol\/niger\/"",""CSI"":1}},{{""MC"":143,""MCN"":""Nigeria"",""ML"":""\/futbol\/nigeria\/"",""CSI"":1}},{{""MC"":145,""MCN"":""Noruega"",""ML"":""\/futbol\/noruega\/"",""CSI"":1}},{{""MC"":140,""MCN"":""Nueva Zelanda"",""ML"":""\/futbol\/nueva-zelanda\/"",""CSI"":1}},{{""MC"":146,""MCN"":""Om\u00e1n"",""ML"":""\/futbol\/oman\/"",""CSI"":1}},{{""MC"":139,""MCN"":""Pa\u00edses Bajos"",""ML"":""\/futbol\/paises-bajos\/"",""CSI"":1}},{{""MC"":147,""MCN"":""Pakist\u00e1n"",""ML"":""\/futbol\/pakistan\/"",""CSI"":1}},{{""MC"":215,""MCN"":""Palestina"",""ML"":""\/futbol\/palestina\/"",""CSI"":1}},{{""MC"":149,""MCN"":""Panam\u00e1"",""ML"":""\/futbol\/panama\/"",""CSI"":1}},{{""MC"":151,""MCN"":""Paraguay"",""ML"":""\/futbol\/paraguay\/"",""CSI"":1}},{{""MC"":152,""MCN"":""Per\u00fa"",""ML"":""\/futbol\/peru\/"",""CSI"":1}},{{""MC"":154,""MCN"":""Polonia"",""ML"":""\/futbol\/polonia\/"",""CSI"":1}},{{""MC"":155,""MCN"":""Portugal"",""ML"":""\/futbol\/portugal\/"",""CSI"":1}},{{""MC"":55,""MCN"":""RD Congo"",""ML"":""\/futbol\/rd-congo\/"",""CSI"":1}},{{""MC"":62,""MCN"":""Rep\u00fablica Checa"",""ML"":""\/futbol\/republica-checa\/"",""CSI"":1}},{{""MC"":66,""MCN"":""Rep\u00fablica Dominicana"",""ML"":""\/futbol\/republica-dominicana\/"",""CSI"":1}},{{""MC"":159,""MCN"":""Ruanda"",""ML"":""\/futbol\/ruanda\/"",""CSI"":1}},{{""MC"":157,""MCN"":""Ruman\u00eda"",""ML"":""\/futbol\/rumania\/"",""CSI"":1}},{{""MC"":158,""MCN"":""Rusia"",""ML"":""\/futbol\/rusia\/"",""CSI"":1}},{{""MC"":163,""MCN"":""San Marino"",""ML"":""\/futbol\/san-marino\/"",""CSI"":1}},{{""MC"":166,""MCN"":""Senegal"",""ML"":""\/futbol\/senegal\/"",""CSI"":1}},{{""MC"":167,""MCN"":""Serbia"",""ML"":""\/futbol\/serbia\/"",""CSI"":1}},{{""MC"":168,""MCN"":""Seychelles"",""ML"":""\/futbol\/seychelles\/"",""CSI"":1}},{{""MC"":169,""MCN"":""Sierra Leona"",""ML"":""\/futbol\/sierra-leona\/"",""CSI"":1}},{{""MC"":170,""MCN"":""Singapur"",""ML"":""\/futbol\/singapur\/"",""CSI"":1}},{{""MC"":183,""MCN"":""Siria"",""ML"":""\/futbol\/siria\/"",""CSI"":1}},{{""MC"":174,""MCN"":""Somalia"",""ML"":""\/futbol\/somalia\/"",""CSI"":1}},{{""MC"":177,""MCN"":""Sri Lanka"",""ML"":""\/futbol\/sri-lanka\/"",""CSI"":1}},{{""MC"":175,""MCN"":""Sud\u00e1frica"",""ML"":""\/futbol\/sudafrica\/"",""CSI"":1}},{{""MC"":178,""MCN"":""Sud\u00e1n"",""ML"":""\/futbol\/sudan\/"",""CSI"":1}},{{""MC"":181,""MCN"":""Suecia"",""ML"":""\/futbol\/suecia\/"",""CSI"":1}},{{""MC"":182,""MCN"":""Suiza"",""ML"":""\/futbol\/suiza\/"",""CSI"":1}},{{""MC"":179,""MCN"":""Surinam"",""ML"":""\/futbol\/suriname\/"",""CSI"":1}},{{""MC"":186,""MCN"":""Tailandia"",""ML"":""\/futbol\/tailandia\/"",""CSI"":1}},{{""MC"":218,""MCN"":""Taiw\u00e1n"",""ML"":""\/futbol\/taiwan\/"",""CSI"":1}},{{""MC"":185,""MCN"":""Tanzania"",""ML"":""\/futbol\/tanzania\/"",""CSI"":1}},{{""MC"":184,""MCN"":""Tayikist\u00e1n"",""ML"":""\/futbol\/tayikistan\/"",""CSI"":1}},{{""MC"":187,""MCN"":""Togo"",""ML"":""\/futbol\/togo\/"",""CSI"":1}},{{""MC"":189,""MCN"":""Trinidad y Tobago"",""ML"":""\/futbol\/trinidad-y-tobago\/"",""CSI"":1}},{{""MC"":190,""MCN"":""T\u00fanez"",""ML"":""\/futbol\/tunez\/"",""CSI"":1}},{{""MC"":192,""MCN"":""Turkmenist\u00e1n"",""ML"":""\/futbol\/turkmenistan\/"",""CSI"":1}},{{""MC"":191,""MCN"":""Turqu\u00eda"",""ML"":""\/futbol\/turquia\/"",""CSI"":1}},{{""MC"":195,""MCN"":""Ucrania"",""ML"":""\/futbol\/ucrania\/"",""CSI"":1}},{{""MC"":194,""MCN"":""Uganda"",""ML"":""\/futbol\/uganda\/"",""CSI"":1}},{{""MC"":201,""MCN"":""Uruguay"",""ML"":""\/futbol\/uruguay\/"",""CSI"":1}},{{""MC"":202,""MCN"":""Uzbekist\u00e1n"",""ML"":""\/futbol\/uzbekistan\/"",""CSI"":1}},{{""MC"":205,""MCN"":""Venezuela"",""ML"":""\/futbol\/venezuela\/"",""CSI"":1}},{{""MC"":206,""MCN"":""Vietnam"",""ML"":""\/futbol\/vietnam\/"",""CSI"":1}},{{""MC"":208,""MCN"":""Yemen"",""ML"":""\/futbol\/yemen\/"",""CSI"":1}},{{""MC"":209,""MCN"":""Zambia"",""ML"":""\/futbol\/zambia\/"",""CSI"":1}},{{""MC"":210,""MCN"":""Zimbabue"",""ML"":""\/futbol\/zimbabwe\/"",""CSI"":1}}]}},{{""SCN"":""Otras competiciones"",""SCU"":"""",""SCC"":[{{""MC"":1,""MCN"":""\u00c1frica"",""ML"":""\/futbol\/africa\/"",""CSI"":1}},{{""MC"":5,""MCN"":""Asia"",""ML"":""\/futbol\/asia\/"",""CSI"":1}},{{""MC"":7,""MCN"":""Australia & Ocean\u00eda"",""ML"":""\/futbol\/australia-oceania\/"",""CSI"":1}},{{""MC"":6,""MCN"":""Europa"",""ML"":""\/futbol\/europa\/"",""CSI"":1}},{{""MC"":8,""MCN"":""Mundial"",""ML"":""\/futbol\/mundial\/"",""CSI"":1}},{{""MC"":2,""MCN"":""Norte, Centroam\u00e9rica y Caribe"",""ML"":""\/futbol\/norte-centroamerica-y-caribe\/"",""CSI"":1}},{{""MC"":3,""MCN"":""Sudam\u00e9rica"",""ML"":""\/futbol\/sudamerica\/"",""CSI"":1}}]}}],
                rawBannersData: {{""banners"":{{""afterCategory"":{{""10"":""\n<div id=\""zoneContainer-left_menu_2\"" data-zone-group=\""left_menu_2\""><\/div>\n"",""20"":""\n<div id=\""zoneContainer-left_menu_3\"" data-zone-group=\""left_menu_3\""><\/div>\n""}}}},""showMoreLimit"":22}},
                isTopGetter: (k) => ml.isTopByLabelKey(k),
                topToggler: (k) => ml.toggleTop(k, event, true),
                translations: {{
                    add: ""Fija esta competición en Ligas Fijadas"",
                    remove: ""Desfija esta competición de Ligas Fijadas"",
                    more: ""Mostrar más"",
                }},
                isMixedPage: false,
                activeTournament: """",
            }};

            cjs.Api.loader.get('categoryMenu').call(inputData, function (module) {{
            }});
        }});

    </script>

    </div></aside></div><aside id=""extraContent"" class=""extraContent"">
    <div class=""extraContent__content"">
        <div class=""extraContent__text"">El servicio de marcadores de fútbol en directo de Flashscore.es ofrece marcadores y resultados de fútbol de <a href=""/futbol/espana/laliga-ea-sports/"">LaLiga EA Sports 2026</a> y otras más de 1000 ligas, copas y torneos de fútbol (<a href=""/futbol/inglaterra/premier-league/"">Premier League</a>, <a href=""/futbol/italia/serie-a/"">Serie A</a>, <a href=""/futbol/alemania/bundesliga/"">Bundesliga</a>, <a href=""/futbol/europa/champions-league/"">Champions League</a>, etc.), así como estadísticas detalladas (tiros a puerta, posesión del balón, goles esperados (xG), córneres, tarjetas amarillas y rojas, faltas, etc.), notas de jugadores, estadísticas H2H, alineaciones, comentarios en directo, informes de partidos, jugador del partido, nota media del equipo, clasificaciones, alertas de goles, goleadores, alineaciones y otras informaciones de fútbol en directo. Selecciona tus partidos e infórmate de los marcadores y resultados en directo. El servicio de marcadores de fútbol en directo se proporciona en tiempo real, sin necesidad de recargar la página. Consulta en Flashscore.es todos los resultados de fútbol de hoy. Además del fútbol, las <a href=""/noticias/futbol/"">noticias de fútbol</a>, <a href=""/noticias/apuestas/SYsd5NSpWSzc94ws/"">apuestas deportivas</a> y el <a href=""/noticias/mercado-de-fichajes/Muqo1b4LWSzc94ws/"">mercado de fichajes</a>, en Flashscore puedes seguir cerca de 40 deportes. Consulta la lista completa de deportes y el número de competiciones con cobertura en la sección <a href=""/livescore/"">Livescore</a>.<br>
<p><strong>LaLiga EA Sports 2026 en directo</strong> en Flashscore.es. Sigue los marcadores en directo del torneo de fútbol masculino de la liga española, <a href=""/futbol/espana/laliga-ea-sports/resultados/"">resultados</a>, <a href=""/futbol/espana/laliga-ea-sports/partidos/"">partidos</a>, <a href=""/noticias/laliga-ea-sports/QVmLl54oCdnS0XT8/"">noticias</a> y <a href=""/futbol/espana/laliga-ea-sports/clasificacion/"">clasificación de la LaLiga EA Sports</a>.<br></p>
<p><strong>LaLiga Hypermotion 2026 en directo</strong> en Flashscore.es. Sigue los marcadores en directo del torneo de fútbol masculino de la segunda división española, <a href=""/futbol/espana/laliga-hypermotion/resultados/"">resultados</a>, <a href=""/futbol/espana/laliga-hypermotion/partidos/"">partidos</a>, <a href=""/noticias/laliga-hypermotion/vZiPmPJiCdnS0XT8/"">noticias</a> y <a href=""/futbol/espana/laliga-hypermotion/clasificacion/"">clasificación de la LaLiga Hypermotion</a>.<br></p> <span class=""next_round"">13.02. <a href=""/partido/futbol/ca-osasuna-ETdxjU8a/elche-cf-4jl02tPF/"" target=""_blank"">Elche CF - CA Osasuna</a>, 14.02. <a href=""/partido/futbol/celta-vigo-8pvUZFhf/rcd-espanyol-QFfPdh1J/"" target=""_blank"">RCD Espanyol - RC Celta</a>, <a href=""/partido/futbol/getafe-cf-dboeiWOt/villarreal-cf-lUatW5jE/"" target=""_blank"">Getafe CF - Villarreal CF</a>, <a href=""/partido/futbol/alaves-hxt57t2q/sevilla-h8oAv4Ts/"" target=""_blank"">Sevilla FC - Alavés</a>, <a href=""/partido/futbol/real-madrid-W8mj7MDD/real-sociedad-jNvak2f3/"" target=""_blank"">Real Madrid - Real Sociedad</a>, 15.02. <a href=""/partido/futbol/athletic-club-IP5zl0cJ/real-oviedo-SzYzw34K/"" target=""_blank"">Real Oviedo - Athletic Club</a>, <a href=""/partido/futbol/atletico-madrid-jaarqpLQ/rayo-vallecano-8bcjFy6O/"" target=""_blank"">Rayo Vallecano - Atlético de Madrid</a>, <a href=""/partido/futbol/levante-ud-G8FL0ShI/valencia-cf-CQeaytrD/"" target=""_blank"">Levante UD - Valencia CF</a>, <a href=""/partido/futbol/rcd-mallorca-4jDQxrbf/real-betis-vJbTeCGP/"" target=""_blank"">RCD Mallorca - Real Betis</a>, 16.02. <a href=""/partido/futbol/fc-barcelona-SKbpVP5K/girona-fc-nNNpcUSL/"" target=""_blank"">Girona FC - FC Barcelona</a>, 18.02. <a href=""/partido/futbol/levante-ud-G8FL0ShI/villarreal-cf-lUatW5jE/"" target=""_blank"">Levante UD - Villarreal CF</a>, 20.02. <a href=""/partido/futbol/athletic-club-IP5zl0cJ/elche-cf-4jl02tPF/"" target=""_blank"">Athletic Club - Elche CF</a>, 21.02. <a href=""/partido/futbol/real-oviedo-SzYzw34K/real-sociedad-jNvak2f3/"" target=""_blank"">Real Sociedad - Real Oviedo</a>, <a href=""/partido/futbol/rayo-vallecano-8bcjFy6O/real-betis-vJbTeCGP/"" target=""_blank"">Real Betis - Rayo Vallecano</a>, <a href=""/partido/futbol/ca-osasuna-ETdxjU8a/real-madrid-W8mj7MDD/"" target=""_blank"">CA Osasuna - Real Madrid</a>, <a href=""/partido/futbol/atletico-madrid-jaarqpLQ/rcd-espanyol-QFfPdh1J/"" target=""_blank"">Atlético de Madrid - RCD Espanyol</a></span><br></div>
    </div>
    <div class=""extraContent__button"">
        Mostrar más
    </div>
</aside>
<script>
    (function() {{
        const buttons = document.getElementsByClassName(""extraContent__button"");
        Array.from(buttons).map((button) => {{
            button.addEventListener(""click"", () => {{
                const elem = document.getElementById(""extraContent"");
                if (elem) {{
                    elem.classList.add(""extraContent--active"");
                }}
            }});
        }});
    }}());

</script>
</div></div></div><footer class=""footerContainer"">
    <div class=""footerContainer__content"">
<div class=""seoFooter"">
    <div class=""seoFooter__mainGroup"">
        <div class=""seoFooter__categories"">
                <div class=""seoFooter__category"">
                    <div class=""seoFooter__categoryTitle""><a href=""https://www.flashscore.es/"">FÚTBOL</a></div>
                    <div class=""seoFooter__categoryLinks"">
                            <div>
                                <a href=""/futbol/espana/laliga-ea-sports/"">LaLiga EA Sports 2026</a>
                            </div>
                            <div>
                                <a href=""/futbol/espana/laliga-hypermotion/"">LaLiga Hypermotion 2026</a>
                            </div>
                            <div>
                                <a href=""/futbol/inglaterra/premier-league/"">Premier League 2026</a>
                            </div>
                            <div>
                                <a href=""/futbol/italia/serie-a/"">Serie A Italia 2026</a>
                            </div>
                            <div>
                                <a href=""/futbol/europa/champions-league/"">Champions League</a>
                            </div>
                            <div>
                                <a href=""/futbol/europa/europa-league/"">Europa League</a>
                            </div>
                            <div>
                                <a href=""/futbol/europa/conference-league/"">Conference League</a>
                            </div>
                            <div>
                                <a href=""https://www.flashscore.es/apuestas/"">Apuestas</a>
                            </div>
                            <div>
                                <a href=""https://www.flashscore.es/apuestas/mejores-casas-apuestas/"">Mejores casas de apuestas</a>
                            </div>
                            <div>
                                <a href=""https://www.flashscore.es/apuestas/bonos-apuestas/"">Apuestas con bonos</a>
                            </div>
                    </div>
                </div>
                <div class=""seoFooter__category"">
                    <div class=""seoFooter__categoryTitle""><a href=""https://www.flashscore.es/tenis/"">tenis</a></div>
                    <div class=""seoFooter__categoryLinks"">
                            <div>
                                <a href=""/tenis/atp-individuales/dallas/"">ATP Dallas 2026</a>
                            </div>
                            <div>
                                <a href=""/tenis/atp-individuales/roterdam/"">ATP Róterdam 2026</a>
                            </div>
                            <div>
                                <a href=""/tenis/wta-individuales/doha/"">WTA Doha</a>
                            </div>
                            <div>
                                <a href=""/tenis/atp-individuales/buenos-aires/"">ATP Buenos Aires</a>
                            </div>
                            <div>
                                <a href=""https://www.flashscore.es/noticias/tenis/"">Noticias de Tenis</a>
                            </div>
                            <div>
                                <a href=""/equipo/alcaraz-garfia-carlos/UkhgIFEq/"">Carlos Alcaraz</a>
                            </div>
                            <div>
                                <a href=""/equipo/davidovich-fokina-alejandro/0zQXLfz4/"">Alejandro Davidovich Fokina</a>
                            </div>
                            <div>
                                <a href=""/equipo/munar-jaume/zZieQm4D/"">Jaume Munar</a>
                            </div>
                            <div>
                                <a href=""/equipo/badosa-paula/Wl76rX3I/"">Paula Badosa</a>
                            </div>
                            <div>
                                <a href=""/equipo/bouzas-maneiro-jessica/Gj20DdaG/"">Jessica Bouzas Maneiro</a>
                            </div>
                    </div>
                </div>
                <div class=""seoFooter__category"">
                    <div class=""seoFooter__categoryTitle""><a href=""https://www.flashscore.es/noticias/"">TENDENCIAS</a></div>
                    <div class=""seoFooter__categoryLinks"">
                            <div>
                                <a href=""/baloncesto/espana/liga-endesa/"">Liga Endesa 2026</a>
                            </div>
                            <div>
                                <a href=""/baloncesto/espana/primera-feb/"">Primera FEB</a>
                            </div>
                            <div>
                                <a href=""/baloncesto/espana/segunda-feb/"">Segunda FEB</a>
                            </div>
                            <div>
                                <a href=""/baloncesto/europa/euroliga/"">Euroliga Resultados</a>
                            </div>
                            <div>
                                <a href=""/baloncesto/europa/champions-league/"">Baloncesto Champions League</a>
                            </div>
                            <div>
                                <a href=""/baloncesto/usa/nba/"">NBA 2026</a>
                            </div>
                            <div>
                                <a href=""/balonmano/espana/liga-asobal/"">Liga ASOBAL 2026</a>
                            </div>
                            <div>
                                <a href=""/hockey/usa/nhl/"">NHL Resultados</a>
                            </div>
                            <div>
                                <a href=""/futbol-sala/espana/liga-nacional/"">Liga Nacional, LNFS 2026</a>
                            </div>
                            <div>
                                <a href=""https://www.flashscore.es/noticias/juegos-olimpicos-de-invierno/vZtdV5VaWSzc94ws/"">Juegos Olímpicos de Invierno Milán–Cortina 2026</a>
                            </div>
                    </div>
                </div>
                <div class=""seoFooter__category"">
                    <div class=""seoFooter__categoryTitle""><a href=""https://www.flashscore.es/"">MARCADORES EN DIRECTO</a></div>
                    <div class=""seoFooter__categoryLinks"">
                            <div>
                                <a href=""/partido/balonmano/eon-horneo-alicante-SdLLX986/fc-barcelona-WfcbnFIB/"">Barcelona - Horneo Alicante</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol-sala/movistar-inter-lpHTz9Tl/palma-futsal-SzGXZSre/"">Movistar Inter - Palma Futsal</a>
                            </div>
                            <div>
                                <a href=""/partido/baloncesto/asvel-basket-KrXB5aC1/valencia-basket-6w8lqYGE/"">Valencia Basket - Asvel Basket</a>
                            </div>
                            <div>
                                <a href=""/partido/baloncesto/barcelona-EDQLcAfL/paris-CdCP6AWf/"">Barcelona - Paris</a>
                            </div>
                            <div>
                                <a href=""/partido/baloncesto/as-monaco-YFxc9UAj/saski-baskonia-8zQPdU9R/"">Mónaco - Baskonia</a>
                            </div>
                            <div>
                                <a href=""/partido/baloncesto/partizan-GAiz1YL6/real-madrid-MP6gLUO7/"">Partizan - Real Madrid</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol-sala/palma-futsal-SzGXZSre/xota-fs-2c42Z6D7/"">Palma Futsal - Xota FS</a>
                            </div>
                            <div>
                                <a href=""/partido/balonmano/atletico-valladolid-tvR9r3Rm/fc-barcelona-WfcbnFIB/"">Atl. Valladolid - Barcelona</a>
                            </div>
                            <div>
                                <a href=""/partido/baloncesto/morabanc-andorra-AeB582X0/valencia-basket-6w8lqYGE/"">MoraBanc Andorra - Valencia Basket</a>
                            </div>
                            <div>
                                <a href=""/partido/baloncesto/barcelona-EDQLcAfL/icl-manresa-KAm6vlvR/"">Barcelona - Manresa</a>
                            </div>
                    </div>
                </div>
                <div class=""seoFooter__category"">
                    <div class=""seoFooter__categoryTitle""><a href=""https://www.flashscore.es/"">RESULTADOS DE FÚTBOL</a></div>
                    <div class=""seoFooter__categoryLinks"">
                            <div>
                                <a href=""/partido/futbol/athletic-club-IP5zl0cJ/real-sociedad-jNvak2f3/"">Athletic Club - Real Sociedad</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/atletico-madrid-jaarqpLQ/fc-barcelona-SKbpVP5K/"">Atlético de Madrid - Barcelona</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/getafe-cf-dboeiWOt/villarreal-cf-lUatW5jE/"">Getafe - Villarreal</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/real-madrid-W8mj7MDD/real-sociedad-jNvak2f3/"">Real Madrid - Real Sociedad</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/athletic-club-IP5zl0cJ/real-oviedo-SzYzw34K/"">Real Oviedo - Athletic Club</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/atletico-madrid-jaarqpLQ/rayo-vallecano-8bcjFy6O/"">Rayo Vallecano - Atlético de Madrid</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/rcd-mallorca-4jDQxrbf/real-betis-vJbTeCGP/"">Mallorca - Real Betis</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/fc-barcelona-SKbpVP5K/girona-fc-nNNpcUSL/"">Girona - Barcelona</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/benfica-zBkyuyRI/real-madrid-W8mj7MDD/"">Benfica - Real Madrid</a>
                            </div>
                            <div>
                                <a href=""/partido/futbol/levante-ud-G8FL0ShI/villarreal-cf-lUatW5jE/"">Levante - Villarreal</a>
                            </div>
                    </div>
                </div>
        </div>
    </div>
</div>
<div class=""selfPromo"">
    <div class=""selfPromo__mainGroup"">
        <div class=""selfPromo__box selfPromo__box--project"">
            <div class=""selfPromo__boxTitle"" >Flashscore.es</div>
            <div class=""selfPromo__boxContent"">
                <div class=""selfPromo__boxContent--links"">
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/terms-of-use/"" class=""selfPromo__boxItem page-privacy-policy"">Condiciones de uso</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/privacy-policy/"" class=""selfPromo__boxItem page-privacy-policy"">Política de privacidad</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/gdpr/"" class=""selfPromo__boxItem page-privacy-policy"">RGPD y periodismo</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/impressum/"" class=""selfPromo__boxItem page-impressum"">Pie de imprenta</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/publicidad/"" class=""selfPromo__boxItem page-advertise"">Publicidad</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/contactar/"" class=""selfPromo__boxItem page-contact"">Contactar</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/mobile/"" class=""selfPromo__boxItem page-mobile"">Móvil</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/livescore/"" class=""selfPromo__boxItem page-live-scores"">Livescore</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/links/"" class=""selfPromo__boxItem page-links"">Sitios recomendados</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/faq/"" class=""selfPromo__boxItem page-faq"">FAQ</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/audio/"" class=""selfPromo__boxItem page-audio"">Audio</a>
                        </div>
                        <div class=""selfPromo__boxItemWrapper"">
                            <a href=""/apuestas/bonos-apuestas/"" class=""selfPromo__boxItem page-betting-offers"">Bonos apuestas</a>
                        </div>
                </div>
            </div>
        </div>
        <div class=""selfPromo__box selfPromo__box--social"">
            <div class=""selfPromo__boxTitle"" >Síguenos</div>
            <div class=""selfPromo__boxContent"">
                    <div class=""selfPromo__boxItemWrapper"">
                        <a href=""https://www.facebook.com/Flashscorecom/"" target=""_blank"" class=""selfPromo__boxItem"">
                            <svg class=""selfPromo__icon"">
                                <use xlink:href=""/res/_fs/image/13_symbols/social.v3.svg#fb""></use>
                            </svg>
                            <div class=""selfPromo__linkText"">Facebook</div>
                        </a>
                    </div>
                    <div class=""selfPromo__boxItemWrapper"">
                        <a href=""https://twitter.com/flashscorees"" target=""_blank"" class=""selfPromo__boxItem"">
                            <svg class=""selfPromo__icon"">
                                <use xlink:href=""/res/_fs/image/13_symbols/social.v3.svg#tw""></use>
                            </svg>
                            <div class=""selfPromo__linkText"">X</div>
                        </a>
                    </div>
                    <div class=""selfPromo__boxItemWrapper"">
                        <a href=""https://www.instagram.com/flashscoreofficial/"" target=""_blank"" class=""selfPromo__boxItem"">
                            <svg class=""selfPromo__icon"">
                                <use xlink:href=""/res/_fs/image/13_symbols/social.v3.svg#in""></use>
                            </svg>
                            <div class=""selfPromo__linkText"">Instagram</div>
                        </a>
                    </div>
                    <div class=""selfPromo__boxItemWrapper"">
                        <a href=""https://tiktok.com/@flashscorecom"" target=""_blank"" class=""selfPromo__boxItem"">
                            <svg class=""selfPromo__icon"">
                                <use xlink:href=""/res/_fs/image/13_symbols/social.v3.svg#tiktok""></use>
                            </svg>
                            <div class=""selfPromo__linkText"">TikTok</div>
                        </a>
                    </div>
            </div>
        </div>
        <div class=""selfPromo__box selfPromo__box--apps"">
            <div class=""selfPromo__wrapper--texts"">
                <div class=""selfPromo__boxTitle"">Aplicaciones móviles</div>
                <div class=""selfPromo__boxContent"">
                    Nuestra app móvil está optimizada para tu teléfono. ¡Descárgala gratis!
                </div>
            </div>
            <div class=""selfPromo__wrapper--stores"">
                <a href=""https://apps.apple.com/es/app/id766443283?l=es&amp;mt=8"" target=""_blank""
                                                              title=""Aplicación para iPhone/iPad"" class=""selfPromo__app selfPromo__app--ios"">
                    <img class=""selfPromo__appImage"" src=""https://static.flashscore.com/res/_fs/image/9_stores/apple/es.svg"" height=""37"" alt=""App Store"" loading=""lazy"">
                </a>
                <a href=""https://play.google.com/store/apps/details?id=eu.livesport.FlashScore_com"" target=""_blank""
                                                       title=""Aplicación para Android"" class=""selfPromo__app selfPromo__app--ios"">
                    <img class=""selfPromo__appImage"" src=""https://static.flashscore.com/res/_fs/image/9_stores/google/es.svg"" height=""37"" alt=""App Store"" loading=""lazy"">
                </a>
                <a href=""https://appgallery.huawei.com/app/C101497479?sharePrepath=ag"" target=""_blank""
                                                      title="""" class=""selfPromo__app selfPromo__app--ios"">
                    <img class=""selfPromo__appImage"" src=""https://static.flashscore.com/res/_fs/image/9_stores/huawei/es.svg"" height=""37"" alt=""App Store"" loading=""lazy"">
                </a>
            </div>
        </div>
    </div>
</div>
<div class=""footer"">
    <div class=""footer__content"">
        <div class=""footer__alternatives footer__alternatives--hidden"" id=""mobi_version"">
            <a href=""http://m.flashscore.es/"" class=""footer__link"">Versión Lite</a>
        </div>
        <div class=""footer__legal"" id=""legal_age_confirmation"">
            <a href=""#[legal-age]"" class=""footer__link"">
              Versión cuotas y apuestas +18
            </a>
        </div>
        <div class=""footer__advert"">
            <div class=""footer__advertBackground""></div>
            <div class=""footerAdvertGambling footer__advertGambling"">

    <div class=""footer__advertGambling--container footer__advertGambling--spain"">
        <div class=""footer__advertGamblingLogos"">
            <a class=""footer__advertGamblingLogo footer__advertGamblingLogo--juegoSeguro""
            href=""https://www.ordenacionjuego.es/participantes-juego/juego-seguro"" target=""_blank""></a>
            <a class=""footer__advertGamblingLogo footer__advertGamblingLogo--autoProhibicion""
            href=""https://www.ordenacionjuego.es/participantes-juego/juego-seguro/rgiaj"" target=""_blank""></a>
        </div>
        <div class=""footer__gambleResponsiblyLink""><a href=""https://www.ordenacionjuego.es/en/participantes-juego/juego-seguro"" target=""_blank"">La ludopatía es un riesgo del juego.</a> +18</div>
    </div>


</div>

        </div>
        <div class=""footer__copyright"">
            <div class=""footer__copyrightText"">Copyright &copy; 2006-26 Flashscore.es</div>
        <div class=""footer__privacy footer__privacy--shield privacySettings"">
            <span class=""footer__privacyButton"" onclick=""OneTrust.ToggleInfoDisplay()"">Establecer privacidad</span>
        </div>
        </div>
    </div>
</div>

<script>
    cjs.Api.loader.get(""cjs"").call((cjs) => {{
        cjs.dic.get('LinkHandler').handleLegalAgeConfirmation();
        cjs.dic.get('LinkHandler').handleMobiLink();
    }});
</script>

<script type=""text/javascript"">
    function isSafari() {{
        var userAgent = navigator.userAgent,
            match = userAgent.match(/(opera|chrome|crios|safari|ucbrowser|firefox|msie|trident(?=\/))\/?\s*(\d+)/i) || [],
            browser,
            tem;

        if (/trident/i.test(match[1])) {{
            tem = /\brv[ :]+(\d+)/g.exec(userAgent) || [];
            return false;
        }} else if (match[1] === ""Chrome"") {{
            tem = userAgent.match(/\b(OPR|Edge)\/(\d+)/);
            if (tem && tem[1]) {{
                return false;
            }}
        }}
        if (!browser && match.length) {{
            browser = match[0].replace(/\/.*/, """");
            if (browser.indexOf(""MSIE"") === 0) {{
                return false;
            }}
            if (userAgent.match(""CriOS"")) {{
                return false;
            }}
        }}

        return browser === ""Safari"";
    }}
</script>


<script type=""text/javascript"">
    const isIos = Boolean(/ipad|iphone|ipod/i.test(window.navigator.userAgent));
</script>


<script type=""text/javascript"">
    if (isIos && isSafari()) {{
        const img = new Image(1, 1);
        img.src = ""https://flashscore.onelink.me/BvSv/2p5v0qxi"";
        img.style.position = ""fixed"";
        document.body.appendChild(img);
    }}
</script>
    </div>
        <div class=""footer__block""></div>
        <div class=""footer__lazyLoadImage"" data-background-src=""https://static.flashscore.com/res/_fs/image/3_footer/mobile_screen.png"">
            <a href=""/mobile/"" class=""footer__mobileScreen""></a>
        </div>
</footer>

<script>
    (() => {{
        let legalAgeElement = document.getElementById('legal_age_confirmation');
        if(legalAgeElement){{
            legalAgeElement.style.display = ""none"";
        }}
        const cb = (globalGeoIp) => {{
            const legalAgeConfirmGeoIp = [""ES""];
            const legalAgeConfirmOverlayGeoIp = [];
            if (legalAgeElement &&
                (Array.isArray(legalAgeConfirmGeoIp) && legalAgeConfirmGeoIp.includes(globalGeoIp) ||
                Array.isArray(legalAgeConfirmOverlayGeoIp) && legalAgeConfirmOverlayGeoIp.includes(globalGeoIp))
            ) {{
                legalAgeElement.style.display = ""block"";
            }}

            const recommendedSitesDisabledGeoIps = cjs.Api.config.get('app', 'disabled_pages', 'geoip') || [];
            if (recommendedSitesDisabledGeoIps.includes(globalGeoIp)) {{
                const selfPromoElement = document.querySelector("".selfPromo__boxItem.page-links"");
                if (selfPromoElement) {{
                    selfPromoElement.remove();
                }}
            }}
        }};
        cjs.Api.loader.get(""geoIpResolver"").call(cb);

        // Lazy load images
        const targetElements = document.querySelectorAll('.footer__lazyLoadImage');
        const options = {{
            threshold: 0.5
        }}

        const lazyLoadCallback = (entries, observer) => {{
            entries.forEach(entry => {{
                if (entry.isIntersecting) {{
                    const lazyElement = entry.target;
                    const backgroundImageUrl = lazyElement.getAttribute('data-background-src');
                    const childAnchor = lazyElement.querySelector('.footer__mobileScreen');
                    if (childAnchor) {{
                        childAnchor.style.background = `url(""${{ backgroundImageUrl }}"") no-repeat center/191px 238px`;
                    }}

                    observer.unobserve(lazyElement);
                }}
            }})
        }}

        const observer = new IntersectionObserver(lazyLoadCallback, options);

        targetElements.forEach((element) => {{
            observer.observe(element);
        }})
    }})();
</script>
<sport name=""soccer"" />
<script>
var dataLayer = dataLayer || []; // Google Tag Manager
(function() {{
    var pageInfo = {{""event"":""pageinfo"",""sport"":""soccer"",""type"":""sport_page""}};
    var hash = window.location.hash;
    var hashParams = new URLSearchParams(hash.startsWith('#') ? hash.slice(1) : hash);
    var queryParams = new URLSearchParams(window.location.search);

    if (pageInfo.constructor === Object && Object.entries(pageInfo).length !== 0) {{
        try {{
            pageInfo['theme'] = window.cjs.Api.darkModeLocal.isDarkModeEnabled() ? 'dark' : 'default';
            pageInfo['theme-user'] = window.cjs.Api.darkModeLocal.isUserDefinedTheme();
            pageInfo['theme-browser'] = window.cjs.Api.darkModeLocal.getPreferredDarkModeBasedOnBrowser() ? 'dark' : 'default';
            pageInfo['app_consent_enabled'] = queryParams.get('app_consent_enabled') ?? undefined;
            pageInfo['app_ga_id'] = hashParams.get('app_ga_id') ?? undefined;
        }} catch(e) {{
            console.error(e);
        }};
        try {{
            pageInfo['user_id'] = cjs.Api.localLsid.getIdent();
        }} catch(e) {{
            console.error(e);
        }};

        var channelsConsent = {{
            emailCampaign: '0',
            pushNotification: '0',
            inAppNotification: '0'
        }};

        (async function tryFetchConsent() {{
            try {{
                var accountManagement = await new Promise(function(resolve) {{
                    cjs.Api.loader.get('accountManagement').call(resolve);
                }});

                if (accountManagement && typeof accountManagement.getMarketingConsent === 'function') {{
                    var consent = await accountManagement.getMarketingConsent();
                    if (consent && consent.channels) {{
                        channelsConsent.emailCampaign = consent.channels.emailCampaign ? '1' : '0';
                        channelsConsent.pushNotification = consent.channels.pushNotification ? '1' : '0';
                        channelsConsent.inAppNotification = consent.channels.inAppNotification ? '1' : '0';
                    }}
                }}
            }} catch(e) {{
                console.error('Failed to fetch marketing consent:', e);
            }} finally {{
                pageInfo['mc_consent'] = 'em_' + channelsConsent.emailCampaign + '|pn_' + channelsConsent.pushNotification + '|ia_' + channelsConsent.inAppNotification;
                dataLayer.push(pageInfo);
            }}
        }})();
    }}

    try {{
        cjs.Api.loader.get(""myGames"").call(function(mg) {{
            cjs.Api.loader.get(""myTeams"").call(function(mt) {{
                dataLayer.push({{'event': 'userMy', 'games': mg.getCount(), 'teams': mt.getCount()}});
            }});
        }});
    }} catch(e) {{ }};

    // window.hasAdBlock is used in BI department (don't remove)
    const googleAdsRequest = new Request(""https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js"", {{ method: ""HEAD"", mode: ""no-cors"" }});
    fetch(googleAdsRequest).then(() => window.hasAdBlock = ""false"").catch(() => window.hasAdBlock = ""true"");
}}());
</script>

<!-- Google Tag Manager -->
<noscript><iframe src=""//www.googletagmanager.com/ns.html?id=GTM-PWJ3NQ"" height=""0"" width=""0"" style=""display:none;visibility:hidden""></iframe></noscript>
<script>(function(w,d,s,l,i){{w[l]=w[l]||[];w[l].push({{'gtm.start': new Date().getTime(),event:'gtm.js'}});var f=d.getElementsByTagName(s)[0],j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src='//www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);}})(window,document,'script','dataLayer','GTM-PWJ3NQ');</script>
<!-- End Google Tag Manager -->
<script>
    window.bannerHandlerSettings = {{
        'options': {{""api"":{{""host"":""https:\/\/content.livesportmedia.eu"",""connectionTimeout"":1000,""connectionRetryCount"":2}},""containerIdPattern"":""zoneContainer-{{zoneName}}"",""labelText"":""Anuncio"",""consentOptions"":{{""timeout"":3000,""programmaticStrategy"":""wait_for_consent"",""bettingStrategy"":""wait_for_consent"",""requiredConsentGroups"":[1,2,3,4,5,6,7,8,9,10]}},""projectData"":{{""id"":13,""url"":""https:\/\/www.flashscore.es\/""}},""deviceParams"":{{""platform"":""web"",""version"":""8.22.0"",""package"":""Flashscore.es""}},""replaceZoneTimeout"":200,""registerZoneTimeout"":400,""debugMode"":false,""sandboxMode"":false}},
        'zones': {{""background"":{{""name"":""background"",""definitions"":[{{""zoneId"":1061,""size"":{{""width"":1920,""height"":1200}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""renderer"":""wallpaper""}},""left_menu_1"":{{""name"":""left_menu_1"",""definitions"":[{{""zoneId"":87,""size"":{{""width"":140,""height"":240}},""breakpoint"":{{""min"":640,""max"":9999}}}}]}},""left_menu_2"":{{""name"":""left_menu_2"",""definitions"":[{{""zoneId"":204,""size"":{{""width"":140,""height"":240}},""breakpoint"":{{""min"":640,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""left_menu_3"":{{""name"":""left_menu_3"",""definitions"":[{{""zoneId"":3564,""size"":{{""width"":140,""height"":240}},""breakpoint"":{{""min"":640,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""right_top"":{{""name"":""right_top"",""definitions"":[{{""zoneId"":6574,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""right_zone_1"":{{""name"":""right_zone_1"",""definitions"":[{{""zoneId"":6575,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""right_zone_2"":{{""name"":""right_zone_2"",""definitions"":[{{""zoneId"":6576,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""top"":{{""name"":""top"",""definitions"":[{{""zoneId"":86,""size"":{{""width"":970,""height"":90}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""labelPosition"":""Right"",""displayPlaceholder"":true}}}},""content_bottom"":{{""name"":""content_bottom"",""definitions"":[{{""zoneId"":127,""size"":{{""width"":480,""height"":480}},""breakpoint"":{{""min"":1,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_top"":{{""name"":""detail_top"",""definitions"":[{{""zoneId"":5006,""size"":{{""width"":970,""height"":90}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""labelPosition"":""Right"",""displayPlaceholder"":true}}}},""detail_content"":{{""name"":""detail_content"",""definitions"":[{{""zoneId"":88,""size"":{{""width"":480,""height"":480}},""breakpoint"":{{""min"":1,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_background"":{{""name"":""detail_background"",""definitions"":[{{""zoneId"":15985,""size"":{{""width"":3000,""height"":2000}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""renderer"":""wallpaper""}},""detail_left_menu_1"":{{""name"":""detail_left_menu_1"",""definitions"":[{{""zoneId"":15989,""size"":{{""width"":140,""height"":240}},""breakpoint"":{{""min"":640,""max"":9999}}}}]}},""detail_left_menu_2"":{{""name"":""detail_left_menu_2"",""definitions"":[{{""zoneId"":15991,""size"":{{""width"":140,""height"":240}},""breakpoint"":{{""min"":640,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_left_menu_3"":{{""name"":""detail_left_menu_3"",""definitions"":[{{""zoneId"":15993,""size"":{{""width"":140,""height"":240}},""breakpoint"":{{""min"":640,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_right_top"":{{""name"":""detail_right_top"",""definitions"":[{{""zoneId"":15995,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_right_zone_1"":{{""name"":""detail_right_zone_1"",""definitions"":[{{""zoneId"":15997,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_right_zone_2"":{{""name"":""detail_right_zone_2"",""definitions"":[{{""zoneId"":15999,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_right_zone_3"":{{""name"":""detail_right_zone_3"",""definitions"":[{{""zoneId"":16001,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_right_zone_4"":{{""name"":""detail_right_zone_4"",""definitions"":[{{""zoneId"":16003,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""detail_box_over_content"":{{""name"":""detail_box_over_content"",""definitions"":[{{""zoneId"":15987,""size"":{{""width"":688,""height"":85}},""breakpoint"":{{""min"":728,""max"":9999}},""rendererOptions"":{{""sticky"":true}}}},{{""zoneId"":16005,""size"":{{""width"":320,""height"":100}},""breakpoint"":{{""min"":320,""max"":727}}}}]}},""responsive_standings_fixed_bottom"":{{""name"":""responsive_standings_fixed_bottom"",""definitions"":[{{""zoneId"":3530,""size"":{{""width"":320,""height"":50}},""breakpoint"":{{""min"":320,""max"":727}},""refreshInterval"":45}},{{""zoneId"":3531,""size"":{{""width"":728,""height"":90}},""breakpoint"":{{""min"":728,""max"":999}},""refreshInterval"":45}}]}},""responsive_fixed_bottom"":{{""name"":""responsive_fixed_bottom"",""definitions"":[{{""zoneId"":3528,""size"":{{""width"":320,""height"":50}},""breakpoint"":{{""min"":320,""max"":727}},""refreshInterval"":45}},{{""zoneId"":3529,""size"":{{""width"":728,""height"":90}},""breakpoint"":{{""min"":728,""max"":999}},""refreshInterval"":45}}]}},""responsive_detail_fixed_bottom"":{{""name"":""responsive_detail_fixed_bottom"",""definitions"":[{{""zoneId"":3530,""size"":{{""width"":320,""height"":50}},""breakpoint"":{{""min"":320,""max"":727}},""refreshInterval"":45,""allowedClientTypes"":[""mobile"",""tablet""]}},{{""zoneId"":3531,""size"":{{""width"":728,""height"":90}},""breakpoint"":{{""min"":728,""max"":9999}},""refreshInterval"":45,""allowedClientTypes"":[""mobile"",""tablet""]}}]}},""premium_square_mobile"":{{""name"":""premium_square_mobile"",""definitions"":[{{""zoneId"":6120,""size"":{{""width"":480,""height"":480}},""breakpoint"":{{""min"":300,""max"":639}}}}],""renderer"":""dynamic""}},""fsnews_right_zone_1"":{{""name"":""fsnews_right_zone_1"",""definitions"":[{{""zoneId"":5693,""size"":{{""width"":300,""height"":250}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false,""displayPlaceholder"":true}}}},""fsnews_right_zone_2"":{{""name"":""fsnews_right_zone_2"",""definitions"":[{{""zoneId"":5694,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""fsnews_right_zone_3"":{{""name"":""fsnews_right_zone_3"",""definitions"":[{{""zoneId"":5695,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""fsnews_content_bottom"":{{""name"":""fsnews_content_bottom"",""definitions"":[{{""zoneId"":5692,""size"":{{""width"":480,""height"":480}},""breakpoint"":{{""min"":1,""max"":9999}}}}]}},""fsnews_top"":{{""name"":""fsnews_top"",""definitions"":[{{""zoneId"":5696,""size"":{{""width"":970,""height"":90}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""labelPosition"":""Right"",""displayPlaceholder"":true}}}},""fsnews_responsive_fixed_bottom"":{{""name"":""fsnews_responsive_fixed_bottom"",""definitions"":[{{""zoneId"":5699,""size"":{{""width"":320,""height"":50}},""breakpoint"":{{""min"":320,""max"":727}},""refreshInterval"":45}},{{""zoneId"":5701,""size"":{{""width"":728,""height"":90}},""breakpoint"":{{""min"":728,""max"":999}},""refreshInterval"":45}}]}},""fsnews_background"":{{""name"":""fsnews_background"",""definitions"":[{{""zoneId"":5697,""size"":{{""width"":3000,""height"":2000}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""renderer"":""wallpaper""}},""fsnews_content_bottom_detail"":{{""name"":""fsnews_content_bottom_detail"",""definitions"":[{{""zoneId"":5687,""size"":{{""width"":480,""height"":480}},""breakpoint"":{{""min"":1,""max"":9999}}}}]}},""fsnews_responsive_fixed_bottom_detail"":{{""name"":""fsnews_responsive_fixed_bottom_detail"",""definitions"":[{{""zoneId"":5698,""size"":{{""width"":320,""height"":50}},""breakpoint"":{{""min"":320,""max"":727}},""refreshInterval"":45}},{{""zoneId"":5700,""size"":{{""width"":728,""height"":90}},""breakpoint"":{{""min"":728,""max"":999}},""refreshInterval"":45}}]}},""fsnews_top_detail"":{{""name"":""fsnews_top_detail"",""definitions"":[{{""zoneId"":5691,""size"":{{""width"":970,""height"":90}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""labelPosition"":""Right"",""displayPlaceholder"":true}}}},""fsnews_right_zone_1_detail"":{{""name"":""fsnews_right_zone_1_detail"",""definitions"":[{{""zoneId"":5688,""size"":{{""width"":300,""height"":250}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false,""displayPlaceholder"":true}}}},""fsnews_right_zone_2_detail"":{{""name"":""fsnews_right_zone_2_detail"",""definitions"":[{{""zoneId"":5689,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""fsnews_right_zone_3_detail"":{{""name"":""fsnews_right_zone_3_detail"",""definitions"":[{{""zoneId"":5690,""size"":{{""width"":300,""height"":600}},""breakpoint"":{{""min"":1048,""max"":9999}}}}],""rendererOptions"":{{""displaySkeleton"":false}}}},""fsnews_article"":{{""name"":""fsnews_article"",""definitions"":[{{""zoneId"":6180,""size"":{{""width"":720,""height"":1280}},""breakpoint"":{{""min"":320,""max"":639}}}}]}},""box_over_content"":{{""name"":""box_over_content"",""definitions"":[{{""zoneId"":9465,""size"":{{""width"":688,""height"":85}},""breakpoint"":{{""min"":728,""max"":9999}}}},{{""zoneId"":9467,""size"":{{""width"":320,""height"":100}},""breakpoint"":{{""min"":320,""max"":727}}}}],""rendererOptions"":{{""sticky"":true}}}}}},
    }};
</script>
<span id=""mlc-4ck3s9wd8c""></span>
<span id=""mlc-aks81bkdz""></span>
    <script>
        if ('serviceWorker' in navigator) {{
          window.addEventListener('load', () => {{
            navigator.serviceWorker.register(""/sw.js"")
              .then(registration => console.log('SW registered:', registration))
              .catch(error => console.log('SW registration failed:', error));
          }});
        }}
    </script></body>
</html>
";

            try
            {
                webView.CoreWebView2.NavigateToString(styledHtml);
                System.Diagnostics.Debug.WriteLine("[App] ¡Fragmento enviado al WebView!");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Error crítico al mostrar HTML: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task IsolateMatchElement()
        {
            string script = @"
                (function() {
                    // Ocultar todo el body
                    document.body.style.margin = '0';
                    document.body.style.padding = '0';
                    document.body.style.overflow = 'hidden';
                    
                    // Buscar el elemento específico del partido
                    const matchElement = document.querySelector('.event__match.fc-processed');
                    
                    if (matchElement) {
                        // Ocultar todo
                        const allElements = document.body.children;
                        for (let i = 0; i < allElements.length; i++) {
                            allElements[i].style.display = 'none';
                        }
                        
                        // Crear un contenedor limpio
                        const container = document.createElement('div');
                        container.style.width = '100%';
                        container.style.height = '100vh';
                        container.style.display = 'flex';
                        container.style.alignItems = 'center';
                        container.style.justifyContent = 'center';
                        container.style.backgroundColor = '#000000';
                        container.style.margin = '0';
                        container.style.padding = '20px';
                        container.style.boxSizing = 'border-box';
                        
                        // Clonar el elemento del partido
                        const clonedMatch = matchElement.cloneNode(true);
                        clonedMatch.style.width = '100%';
                        clonedMatch.style.maxWidth = '800px';
                        
                        // Agregar al contenedor
                        container.appendChild(clonedMatch);
                        document.body.appendChild(container);
                        
                        // Hacer visible solo este contenedor
                        container.style.display = 'flex';
                    }
                })();
            ";

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al ejecutar script: {ex.Message}");
            }
        }

        private void StartHttpListener()
        {
            int[] portsToTry = { 9876 };

            foreach (int port in portsToTry)
            {
                try
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add($"http://localhost:{port}/");
                    listener.Start();

                    currentPort = port;
                    System.Diagnostics.Debug.WriteLine($"Servidor iniciado en puerto {port}");

                    listenerThread = new Thread(() =>
                    {
                        while (listener.IsListening)
                        {
                            try
                            {
                                var context = listener.GetContext();
                                var request = context.Request;

                                if (request.Url.AbsolutePath == "/show")
                                {
                                    System.Diagnostics.Debug.WriteLine($"Recibida petición {request.HttpMethod} en /show (User-Agent: {request.UserAgent})");
                                    string divHtmlReceived = null;
                                    
                                    if (request.HttpMethod == "POST")
                                    {
                                        using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                                        {
                                            divHtmlReceived = reader.ReadToEnd();
                                            System.Diagnostics.Debug.WriteLine($"[Http] HTML del div recibido. Longitud: {divHtmlReceived?.Length ?? 0}");
                                        }
                                    }

                                    Application.Current.Dispatcher.Invoke(async () =>
                                    {
                                        await navigationLock.WaitAsync();
                                        try
                                        {
                                            this.Visibility = Visibility.Visible;
                                            this.Show();
                                            this.WindowState = WindowState.Normal;
                                            this.Topmost = true;
                                            this.Activate();

                                            // Si se recibió HTML del div, prepararlo para inyección
                                            if (!string.IsNullOrEmpty(divHtmlReceived))
                                            {
                                                System.Diagnostics.Debug.WriteLine("[App] Preparando HTML para inyección automática...");
                                                pendingDivHtml = divHtmlReceived;
                                                shouldInjectOnLoad = true;
                                            }
                                            else
                                            {
                                                pendingDivHtml = null;
                                                shouldInjectOnLoad = false;
                                            }

                                            // Siempre cargar la URL de Flashscore
                                            // El evento NavigationCompleted se encargará de inyectar el HTML cuando cargue
                                            await NavigateToFlashscore();

                                            BringToForeground();
                                        }
                                        finally
                                        {
                                            navigationLock.Release();
                                        }
                                    });

                                    var response = context.Response;
                                    response.Headers.Add("Access-Control-Allow-Origin", "*");
                                    string responseString = "OK";
                                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                                    response.ContentLength64 = buffer.Length;
                                    response.OutputStream.Write(buffer, 0, buffer.Length);
                                    response.Close();
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error en listener: {ex.Message}");
                            }
                        }
                    });

                    listenerThread.IsBackground = true;
                    listenerThread.Start();

                    return;
                }
                catch (HttpListenerException)
                {
                    listener?.Close();
                    listener = null;
                    continue;
                }
            }

        }

        private void BringToForeground()
        {
            if (!this.IsVisible)
            {
                this.Show();
            }

            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }

            this.Topmost = true;
            this.Activate();
            this.Topmost = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch { }

            base.OnClosed(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            this.Visibility = Visibility.Hidden;
        }
    }
}