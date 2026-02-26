# 🚀 Sistema de Caché para Scraping

Documentación del nuevo sistema de caché que evita re-scrapar datos innecesarios.

---

## ¿Qué se cachea?

Cuando se extrae información de un partido, se cachea:

✅ **Datos de Liga**
- Nombre de la liga
- País
- URL de la liga
- Imagen de bandera

✅ **Datos de Equipos**
- Nombre equipo local
- Nombre equipo visitante
- Logo equipo local (URL)
- Logo equipo visitante (URL)

❌ **Datos NO cacheados** (cambian cada scrape)
- Marcador
- Tiempo del partido
- Estado de alertas
- Categoría de fase

---

## Configuración del Caché

```csharp
private const long SCRAPING_CACHE_DURATION_MS = 3600000; // 1 hora
```

**Duración**: 1 hora por defecto  
**Después de 1 hora**: El caché expira y se re-scrapia

---

## Clases Relacionadas

### ScrapingCacheEntry

```csharp
public class ScrapingCacheEntry
{
    public string MatchId { get; set; }
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string League { get; set; }
    public string LeagueCountry { get; set; }
    public string HomeImg { get; set; }
    public string AwayImg { get; set; }
    public string LeagueUrl { get; set; }
    public string LeagueImgSrc { get; set; }
    public long CachedAtMs { get; set; }      // Cuándo se cacheó
    public long ExpiresAtMs { get; set; }     // Cuándo expira
    public bool IsExpired { get; }            // ¿Ha expirado?
}
```

---

## Cómo Funciona

### Flujo de Scraping CON Caché

```
1. Se solicita extraer datos de match (ej: "3456789_es_6789")
    │
    ├─→ ¿Está en caché?
    │   ├─ Sí + No expirado
    │   │   └─ ✅ Usar datos cacheados (sin Playwright)
    │   │       └─ [CACHE] ✓ HIT
    │   │
    │   └─ No expirado
    │       └─ ❌ Scrapecar con Playwright
    │           └─ [CACHE] ✗ MISS
    │
    ├─→ Scrape realizado
    │   └─ Guardar en caché
    │       └─ [CACHE] ✓ STORED (expires in 60 min)
    │
    └─ Retornar datos
```

### Ejemplo de Logs

**Primera ejecución**:
```
[CACHE] ✗ MISS for 3456789_es_6789 (hits: 0, misses: 1)
[SCRAPE] Navigating to https://www.flashscore.es/partido/6789/#/resumen-del-partido
[SCRAPE] ✓ Real Madrid vs Barcelona | 2-1 | 52 | España: LaLiga Hypermotion
[CACHE] ✓ STORED 3456789_es_6789 (expires in 60 min)
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    0
[CACHE]   Total Misses:  1
[CACHE]   Hit Rate:      0.0%
[CACHE]   Cached Items:  1
```

**Segunda ejecución (mismo partido)**:
```
[CACHE] ✓ HIT for 3456789_es_6789 (hits: 1, misses: 1)
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    1
[CACHE]   Total Misses:  1
[CACHE]   Hit Rate:      50.0%
[CACHE]   Cached Items:  1
```

---

## Métodos de Manejo de Caché

### PrintCacheStatistics()

Imprime estadísticas del caché después de cada scrape:

```
[CACHE] ═══════════════════════════════════════
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    5
[CACHE]   Total Misses:  2
[CACHE]   Hit Rate:      71.4%
[CACHE]   Cached Items:  7
[CACHE] ═══════════════════════════════════════
```

### ClearExpiredCacheEntries()

**Llamado automáticamente** al inicio de cada scrape:
- Elimina entradas que han expirado (pasaron 1 hora)
- Reduce uso de memoria

```
[CACHE] Removed expired entry: 1234567_es_1234
[CACHE] Removed expired entry: 2345678_es_2345
[CACHE] Cleaned 2 expired entries. Remaining: 5
```

### InvalidateCacheForMatch(matchId)

Elimina un match del caché manualmente:
- Usado cuando se remueve un partido
- Fuerza re-scrape la próxima vez

```
[CACHE] Invalidated cache for 3456789_es_6789
```

### ClearScrapingCache()

**Limpia completamente el caché**:

```csharp
form.ClearScrapingCache();
```

Resultado:
```
[CACHE] Cache cleared completely
```

---

## Performance

### Antes (sin caché):
```
Match 1: 3-5 segundos (scrape completo)
Match 2: 3-5 segundos (scrape completo)
Match 3: 3-5 segundos (scrape completo)
─────────────────────
Total:   9-15 segundos
```

### Después (con caché):
```
Match 1: 3-5 segundos (scrape completo)
Match 2: <50ms (cache hit)
Match 3: <50ms (cache hit)
─────────────────────
Total:   3-5 segundos (3x más rápido)
```

