using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

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

        private async void setupWebView2()
        {
            await setupWebView2Async();

            webView.CoreWebView2.NavigationCompleted += async (s, e) =>
            {
                if (!e.IsSuccess) return;
                string script = $@"
(function() {{
    var matchId = ""{ids[0]}"";
    var elementoOriginal = document.getElementById(matchId);
    
    if (!elementoOriginal) {{
        console.error(""Partido no encontrado: "" + matchId);
        return;
    }}

    // Obtener el contenedor de la liga
    var ligaOriginal = elementoOriginal.closest("".headerLeague__wrapper, section"") 
                      || elementoOriginal.parentElement.parentElement;
    
    // CLONAR el contenedor
    var ligaClon = ligaOriginal.cloneNode(true);
    
    // Limpiar otros partidos del clon
    ligaClon.querySelectorAll('[id^=""g_1_""]').forEach(function(p) {{
        if (p.id !== matchId) p.remove();
    }});

    // Crear overlay
    var overlay = document.createElement(""div"");
    overlay.id = ""mi-overlay"";
    overlay.style.cssText =
        ""position: fixed;"" +
        ""top: 0; left: 0;"" +
        ""width: 100vw; height: 100vh;"" +
        ""background: #001e28;"" +
        ""z-index: 999999;"" +
        ""overflow: auto;"" +
        ""padding: 20px;"";

    overlay.appendChild(ligaClon);

    // Ocultar el resto del contenido
    document.body.childNodes.forEach(function(nodo) {{
        if (nodo.nodeType === 1 && nodo.id !== ""mi-overlay"") {{
            nodo.style.display = ""none"";
            nodo.setAttribute(""data-oculto"", ""true"");
        }}
    }});

    document.body.appendChild(overlay);

    // SINCRONIZAR: Observar cambios en el elemento ORIGINAL y copiarlos al CLON
    var elementoClon = ligaClon.querySelector('#' + matchId);
    
    var syncObserver = new MutationObserver(function(mutations) {{
        mutations.forEach(function(mutation) {{
            // Copiar cambios de atributos
            if (mutation.type === 'attributes') {{
                var attrName = mutation.attributeName;
                var newValue = elementoOriginal.getAttribute(attrName);
                if (elementoClon.getAttribute(attrName) !== newValue) {{
                    if (newValue === null) {{
                        elementoClon.removeAttribute(attrName);
                    }} else {{
                        elementoClon.setAttribute(attrName, newValue);
                    }}
                }}
            }}
            
            // Copiar cambios en el contenido (texto, HTML interno)
            if (mutation.type === 'childList' || mutation.type === 'characterData') {{
                elementoClon.innerHTML = elementoOriginal.innerHTML;
            }}
        }});
    }});

    // Observar TODO en el elemento original
    syncObserver.observe(elementoOriginal, {{
        attributes: true,
        childList: true,
        subtree: true,
        characterData: true,
        attributeOldValue: true
    }});

    // También sincronizar cambios en subelementos específicos (marcadores, tiempo, etc.)
    var deepSyncObserver = new MutationObserver(function() {{
        elementoClon.innerHTML = elementoOriginal.innerHTML;
    }});

    deepSyncObserver.observe(elementoOriginal, {{
        childList: true,
        subtree: true,
        characterData: true
    }});

    console.log(""✓ Furbo: Partido "" + matchId + "" sincronizado en tiempo real"");
}})();
";

                await webView.CoreWebView2.ExecuteScriptAsync(script);
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
                                                    ids.Add(id);
                                                    Console.WriteLine($"[Http] ✓ ID: {id}");
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
                                            setupWebView2();
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