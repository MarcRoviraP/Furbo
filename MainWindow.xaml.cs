using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Text;
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
        private bool isWebViewLoaded = false;
        private int currentPort = 0;
        private readonly System.Threading.SemaphoreSlim navigationLock = new System.Threading.SemaphoreSlim(1, 1);
        ArrayList ids = new ArrayList();

        public MainWindow()
        {
            InitializeComponent();
            StartHttpListener();

            this.ShowInTaskbar = false;
            this.Visibility = Visibility.Hidden;

            this.Topmost = true;
            
        }

        async Task setupWebView2Async()
        {
            await webView.EnsureCoreWebView2Async();
            webView.DefaultBackgroundColor = Color.Black;
            webView.CoreWebView2.Settings.IsScriptEnabled = true;

        }
        private bool isPageReady = false;

        private async void setupWebView2()
        {
            await setupWebView2Async();

            webView.CoreWebView2.NavigationCompleted += async (s, e) =>
            {
                if (!e.IsSuccess) return;

                isPageReady = true;  // ← ahora sí está lista
                await ejecutarScript();
                webView.Visibility = Visibility.Visible;
            };

            webView.CoreWebView2.Navigate("https://flashscore.es");
        }

        private void StartHttpListener()
        {
            int port =  9876 ;


            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();

                currentPort = port;
                System.Diagnostics.Debug.WriteLine($"[Http] Servidor iniciado en puerto {port}");

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
                                System.Diagnostics.Debug.WriteLine($"[Http] ← Petición recibida");

                                string id = "";

                                if (request.HttpMethod == "POST")
                                {
                                    using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                                    {
                                        string body = reader.ReadToEnd();

                                        if (body.TrimStart().StartsWith("{"))
                                        {
                                            try
                                            {
                                                var jsonDoc = System.Text.Json.JsonDocument.Parse(body);
                                                var root = jsonDoc.RootElement;

                                if (root.TryGetProperty("id", out var idValido))
                                                {
                                                    id = idValido.ToString();
                                                    
                                                    // Verificar si el ID ya existe (evitar duplicados)
                                                    if (!ids.Contains(id))
                                                    {
                                                        ids.Add(id);
                                                        Debug.WriteLine($"[Http] ✓ ID añadido: {id} (Total: {ids.Count})");
                                                    }
                                                    else
                                                    {
                                                        Debug.WriteLine($"[Http] ⚠ ID duplicado ignorado: {id}");
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"[Http] ✗ Error JSON: {ex.Message}");
                                            }
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(id))
                                {
                                    Application.Current.Dispatcher.Invoke(async () =>
                                    {
                                        await navigationLock.WaitAsync();
                                        try
                                        {
                                            // Si el WebView ya está cargado, solo ejecutar el script
                                            if (isWebViewLoaded && isPageReady)
                                            {
                                                // Página ya cargada, ejecutar script directamente
                                                await ejecutarScript();
                                            }
                                            else if (!isWebViewLoaded)
                                            {
                                                // Primera vez: inicializar y cargar
                                                isWebViewLoaded = true;
                                                setupWebView2();
                                                // No ejecutar script aquí, lo hará NavigationCompleted cuando termine
                                            }

                                            System.Diagnostics.Debug.WriteLine("[App] → Mostrando ventana...");

                                            this.Visibility = Visibility.Visible;
                                            this.Show();
                                            this.WindowState = WindowState.Normal;
                                            this.Topmost = true;
                                            this.Activate();

                                            Title = "Furbo - Cargando partido...";


                                            Title = "Furbo - Vista de Partido";

                                            BringToForeground();
                                        }
                                        finally
                                        {
                                            navigationLock.Release();
                                        }
                                    });
                                }

                                // Responder al cliente
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
                            System.Diagnostics.Debug.WriteLine($"[Http] Error en listener: {ex.Message}");
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
                }
            }

        private async Task ejecutarScript()
        {
            string idsArray = string.Join(", ", ids.Cast<string>().Select(id => $"\"{id}\""));

            string script = $@"
(function() {{
    const ids = [{idsArray}];

    // Ocultar todo
    const all = document.querySelectorAll('body *');
    all.forEach(el => {{ el.style.display = 'none'; }});

    function showElementAndParents(id) {{
        const element = document.getElementById(id);
        if (!element) return false;

        let current = element;
        while (current) {{
            current.style.display = '';
            current = current.parentElement;
        }}

        element.querySelectorAll('*').forEach(child => {{
            child.style.display = '';
        }});

        return true;
    }}

    function waitAndShow(id, attempts) {{
        if (attempts <= 0) {{
            console.warn('Elemento no encontrado tras esperar: ' + id);
            return;
        }}
        if (!showElementAndParents(id)) {{
            setTimeout(() => waitAndShow(id, attempts - 1), 300);
        }} else {{
            console.log('Elemento mostrado: ' + id);
        }}
    }}

    ids.forEach(id => waitAndShow(id, 20));
}})();
";

            await webView.CoreWebView2.ExecuteScriptAsync(script)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.WriteLine($"Error al ejecutar script: {t.Exception?.GetBaseException().Message}");
                    }
                    else
                    {
                        Debug.WriteLine("Script ejecutado correctamente");
                        Debug.WriteLine($"Resultado: {t.Result}");
                    }
                });

            Debug.WriteLine("Script ejecutado");
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