**Mejora**: ~60-70% de reducción en tiempo cuando hay hits

---

## Casos de Uso

### Caso 1: Mismo Partido, Múltiples Llamadas

```
t=0s:  Scrape match #123 → MISS → 4s scrape → Cachéado
t=5s:  Scrape match #123 → HIT  → <50ms caché
t=10s: Scrape match #123 → HIT  → <50ms caché
```

**Beneficio**: 2º y 3º scrape usan caché

### Caso 2: Múltiples Partidos

```
t=0s:  Sync 5 matches
       - Match 1 → MISS (4s scrape)
       - Match 2 → MISS (4s scrape)
       - Match 3 → MISS (4s scrape)
       - Match 4 → MISS (4s scrape)
       - Match 5 → MISS (4s scrape)
       Total: 20s

t=10s: Sync mismos 5 matches
       - Match 1 → HIT  (<50ms caché)
       - Match 2 → HIT  (<50ms caché)
       - Match 3 → HIT  (<50ms caché)
       - Match 4 → HIT  (<50ms caché)
       - Match 5 → HIT  (<50ms caché)
       Total: <250ms (80x más rápido)
```

---

## Estadísticas del Caché

### Hit Rate

```
Hit Rate = (Total Hits) / (Total Hits + Total Misses) * 100
```

**Objetivo**: >80% hit rate indica buen comportamiento

**Ejemplo**:
```
Hits:   75
Misses: 10
Total:  85
Hit Rate: 88.2% ✅ Excelente
```

---

## Configuración Avanzada

### Cambiar Duración del Caché

En `OverlayForm.cs`:

```csharp
private const long SCRAPING_CACHE_DURATION_MS = 1800000; // 30 minutos
```

**Opciones comunes**:
- 300000 = 5 minutos
- 600000 = 10 minutos
- 1800000 = 30 minutos
- 3600000 = 1 hora (default)
- 7200000 = 2 horas

### Desactivar Caché

Comentar en `ExtractMatchData()`:

```csharp
// if (_scrapingCache.TryGetValue(matchId, out var cachedEntry) && !cachedEntry.IsExpired)
// {
//     ... usar caché
// }
```

---

## Thread Safety

✅ **Thread-safe**: Usa `ConcurrentDictionary`  
✅ **Thread-safe**: Usa `Interlocked` para estadísticas  
✅ **No locks**: Evita deadlocks

---

## Monitoreo

### Ver estadísticas después de cada scrape

```
[SCRAPE] Done. Total matches: 5
[CACHE] ═══════════════════════════════════════
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    12
[CACHE]   Total Misses:  5
[CACHE]   Hit Rate:      70.6%
[CACHE]   Cached Items:  5
[CACHE] ═══════════════════════════════════════
```

### Ver logs de caché en tiempo real

Busca en consola:
```
[CACHE] ✓ HIT
[CACHE] ✗ MISS
[CACHE] ✓ STORED
[CACHE] Removed expired entry
```

---

## Ejemplos de Uso

### Limpiar caché cuando se cierra la app

```csharp
protected override void OnFormClosing(FormClosingEventArgs e)
{
    ClearScrapingCache();  // ← Llamar aquí
    // ... resto del cleanup
}
```

### Forzar re-scrape de un match

```csharp
InvalidateCacheForMatch("3456789_es_6789");
await ScrapeAllMatches();  // Rescrapea sin usar caché
```

### Limpiar caché manual desde consola/debugger

```csharp
// En Debug Console
OverlayForm.Instance?.ClearScrapingCache();
```

---

## Limitaciones y Consideraciones

⚠️ **Caché en memoria**: No persiste entre ejecuciones  
⚠️ **Datos estáticos**: Solo cachea datos que no cambian (nombres, logos)  
⚠️ **No cachea**: Marcador, tiempo, estado de alertas  
⚠️ **Expiración fija**: Todos los entries expiran en 1 hora  

---

## Mejoras Futuras

- [ ] Caché persistente (guardar en archivo)
- [ ] LRU (Least Recently Used) para limitar tamaño
- [ ] Estadísticas por hora
- [ ] Caché específico por tipo de dato
- [ ] Invalidación inteligente basada en cambios detectados

---

## Resumen

| Aspecto | Beneficio |
|--------|-----------|
| **Speed** | 3-80x más rápido en hits |
| **Network** | Reduce solicitudes HTTP |
| **Batch** | Múltiples scrapes → Rápido después del 1º |
| **Memory** | Limpieza automática de expirados |
| **Logging** | Estadísticas visibles en consola |
| **Thread-safe** | Funciona sin problemas multi-thread |

