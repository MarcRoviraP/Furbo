using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Threading.Tasks;
using System.Linq;

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
            Debug.WriteLine("[WebView2] setupWebView2Async - inicio: intentando EnsureCoreWebView2Async()");
            await webView.EnsureCoreWebView2Async();
            Debug.WriteLine("[WebView2] setupWebView2Async - EnsureCoreWebView2Async() completado");
            webView.DefaultBackgroundColor = Color.Black;
            webView.CoreWebView2.Settings.IsScriptEnabled = true;
            Debug.WriteLine("[WebView2] setupWebView2Async - configuraciones aplicadas");
        }

        private async Task setupWebView2()
        {
            Debug.WriteLine("[WebView2] setupWebView2 - inicio");
            await setupWebView2Async();
            Debug.WriteLine("[WebView2] setupWebView2 - setupWebView2Async finalizado, suscribiendo NavigationCompleted");

            webView.CoreWebView2.NavigationCompleted += async (s, e) =>
            {
                Debug.WriteLine($"[WebView2] NavigationCompleted IsSuccess={e.IsSuccess}");
                if (e.IsSuccess)
                {
                    // Inyectar el script helper que controla el DOM
                    string earlyScript = @"
window.__furboHiddenElements = new Set();
window.__furboApplyStyles = function(ids) {
    // Ocultar todo
    const all = document.querySelectorAll('body *');
    all.forEach(el => { 
        el.style.display = 'none'; 
        window.__furboHiddenElements.add(el);
    });

    // Mostrar solo los elementos específicos y sus padres
    function showElementAndParents(id) {
        const element = document.getElementById(id);
        if (!element) return false;

        let current = element;
        while (current) {
            current.style.display = '';
            window.__furboHiddenElements.delete(current);
            current = current.parentElement;
        }

        element.querySelectorAll('*').forEach(child => {
            child.style.display = '';
            window.__furboHiddenElements.delete(child);
        });

        return true;
    }

    ids.forEach(id => showElementAndParents(id));
};

// Prevenir que nuevos elementos mostrados se oculten automáticamente
if (!window.__furboObserverInitialized) {
    window.__furboObserverInitialized = true;
    const observer = new MutationObserver((mutations) => {
        mutations.forEach((mutation) => {
            if (mutation.type === 'attributes' && mutation.attributeName === 'style') {
                const el = mutation.target;
                // Si el elemento fue mostrado por nosotros, mantenerlo visible
                if (!window.__furboHiddenElements.has(el) && el.style.display === 'none') {
                    el.style.display = '';
                }
            }
            // Si se agregan nuevos nodos, asegurarse que no se muestren si deben estar ocultos
            if (mutation.type === 'childList') {
                mutation.addedNodes.forEach((node) => {
                    if (node.nodeType === 1 && !window.__furboHiddenElements.has(node)) {
                        node.style.display = 'none';
                        window.__furboHiddenElements.add(node);
                    }
                });
            }
        });
    });

    observer.observe(document.body, {
        attributes: true,
        attributeFilter: ['style'],
        subtree: true,
        childList: true
    });
}
";
                    try
                    {
                        await webView.CoreWebView2.ExecuteScriptAsync(earlyScript);
                        Debug.WriteLine("[WebView2] Script helper inyectado correctamente");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WebView2] Error al inyectar script helper: {ex.Message}");
                    }

                    isWebViewLoaded = true;
                    Debug.WriteLine("[WebView2] isWebViewLoaded = true");
                }
                else
                {
                    Debug.WriteLine("[WebView2] NavigationCompleted - fallo al navegar");
                }
            };

            Debug.WriteLine("[WebView2] Navegando a https://flashscore.es");
            webView.CoreWebView2.Navigate("https://flashscore.es");
        }

        private void StartHttpListener()
        {
            int port =  9876 ;
            Debug.WriteLine($"[Http] StartHttpListener - inicio en puerto {port}");
            isWebViewLoaded = false;
            Debug.WriteLine("[Http] StartHttpListener - isWebViewLoaded marcado false (inicial)");
            _ = setupWebView2();
            Debug.WriteLine("[Http] StartHttpListener - setupWebView2 llamado (no await)");
            mostrarPrograma();

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
                                System.Diagnostics.Debug.WriteLine($"[Http] ← Petición recibida {request.HttpMethod} {request.Url}");

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
                                                    Debug.WriteLine($"[Http] ID parseado: {id}");
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
                                        Debug.WriteLine("[Http] Dispatcher.Invoke - procesando id");
                                        await navigationLock.WaitAsync();
                                        try
                                        {
                                            Debug.WriteLine($"[Http] navigationLock adquirido, isWebViewLoaded={isWebViewLoaded}");
                                            if (isWebViewLoaded)
                                            {
                                                Debug.WriteLine("[Http] WebView marcado como cargado, llamando ejecutarScript()");
                                                await ejecutarScript();
                                            }
                                            else
                                            {
                                                Debug.WriteLine("[Http] WebView no cargado todavía, no se ejecuta el script ahora");
                                            }
                                            mostrarPrograma();
                                            
                                        }
                                        finally
                                        {
                                            navigationLock.Release();
                                            Debug.WriteLine("[Http] navigationLock liberado");
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
        private void mostrarPrograma()
        {
            System.Diagnostics.Debug.WriteLine("[App] → Mostrando ventana...");

            this.Visibility = Visibility.Visible;
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Topmost = true;
            this.Activate();


            BringToForeground();
        }
        private async Task ejecutarScript()
        {

            Debug.WriteLine("[Script] ejecutarScript - inicio");

            if (webView?.CoreWebView2 == null)
            {
                Debug.WriteLine("[Script] CoreWebView2 == null, llamando EnsureCoreWebView2Async() antes de ejecutar script");
                try
                {
                    await webView.EnsureCoreWebView2Async();
                    Debug.WriteLine("[Script] EnsureCoreWebView2Async() completado");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Script] Error al inicializar CoreWebView2: {ex.GetBaseException().Message}");
                    return;
                }

                if (webView.CoreWebView2 == null)
                {
                    Debug.WriteLine("[Script] CoreWebView2 sigue siendo null tras EnsureCoreWebView2Async(), abortando ejecución");
                    return;
                }
            }

            try
            {
                Debug.WriteLine("[Script] Ejecutando ExecuteScriptAsync...");
                var result = await webView.CoreWebView2.ExecuteScriptAsync(cargarScript());
                Debug.WriteLine("[Script] Script ejecutado correctamente");
                Debug.WriteLine($"[Script] Resultado: {result}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Script] Error al ejecutar script: {ex.GetBaseException().Message}");
            }

            Debug.WriteLine("[Script] ejecutarScript - fin");
        }

        private string cargarScript()
        {
            string idsArray = string.Join(", ", ids.Cast<string>().Select(id => $"\"{id}\""));

            string script = $@"
(function() {{
    const ids = [{idsArray}];

    // Esperar a que el documento esté listo
    if (document.readyState === 'loading') {{
        document.addEventListener('DOMContentLoaded', () => {{
            window.__furboApplyStyles(ids);
        }});
    }} else {{
        // Ya está cargado
        window.__furboApplyStyles(ids);
    }}

    // Reintentar cada 500ms por si hay cambios posteriores
    setTimeout(() => {{
        window.__furboApplyStyles(ids);
    }}, 500);

    setTimeout(() => {{
        window.__furboApplyStyles(ids);
    }}, 1000);
}})();
";

            return script;
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