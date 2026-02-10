using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
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

                    // Solo aplicar aislamiento si estamos en la web de flashscore
                    if (webView.Source.ToString().Contains("flashscore.es"))
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

        async System.Threading.Tasks.Task NavigateToFlashscore()
        {
            isShowingSnippet = false;
            await SetupWebView2Async();

            // Habilitar scripts para la web original
            webView.CoreWebView2.Settings.IsScriptEnabled = true;

            webView.CoreWebView2.Navigate("https://www.flashscore.es/");
        }

        private async System.Threading.Tasks.Task ShowHtmlSnippet(string html)
        {
            System.Diagnostics.Debug.WriteLine("[App] Preparando para mostrar fragmento HTML...");
            isShowingSnippet = true;
            await SetupWebView2Async();

            // Detener cualquier navegación actual antes de inyectar el fragmento
            webView.CoreWebView2.Stop();

            // Deshabilitar scripts para evitar que el fragmento intente recargar o redirigir
            webView.CoreWebView2.Settings.IsScriptEnabled = false;
            System.Diagnostics.Debug.WriteLine("[App] Scripts deshabilitados, inyectando HTML...");

            // CSS básico para que el div se vea bien sobre fondo negro
            string styledHtml = $@"
                <!DOCTYPE html>
                <html>
                <head>        <link rel=""stylesheet"" href=""https://static.flashscore.com/res/_fs/build/LivesportFinderLatin.b5b9ae1.css"">
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

                </head>
                <body>
                    {html}
                </body>
                </html>";

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
                                    string htmlReceived = null;
                                    if (request.HttpMethod == "POST")
                                    {
                                        using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                                        {
                                            htmlReceived = reader.ReadToEnd();
                                            System.Diagnostics.Debug.WriteLine($"[Http] HTML recibido. Longitud: {htmlReceived?.Length ?? 0}");
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

                                            if (!string.IsNullOrEmpty(htmlReceived))
                                            {
                                                await ShowHtmlSnippet(htmlReceived);
                                            }
                                            else
                                            {
                                                await NavigateToFlashscore();
                                            }

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