# ✅ Corrección: Tiempo y Marcador Visibles en Todas las Cargas + Layout Adaptable

## Problemas Identificados y Corregidos

### ❌ Problema 1: Tiempo y Marcador No Aparecían en Segunda Carga

**Causa**: En CACHE HIT, el código retornaba temprano sin scrapear los datos dinámicos.

**Síntoma**:
- 1ª carga: ✓ Tiempo y marcador visibles
- 2ª carga (CACHE HIT): ✗ Tiempo y marcador VACÍOS

### ✅ Solución 1: Separar Lógica de Caché y Datos Dinámicos

**Estructura correcta ahora**:

```csharp
if (CACHE_HIT)
{
    // Obtener datos estáticos del caché
    data.HomeTeam = cached...
    data.League = cached...
    // NO retornar aquí
}
else
{
    // Scrappear datos estáticos si no hay caché
    data.HomeTeam = await scrape...
    data.League = await scrape...
}

// ─ SIEMPRE scrappear datos dinámicos ─
data.HomeScore = await scrape...    // SIEMPRE
data.AwayScore = await scrape...    // SIEMPRE
data.MatchTime = await scrape...    // SIEMPRE

// Cache solo estáticos si fue MISS
if (!cacheHit)
{
    _scrapingCache.AddOrUpdate(...)
}
```

### ❌ Problema 2: Layout del Tiempo No Se Adaptaba al Contenido

**Síntoma**: Tiempo desalineado o cortado porque usaba tamaño fijo.

**Código anterior**:
```csharp
var timeRect = new RectangleF(cx, y, TIME_COL_W, MATCH_ROW_HEIGHT);
g.DrawString(timeText, timeFont, new SolidBrush(timeColor), timeRect, sf);
// Rectangle fijo, texto puede estar descentrado
```

### ✅ Solución 2: Medir Texto y Centrar Dinámicamente

**Nuevo código**:
```csharp
// Medir tamaño real del texto
var timeSize = g.MeasureString(timeText, timeFont);

// Calcular posición centrada
float timeX = cx + (TIME_COL_W - timeSize.Width) / 2;
float timeY = y + MATCH_ROW_HEIGHT / 2 - timeSize.Height / 2;

// Dibujar en posición calculada
g.DrawString(timeText, timeFont, new SolidBrush(timeColor), timeX, timeY);
```

**Beneficios**:
- ✓ Texto siempre centrado
- ✓ Se adapta a cualquier tamaño de fuente
- ✓ Funciona con números cortos (52) y largos (90+2)

---

## Cambios Implementados

### En `ExtractMatchData()` - Línea ~433

**Antes** (INCORRECTO):
```csharp
if (CACHE_HIT)
{
    return new MatchData { ... };  // ← RETORNA SIN TIEMPO/MARCADOR
}
```

**Después** (CORRECTO):
```csharp
if (CACHE_HIT)
{
    cacheHit = true;
    data.HomeTeam = cached...  // ← SIN RETURN
    // ... más datos estáticos
}
else
{
    // scrappear estáticos
}

// SIEMPRE scrappear dinámicos
data.HomeScore = await scrape...
data.MatchTime = await scrape...

// Cache solo si fue MISS
if (!cacheHit)
{
    _scrapingCache.AddOrUpdate(...)
}
```

### En `OnPaint()` - Línea ~918

**Antes** (INCORRECTO):
```csharp
var timeRect = new RectangleF(cx, y, TIME_COL_W, MATCH_ROW_HEIGHT);
g.DrawString(timeText, timeFont, new SolidBrush(timeColor), timeRect, sf);
// Usa StringFormat para centrar, pero no mide el texto
```

**Después** (CORRECTO):
```csharp
if (isLive && timeText.Any(char.IsDigit))
{
    // Medir texto
    var timeSize = g.MeasureString(timeText, timeFont);
    float apoWidth = _blinkOn ? g.MeasureString("'", timeFont).Width : 0;
    float totalTimeWidth = timeSize.Width + apoWidth;
    
    // Centrar dinámicamente
    float timeX = cx + (TIME_COL_W - totalTimeWidth) / 2;
    float timeY = y + MATCH_ROW_HEIGHT / 2 - timeSize.Height / 2;
    
    g.DrawString(timeText, timeFont, new SolidBrush(timeColor), timeX, timeY);
}
else
{
    // También adaptar para no-vivo
    var timeSize = g.MeasureString(timeText, timeFont);
    float timeX = cx + (TIME_COL_W - timeSize.Width) / 2;
    float timeY = y + MATCH_ROW_HEIGHT / 2 - timeSize.Height / 2;
    g.DrawString(timeText, timeFont, new SolidBrush(timeColor), timeX, timeY);
}
```

---

## Comportamiento Ahora

### Cargas Posteriores

| Scrape | Caché | Tiempo | Marcador | Nombres | Logos |
|--------|-------|--------|----------|---------|-------|
| **1º** | MISS | ✓ Visible | ✓ Visible | ✓ Visible | ✓ Visible |
| **2º** | HIT | ✓ Visible | ✓ Visible | ✓ Cacheado | ✓ Cacheado |
| **3º+** | HIT | ✓ Visible | ✓ Visible | ✓ Cacheado | ✓ Cacheado |

### Layout del Tiempo

**Ejemplo con diferentes contenidos**:
```
Antes (fijo):          Ahora (adaptable):
[52  ]                 [    52    ]
[45+2]                 [  45+2    ]
[HT  ]                 [   HT     ]
[22:30]                [  22:30   ]
```

El texto se centra perfectamente sin importar su tamaño.

---

## Validación

✅ **Compilado sin errores**  
✅ **Tiempo visible en todas las cargas**  
✅ **Marcador visible en todas las cargas**  
✅ **Layout adaptable**  
✅ **Centrado correcto**  
✅ **Caché funciona correctamente**  

---

## Resumen

| Aspecto | Antes | Ahora |
|--------|-------|-------|
| 2ª carga sin tiempo | ❌ Falla | ✅ Funciona |
| 2ª carga sin marcador | ❌ Falla | ✅ Funciona |
| Layout tiempo adaptable | ❌ No | ✅ Sí |
| Texto centrado | ⚠️ Con StringFormat | ✅ Medición real |
| Compatibilidad caché | ❌ Roto | ✅ Correcto |

