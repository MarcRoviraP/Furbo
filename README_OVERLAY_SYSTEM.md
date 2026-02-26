# Sistema de Overlay de Flashscore

Documentación del nuevo sistema de obtención y renderizado de datos de partidos en tiempo real.

## 📋 Tabla de Contenidos

1. [Arquitectura General](#arquitectura-general)
2. [Flujo de Datos](#flujo-de-datos)
3. [Obtención de Datos (Scraping)](#obtención-de-datos-scraping)
4. [Comunicación WebSocket](#comunicación-websocket)
5. [Estructura de Datos](#estructura-de-datos)
6. [Renderizado en Overlay](#renderizado-en-overlay)
7. [Sistema de Alertas](#sistema-de-alertas)

---

## Arquitectura General

El sistema consta de tres componentes principales:

```
┌─────────────────────────────────────────────────┐
│  Frontend Web (Flashscore2D)                    │
│  - Selecciona partidos                          │
│  - Envía comandos por WebSocket                 │
└────────────┬────────────────────────────────────┘
             │ WebSocket (ws://localhost:19000)
             │
┌────────────▼────────────────────────────────────┐
│  WinForms Overlay (OverlayForm)                 │
│  - Servidor WebSocket                           │
│  - Motor de Scraping (Playwright)               │
│  - Renderizado GDI+ en tiempo real              │
└─────────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────┐
│  Flashscore.es                                  │
│  - HTML con datos de partidos                   │
│  - Selectores CSS identificados                 │
└─────────────────────────────────────────────────┘
```

---

## Flujo de Datos

### 1️⃣ Sincronización Inicial

```
Cliente Web envía:
{
  "action": "sync",
  "ids": ["match_123", "match_456", "match_789"]
}
         │
         ▼
Overlay recibe y actualiza lista de partidos tracked
         │
         ▼
Inicia scraping de cada match ID
```

### 2️⃣ Ciclo de Scraping

```
Timer cada 10 segundos
    │
    ▼
ScrapeAllMatches()
    │
    ├─ Para cada match ID tracked:
    │   ├─ Navega a: https://www.flashscore.es/partido/{id}/#/resumen-del-partido
    │   ├─ Extrae datos con Playwright
    │   └─ Guarda en lista de matches
    │
    ├─ Compara con datos anteriores
    │   ├─ Si score cambió → AlertExpiresMs = now + 10000ms
    │   └─ Si estado cambió → StageAlertExpiresMs = now + 10000ms
    │
    └─ Recalcula altura del formulario
       └─ Renderiza (OnPaint)
```

---

## Obtención de Datos (Scraping)

### Herramientas Utilizadas

- **Playwright**: Navegador headless Chrome para scraping
- **C# async/await**: Ejecución no-bloqueante
- **Selectores CSS**: Targeting directo a elementos del DOM

### Nota Importante: Debug vs Release

⚠️ **Diferencia de Comportamiento**:
- En modo **Debug**: Más tiempo de espera = DOM completamente renderizado
- En modo **Release**: Ejecución más rápida = Posibles atributos dinámicos no cargados

**Solución implementada**: Sistema de **4 estrategias de extracción** en cascada
- Strategy 1: `src` directo
- Strategy 2: `data-src` (lazy loading)
- Strategy 3: CSS `background-image`
- Strategy 4: Búsqueda por clase `wcl-flag`

Ver `FIX_DEBUG_RELEASE_FLAGS.md` para más detalles.

### Inicio de Playwright

```csharp
// En InitPlaywrightAsync()
_playwright = await Playwright.CreateAsync();
_browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true  // Sin interfaz gráfica
});
```

### Selectores CSS Utilizados

#### 🏆 Liga y País

| Dato | Selector | Fallback |
|------|----------|----------|
| **País** | `.wcl-breadcrumbs_0ZcSd li` (2º item) | `meta[property='og:description']` |
| **Liga** | `.wcl-breadcrumbs_0ZcSd li` (3º item) | Parsing de `og:description` |
| **URL Liga** | `.wcl-breadcrumbs_0ZcSd li a` href | (vacío si no existe) |

**Ejemplo de breadcrumbs:**
```html
<!-- Estructura esperada -->
<li>Fútbol</li>
<li>España</li>
<li><a href="/categoria/espana-laliga/">LaLiga Hypermotion - Jornada 25</a></li>
```

**Fallback meta tag:**
```html
<meta property="og:description" content="ESPAÑA: LaLiga Hypermotion - Jornada 25">
```

#### ⚽ Equipos

| Dato | Selector |
|------|----------|
| **Equipo Local** | `.duelParticipant__home .participant__participantName` |
| **Equipo Visitante** | `.duelParticipant__away .participant__participantName` |

**Ejemplo HTML:**
```html
<div class="duelParticipant__home">
  <div class="participant__participantName">Real Madrid</div>
</div>
<div class="duelParticipant__away">
  <div class="participant__participantName">FC Barcelona</div>
</div>
```

#### 🏅 Logos

| Dato | Selector |
|------|----------|
| **Logo Local** | `.duelParticipant__home img.participant__image` src |
| **Logo Visitante** | `.duelParticipant__away img.participant__image` src |

#### 📊 Marcador

**Opción 1 (Preferida):**
```html
<div class="detailScore__wrapper">
  <span>2</span>      <!-- score local -->
  <span>-</span>      <!-- separador -->
  <span>1</span>      <!-- score visitante -->
</div>
```

**Opción 2 (Fallback):**
```html
<div class="duelParticipant__score">2-1</div>
```

**Extracción:**
```csharp
var scoreSpans = await page.QuerySelectorAllAsync(".detailScore__wrapper span");
if (scoreSpans.Count >= 3)
{
    data.HomeScore = await scoreSpans[0].TextContentAsync();  // "2"
    data.AwayScore = await scoreSpans[2].TextContentAsync();  // "1"
}
```

#### ⏱️ Tiempo del Partido

| Estado | Ejemplo | Selector |
|--------|---------|----------|
| **En directo** | "52'" | `.detailScore__status span` |
| **Descanso** | "HT" o "Descanso" | `.detailScore__status span` |
| **Finalizado** | "FT" o "Fin" | `.detailScore__status span` |
| **Programado** | "15:30" | `.duelParticipant__startTime` |

**Extracción de tiempo:**
```csharp
var timeSpans = await page.QuerySelectorAllAsync(".detailScore__status span");
// Puede contener: ["2º tiempo", "52"] → show "52"
// O simplemente: ["52"]
// O: ["HT"], ["Descanso"], ["FT"], ["Fin"]
```

### Categorización de Fases

```csharp
private static string GetStageCategory(string text)
{
    // "HT", "descanso" → "halftime"
    // "fin", "F", "post" → "finished"
    // Contiene ":" → "scheduled" (ej: "15:30")
    // Contiene dígitos → "live" (ej: "52")
    // Else → "other"
}
```

### Método de Extracción Completo

```csharp
private async Task<MatchData?> ExtractMatchData(IPage page, string matchId)
{
    var data = new MatchData { MatchId = matchId };
    
    // 1. Liga y país
    var breadcrumbItems = await page.QuerySelectorAllAsync(".wcl-breadcrumbs_0ZcSd li");
    if (breadcrumbItems.Count >= 3)
    {
        data.LeagueCountry = (await breadcrumbItems[1].TextContentAsync())?.Trim();
        data.League = (await breadcrumbItems[2].TextContentAsync())?.Trim();
    }
    
    // 2. Equipos
    data.HomeTeam = await SafeTextContent(page, ".duelParticipant__home .participant__participantName");
    data.AwayTeam = await SafeTextContent(page, ".duelParticipant__away .participant__participantName");
    
    // 3. Logos
    data.HomeImg = await SafeAttribute(page, ".duelParticipant__home img.participant__image", "src");
    data.AwayImg = await SafeAttribute(page, ".duelParticipant__away img.participant__image", "src");
    
    // 4. Marcador
    var scoreSpans = await page.QuerySelectorAllAsync(".detailScore__wrapper span");
    if (scoreSpans.Count >= 3)
    {
        data.HomeScore = (await scoreSpans[0].TextContentAsync())?.Trim();
        data.AwayScore = (await scoreSpans[2].TextContentAsync())?.Trim();
    }
    
    // 5. Tiempo
    var timeSpans = await page.QuerySelectorAllAsync(".detailScore__status span");
    // ... procesamiento de spans ...
    
    return data;
}
```

---

## Comunicación WebSocket

### Servidor WebSocket

**Configuración:**
```csharp
_wssv = new WebSocketServer("ws://localhost:19000");
_wssv.AddWebSocketService<FlashscoreBehavior>("/flashscore");
_wssv.Start();
```

**URL:** `ws://localhost:19000/flashscore`

### Comandos Soportados

#### 1. **Sincronizar** (obtener lista completa)

**Cliente → Servidor:**
```json
{
  "action": "sync",
  "ids": ["3456789_es_6789", "3456790_es_6790"]
}
```

**Efecto:**
- Reemplaza la lista de IDs tracked
- Inicia scraping automático

---

#### 2. **Agregar Partido**

**Cliente → Servidor:**
```json
{
  "action": "add",
  "matchId": "3456791_es_6791"
}
```

**Efecto:**
- Añade a lista de tracked
- Inicia scraping

---

#### 3. **Remover Partido**

**Cliente → Servidor:**
```json
{
  "action": "remove",
  "matchId": "3456789_es_6789"
}
```

**Efecto:**
- Elimina de la lista visible
- Notifica al cliente

---

## Estructura de Datos

### Clase `MatchData`

```csharp
public class MatchData
{
    // Identificación
    public string MatchId { get; set; }           // "3456789_es_6789"
    
    // Equipos
    public string HomeTeam { get; set; }          // "Real Madrid"
    public string AwayTeam { get; set; }          // "FC Barcelona"
    public string HomeImg { get; set; }           // URL del logo
    public string AwayImg { get; set; }           // URL del logo
    
    // Marcador
    public string HomeScore { get; set; }         // "2"
    public string AwayScore { get; set; }         // "1"
    
    // Información del partido
    public string MatchTime { get; set; }         // "52" o "HT" o "15:30"
    public string League { get; set; }            // "LaLiga Hypermotion"
    public string LeagueCountry { get; set; }     // "España"
    public string LeagueUrl { get; set; }         // URL de la liga
    
    // Estado de alertas
    public string PrevHomeScore { get; set; }     // Score anterior
    public string PrevAwayScore { get; set; }     // Score anterior
    public long AlertExpiresMs { get; set; }      // Timestamp expiración alerta de score
    public string StageCategory { get; set; }     // "live", "halftime", "finished", etc.
    public long StageAlertExpiresMs { get; set; } // Timestamp expiración alerta de fase
}
```

### Enumeración de Estados

```csharp
// StageCategory values:
"live"       // En directo (contiene dígitos sin ":"): "52"
"halftime"   // Descanso: "HT", "Descanso"
"finished"   // Finalizado: "F", "FT", "Fin"
"scheduled"  // Programado (formato hora): "15:30"
"empty"      // Sin datos
"other"      // Otro
```

---

## Renderizado en Overlay

### Ciclo de Renderizado

```csharp
protected override void OnPaint(PaintEventArgs e)
{
    // 1. Obtener snapshot de matches
    // 2. Agrupar por liga
    // 3. Para cada liga:
    //    - Dibujar encabezado (nombre liga)
    //    - Para cada partido:
    //       - Dibujar fila con: tiempo | equipos | marcador
    //       - Aplicar alertas visuales si es necesario
}
```

### Estructura Visual

```
┌─ HEADER (30px) ────────────────────────────┐
│ España: LaLiga Hypermotion - Jornada 25   │
├────────────────────────────────────────────┤
│ 52' │ Real Madrid  [logo] │  2            │
│     │ FC Barcelona [logo] │  1            │
├────────────────────────────────────────────┤
│ HT  │ Barcelona    [logo] │  0            │
│     │ Sevilla      [logo] │  1            │
└────────────────────────────────────────────┘
```

### Colores Utilizados

| Elemento | Color | Código Hex |
|----------|-------|-----------|
| **Fondo Encabezado** | Azul Marino | #001e28 |
| **Fondo Partido** | Azul Oscuro | #00141e |
| **Hover** | Azul Claro | #0b1e28 |
| **Alerta Score** | Rojo Oscuro | #3D0314 |
| **Texto Encabezado** | Azul Claro | #accbd9 |
| **Tiempo (en directo)** | Rojo | #FF0046 |
| **Tiempo (otros)** | Blanco | #FFFFFF |

### Dimensiones

```csharp
const int FORM_WIDTH = 560;           // Ancho total
const int HEADER_HEIGHT = 30;         // Altura del encabezado por liga
const int MATCH_ROW_HEIGHT = 56;      // Altura de cada fila de partido
const int MIN_HEIGHT = 80;            // Altura mínima
const int LOGO_SIZE = 14;             // Tamaño de logos
const int TIME_COL_W = 45;            // Ancho columna tiempo
const int SCORE_COL_W = 35;           // Ancho columna score
```

### Cálculo de Altura

```
total_height = (num_ligas × HEADER_HEIGHT) + (num_partidos × MATCH_ROW_HEIGHT)
max_height = screen_height - 100
final_height = max(MIN_HEIGHT, min(total_height, max_height))
```

---

## Sistema de Alertas

### Tipos de Alertas

#### 1. **Alerta de Score** (Cambio de marcador)

```csharp
if (existing.HomeScore != nd.HomeScore || 
    existing.AwayScore != nd.AwayScore)
{
    nd.AlertExpiresMs = nowMs + 10000;  // 10 segundos
}
```

**Visual:** Fondo rojo (#3D0314) por 10 segundos

#### 2. **Alerta de Fase** (Cambio de estado del partido)

```csharp
string newCat = GetStageCategory(nd.MatchTime);
if (existing.StageCategory != "" && existing.StageCategory != newCat)
{
    nd.StageAlertExpiresMs = nowMs + 10000;
}
```

**Visual:** Flash rojo alrededor del tiempo

### Indicador de Directo

```csharp
// Cada 500ms parpadea la comilla
if (isLive && _blinkOn)
{
    g.DrawString("'", _fontTimeLive, brush, x, y);  // "52'"
}
```

---

## Caché de Imágenes

### Estrategia

```csharp
// 1. Detectar URL nueva de imagen
if (!_imageCache.ContainsKey(url))
{
    // 2. Marcar como "cargando"
    _imageLoading.TryAdd(url, true);
    
    // 3. Descargar en background
    _ = Task.Run(async () =>
    {
        var bytes = await _httpClient.GetByteArrayAsync(url);
        var img = Image.FromStream(new MemoryStream(bytes));
        _imageCache.TryAdd(url, img);  // Guardar en caché
    });
}
```

### Beneficios

✅ No bloquea UI durante descarga  
✅ Las imágenes se reutilizan (evita descargas repetidas)  
✅ Renderizado más fluido

---

## Timers y Ciclos

### Timer de Scraping (10 segundos)

```csharp
_scrapeTimer = new System.Threading.Timer(
    async _ => await ScrapeAllMatches(),
    null,
    3000,    // Delay inicial: 3 segundos
    10000    // Intervalo: 10 segundos
);
```

### Timer de Parpadeo (500ms)

```csharp
_blinkTimer = new System.Windows.Forms.Timer { Interval = 500 };
_blinkTimer.Tick += (s, e) => 
{
    _blinkOn = !_blinkOn;  // Alterna cada 500ms
    Invalidate();          // Redibuja
};
```

---

## Manejo de Errores

### Timeouts en Scraping

```csharp
await page.GotoAsync(url, new PageGotoOptions 
{ 
    WaitUntil = WaitUntilState.DOMContentLoaded,
    Timeout = 15000  // 15 segundos máximo
});

await page.WaitForSelectorAsync(".duelParticipant__home", 
    new PageWaitForSelectorOptions { Timeout = 8000 });  // 8 segundos
```

### Fallbacks en Extracción

```csharp
// Si breadcrumbs no existen, usar meta tag
var ogDesc = await SafeAttribute(page, "meta[property='og:description']", "content");

// Si detailScore no existe, usar duelParticipant__score
var scoreSpans = await page.QuerySelectorAllAsync(".detailScore__wrapper span");
if (scoreSpans.Count == 0)
{
    // Fallback a otra estructura
}
```

---

## Notas de Implementación

⚠️ **Hilo de Seguridad:**
- `_matches` protegida por `_matchLock`
- Imágenes en `ConcurrentDictionary` para acceso multithread

⚠️ **Performance:**
- `DoubleBuffered = true` para reducir parpadeos
- `BeginInvoke()` para operaciones asincrónicas desde threads

⚠️ **Límites:**
- Max 5 partidos simultáneos recomendado (limitar carga de navegador)
- URLs de imágenes se cachean indefinidamente (considerar limpieza periódica)

---

## Desarrollo Futuro

- [ ] Persistencia de match IDs (archivo local)
- [ ] Limpieza automática de caché de imágenes
- [ ] Soporte para múltiples ligas/filtros
- [ ] Notificaciones de sonido en alertas
- [ ] API REST alternativa a WebSocket

