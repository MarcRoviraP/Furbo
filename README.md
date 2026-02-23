# Furbo – Flashscore Overlay

Un overlay flotante para Windows que muestra en tiempo real los partidos seleccionados en Flashscore, usando un userscript de Tampermonkey y una aplicación WPF nativa.

---

## Descripción general

```
Flashscore (navegador)
       │  POST HTML cada 2 s
       ▼
 localhost:19000  ◄──── HttpListener (C# / WPF)
       │  PostWebMessageAsString
       ▼
  WebView2 (overlay transparente siempre encima)
```

El userscript inyecta un botón en cada partido de Flashscore. Al hacer clic, el partido se envía periódicamente al servidor local embebido en la app WPF, que lo renderiza en un overlay transparente y siempre-en-primer-plano.

---

## Componentes

### `Flashscore Overlay.js` — Userscript (Tampermonkey)

| Campo | Valor |
|---|---|
| Plataformas | `flashscore.es`, `flashscore.com` |
| Versión | 30.0 |
| Permisos | `GM_xmlhttpRequest`, `GM_addStyle` |

**Funcionalidades principales:**

- **Botón de seguimiento** — Añade un pequeño botón rojo (●) en cada fila `.event__match`. Al pulsarlo el partido se marca en azul y queda rastreado.
- **Rastreo persistente** — Los partidos seleccionados se guardan en `localStorage` (`fc_overlay_v27`) para sobrevivir recargas de página.
- **Actualización periódica** — Cada 2 segundos llama a `updateExternalOverlay()` para enviar el HTML actualizado al servidor.
- **Envío remoto** — `sendDataToCSharp()` hace un `POST` a `http://localhost:19000/` con el HTML de los partidos agrupados por liga.
- **Borrado remoto** — Si el servidor responde con `REMOVE:<id>`, el partido se elimina del rastreo.
- **Cabecera de liga** — `getLeagueHeaderData()` localiza el encabezado de la competición y extrae bandera (via [flagcdn.com](https://flagcdn.com)), categoría y nombre.
- **Mapa de banderas** — Diccionario `COUNTRY_TO_ISO` con ~100 países (nombres en ES/EN → ISO 3166-1 alpha-2) con normalización de acentos.
- **MutationObserver** — Detecta partidos cargados dinámicamente y les añade el botón automáticamente.

**Ciclo de vida:**
```
document-end → setTimeout 1.5 s → addButtonsToAllMatches()
                                 → observer.observe()
                                 → updateExternalOverlay()
                         ↺ setInterval 2 s → updateExternalOverlay()
```

---

### `MainWindow.xaml.cs` — Aplicación WPF (C#)

Ventana sin bordes, transparente, siempre encima (`Topmost = true`), sin entrada en la barra de tareas.

#### Servidor HTTP embebido

- Escucha en `http://localhost:19000/` mediante `HttpListener`.
- Acepta POST con el HTML de los partidos y lo reenvía al WebView2 vía `PostWebMessageAsString`.
- Si hay un ID pendiente de eliminar (`_idsToRemove`), lo devuelve en la respuesta como `REMOVE:<id>`.
- Cabecera CORS `Access-Control-Allow-Origin: *` para compatibilidad con el userscript.

#### WebView2

- Carga `BaseHtml` (HTML/CSS/JS inline construido en `BuildHtml()`).
- CSS personalizado reproduce el look de Flashscore con tipografía Ubuntu, colores oscuros y soporte para:
  - Partidos individuales y dobles (tenis)
  - Superíndices de tie-break (`<sup>`)
  - Marcadores parciales (sets, períodos) en columnas adicionales
  - Animación de parpadeo del apostrofo de minuto en vivo
  - Alerta visual (fondo rojo `#3D0314`) durante 10 s tras un gol/evento
- JS inline:
  - `applyLiveColors()` — colorea y formatea cada partido según su estado (en vivo, descanso, finalizado, programado).
  - `processScoreCell()` — detecta goles, penaltis y revisiones VAR; preserva superíndices.
  - `sendResize()` — notifica la altura del contenido para redimensionar la ventana automáticamente.
  - Recibe mensajes del C# vía `window.chrome.webview.postMessage`.

#### Mensajes WebView2 → C#

| Mensaje | Acción |
|---|---|
| `DRAG` | Inicia arrastre nativo de la ventana (`WM_SYSCOMMAND / SC_MOVE`) |
| `REMOVE:<id>` | Encola el ID para devolverlo al userscript en la próxima respuesta |
| `RESIZE:<px>` | Ajusta `this.Height` (mín. 80 px, máx. pantalla − 100 px) |
| `OPEN:<path>` | Abre la URL de la liga en el navegador predeterminado |

#### Características extra de ventana
- **No roba el foco** — `WndProc` devuelve `MA_NOACTIVATE` para `WM_MOUSEACTIVATE`.
- **Arrastrable** — clic izquierdo en cualquier punto activa `SC_MOVE`.
- **Doble clic en cabecera de liga** — abre la página de la competición en el navegador.
- **Clic derecho en un partido** — lo elimina del overlay y notifica al userscript.

---

## Instalación

### Requisitos
- Windows 10/11
- .NET 10 (Windows)
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
- Navegador con [Tampermonkey](https://www.tampermonkey.net/)

### Pasos

1. **Compilar y ejecutar la app WPF:**
   ```bat
   start.bat
   ```
   O abre `Furbo.slnx` en Visual Studio y ejecuta el proyecto.

2. **Instalar el userscript:**
   - Abre Tampermonkey → *Crear nuevo script*.
   - Pega el contenido de `Flashscore Overlay.js` y guarda.

3. **Navega a [flashscore.es](https://www.flashscore.es).**  
   Aparecerá un punto rojo (●) en cada partido. Haz clic para añadirlo al overlay.

---

## Uso

| Acción | Resultado |
|---|---|
| Clic en ● rojo (Flashscore) | Añade el partido al overlay (botón se pone azul) |
| Clic en ● azul (Flashscore) | Elimina el partido del overlay |
| Arrastrar el overlay | Reposiciona la ventana |
| Doble clic en cabecera de liga | Abre la página de la competición |
| Clic derecho en un partido | Elimina ese partido del overlay |

El overlay se redimensiona automáticamente en altura según el número de partidos visibles.

---

## Estructura del proyecto

```
Furbo/
├── Flashscore Overlay.js   # Userscript Tampermonkey
├── MainWindow.xaml         # Diseño XAML de la ventana
├── MainWindow.xaml.cs      # Lógica: servidor HTTP + WebView2
├── App.xaml / App.xaml.cs  # Punto de entrada WPF
├── Furbo.csproj            # Proyecto .NET 10 (net10.0-windows)
├── Furbo.slnx              # Solución Visual Studio
└── start.bat               # Script de arranque rápido
```