# 📊 Ejemplos de Uso del Sistema de Caché

Ejemplos prácticos del nuevo sistema de caché.

---

## Ejemplo 1: Primer Scrape (Cold Start)

### Escenario
Usuario abre la app y selecciona 3 partidos de La Liga.

### Logs Esperados

```
[WS] Received action: sync | message: {"action":"sync","ids":["3456789_es_1","3456790_es_2","3456791_es_3"]}
[SCRAPE] Starting scrape of 3 matches: [3456789_es_1, 3456790_es_2, 3456791_es_3]
[CACHE] Cleaned 0 expired entries. Remaining: 0

[SCRAPE] Navigating to https://www.flashscore.es/partido/1/#/resumen-del-partido
[CACHE] ✗ MISS for 3456789_es_1 (hits: 0, misses: 1)
[SCRAPE] ✓ Real Madrid vs Barcelona | 2-1 | 52 | España: LaLiga Hypermotion
[CACHE] ✓ STORED 3456789_es_1 (expires in 60 min)

[SCRAPE] Navigating to https://www.flashscore.es/partido/2/#/resumen-del-partido
[CACHE] ✗ MISS for 3456790_es_2 (hits: 0, misses: 2)
[SCRAPE] ✓ Sevilla vs Athletic | 1-0 | 30 | España: LaLiga Hypermotion
[CACHE] ✓ STORED 3456790_es_2 (expires in 60 min)

[SCRAPE] Navigating to https://www.flashscore.es/partido/3/#/resumen-del-partido
[CACHE] ✗ MISS for 3456791_es_3 (hits: 0, misses: 3)
[SCRAPE] ✓ Atletico vs Valencia | 0-0 | 45+2 | España: LaLiga Hypermotion
[CACHE] ✓ STORED 3456791_es_3 (expires in 60 min)

[SCRAPE] Done. Total matches: 3
[CACHE] ═══════════════════════════════════════
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    0
[CACHE]   Total Misses:  3
[CACHE]   Hit Rate:      0.0%
[CACHE]   Cached Items:  3
[CACHE] ═══════════════════════════════════════
```

**Time**: ~12 segundos (4s por match)  
**Cache Hits**: 0  
**Cache Misses**: 3  

---

## Ejemplo 2: Segunda Ejecución (Warm Cache)

### Escenario
5 segundos después, se ejecuta otro scrape de los mismos 3 partidos.

### Logs Esperados

```
[WS] Received action: sync | message: {"action":"sync","ids":["3456789_es_1","3456790_es_2","3456791_es_3"]}
[SCRAPE] Starting scrape of 3 matches: [3456789_es_1, 3456790_es_2, 3456791_es_3]
[CACHE] Cleaned 0 expired entries. Remaining: 3

[CACHE] ✓ HIT for 3456789_es_1 (hits: 1, misses: 3)
[SCRAPE] ✓ Real Madrid vs Barcelona | 2-1 | 52 | España: LaLiga Hypermotion

[CACHE] ✓ HIT for 3456790_es_2 (hits: 2, misses: 3)
[SCRAPE] ✓ Sevilla vs Athletic | 1-0 | 30 | España: LaLiga Hypermotion

[CACHE] ✓ HIT for 3456791_es_3 (hits: 3, misses: 3)
[SCRAPE] ✓ Atletico vs Valencia | 0-0 | 45+2 | España: LaLiga Hypermotion

[SCRAPE] Done. Total matches: 3
[CACHE] ═══════════════════════════════════════
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    3
[CACHE]   Total Misses:  3
[CACHE]   Hit Rate:      50.0%
[CACHE]   Cached Items:  3
[CACHE] ═══════════════════════════════════════
```

**Time**: <200ms (caché + renderizado)  
**Cache Hits**: 3  
**Speedup**: ~60x más rápido  

---

## Ejemplo 3: Mix de Hits y Misses

### Escenario
User agrega 2 partidos nuevos mientras mantiene 2 antiguos.

### Logs Esperados

```
[WS] Received action: sync | message: {"action":"sync","ids":["3456789_es_1","3456790_es_2","9999999_es_new1","9999998_es_new2"]}
[SCRAPE] Starting scrape of 4 matches: [3456789_es_1, 3456790_es_2, 9999999_es_new1, 9999998_es_new2]

[CACHE] ✓ HIT for 3456789_es_1 (hits: 4, misses: 3)
[SCRAPE] ✓ Real Madrid vs Barcelona | 2-1 | 52 | España: LaLiga Hypermotion

[CACHE] ✓ HIT for 3456790_es_2 (hits: 5, misses: 3)
[SCRAPE] ✓ Sevilla vs Athletic | 1-0 | 30 | España: LaLiga Hypermotion

[CACHE] ✗ MISS for 9999999_es_new1 (hits: 5, misses: 4)
[SCRAPE] Navigating to https://www.flashscore.es/partido/new1/#/resumen-del-partido
[SCRAPE] ✓ PSG vs Lyon | 3-0 | 78 | Francia: Ligue 1
[CACHE] ✓ STORED 9999999_es_new1 (expires in 60 min)

[CACHE] ✗ MISS for 9999998_es_new2 (hits: 5, misses: 5)
[SCRAPE] Navigating to https://www.flashscore.es/partido/new2/#/resumen-del-partido
[SCRAPE] ✓ Chelsea vs Man City | 1-2 | 90 | England: Premier League
[CACHE] ✓ STORED 9999998_es_new2 (expires in 60 min)

[SCRAPE] Done. Total matches: 4
[CACHE] ═══════════════════════════════════════
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    5
[CACHE]   Total Misses:  5
[CACHE]   Hit Rate:      50.0%
[CACHE]   Cached Items:  4
[CACHE] ═══════════════════════════════════════
```

**Time**: ~8 segundos (2x hits + 2x nuevos scrapes)  
**Speedup vs sin caché**: 2x más rápido  

---

## Ejemplo 4: Caché Expirado (1+ Hora)

### Escenario
App abierta hace 1+ hora, se ejecuta otro scrape.

### Logs Esperados

```
[WS] Received action: sync | message: {"action":"sync","ids":["3456789_es_1","3456790_es_2"]}
[SCRAPE] Starting scrape of 2 matches: [3456789_es_1, 3456790_es_2]

[CACHE] Removed expired entry: 3456789_es_1
[CACHE] Removed expired entry: 3456790_es_2
[CACHE] Cleaned 2 expired entries. Remaining: 0

[CACHE] ✗ MISS for 3456789_es_1 (hits: 5, misses: 7)
[SCRAPE] Navigating to https://www.flashscore.es/partido/1/#/resumen-del-partido
[SCRAPE] ✓ Real Madrid vs Barcelona | 2-2 | 90 | España: LaLiga Hypermotion (actualizado)
[CACHE] ✓ STORED 3456789_es_1 (expires in 60 min)

[CACHE] ✗ MISS for 3456790_es_2 (hits: 5, misses: 8)
[SCRAPE] Navigating to https://www.flashscore.es/partido/2/#/resumen-del-partido
[SCRAPE] ✓ Sevilla vs Athletic | 1-1 | 90 | España: LaLiga Hypermotion (finalizado)
[CACHE] ✓ STORED 3456790_es_2 (expires in 60 min)

[SCRAPE] Done. Total matches: 2
[CACHE] ═══════════════════════════════════════
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    5
[CACHE]   Total Misses:  8
[CACHE]   Hit Rate:      38.5%
[CACHE]   Cached Items:  2
[CACHE] ═══════════════════════════════════════
```

**Comportamiento**: Caché se limpió automáticamente y se re-scrapea  
**Beneficio**: Datos frescos después de 1 hora  

---

## Ejemplo 5: Remover Partido (Invalidar Caché)

### Escenario
User elimina un partido que estaba cacheado.

### Logs Esperados

```
[WS] Received action: remove | message: {"action":"remove","matchId":"3456789_es_1"}
[WS] Removing match: 3456789_es_1
[CACHE] Invalidated cache for 3456789_es_1
```

**Siguiente scrape**: Ese partido no se rescrapea (fue removido)  

---

## Ejemplo 6: Estadísticas Acumulativas

### Escenario
Después de varias horas de uso.

### Logs Esperados

```
[CACHE] ═══════════════════════════════════════
[CACHE] Cache Statistics:
[CACHE]   Total Hits:    247
[CACHE]   Total Misses:  28
[CACHE]   Hit Rate:      89.8%
[CACHE]   Cached Items:  15
[CACHE] ═══════════════════════════════════════
```

**Interpretación**:
- 89.8% de las búsquedas usaron caché ✅ Excelente
- Solo 28 misses de 275 intentos
- 15 partidos cacheados actualmente

---

## Gráfico de Performance Over Time

```
Tiempo(s) │ Operación              │ Duración │ Fuente
──────────┼────────────────────────┼──────────┼─────────
0-4       │ Scrape Match 1         │ 4.2s     │ Playwright
4-8       │ Scrape Match 2         │ 4.1s     │ Playwright
8-12      │ Scrape Match 3         │ 3.9s     │ Playwright
──────────┼────────────────────────┼──────────┼─────────
Total (1º scrape): 12.2s
Hit Rate: 0% (todas nuevas)

──────────┼────────────────────────┼──────────┼─────────
12-12.1   │ Fetch Match 1          │ 0.05s    │ Caché
12.1-12.2 │ Fetch Match 2          │ 0.04s    │ Caché
12.2-12.3 │ Fetch Match 3          │ 0.05s    │ Caché
──────────┼────────────────────────┼──────────┼─────────
Total (2º scrape): 0.14s
Hit Rate: 100% (todas cacheadas)

SPEEDUP: 12.2s / 0.14s = 87x más rápido
```

---

## Fórmulas de Cálculo

### Hit Rate

```
Hit Rate = (Hits / (Hits + Misses)) * 100

Ejemplo:
Hits:   100
Misses: 20
Total:  120
Hit Rate = (100 / 120) * 100 = 83.3%
```

### Time Saved per Hit

```
Time Saved = (Scrape Time - Cache Lookup Time) * Hits

Ejemplo:
Scrape Time:        4.5s
Cache Lookup Time:  0.05s
Time per Hit:       4.5 - 0.05 = 4.45s
Hits:               100
Total Saved:        4.45s * 100 = 445s = 7.4 minutos
```

---

## Recomendaciones

✅ **Good Hit Rate**: >80%  
✅ **Optimal Hit Rate**: >90%  
⚠️ **Warning Hit Rate**: <50%  
❌ **Poor Hit Rate**: <20%  

### Si Hit Rate es bajo:

1. Verificar que se están scrapendo los mismos partidos
2. Aumentar `SCRAPING_CACHE_DURATION_MS` (más tiempo en caché)
3. Validar que no se está limpiando caché manualmente

---

## Comparativa: Con vs Sin Caché

### Escenario: 10 scrapes de 5 partidos cada uno

**SIN CACHÉ**:
```
10 scrapes × 5 partidos × 4.5s = 225 segundos
```

**CON CACHÉ**:
```
Primer scrape:  5 × 4.5s = 22.5s
9 scrapes siguientes: 9 × 0.2s = 1.8s (caché hits)
─────────────────────────────
Total: 24.3 segundos
```

**Mejora**: 225s → 24.3s = **9.3x más rápido**

---

## Debug y Troubleshooting

### ¿Por qué hit rate bajo?

```csharp
// Verificar en consola
if (_cacheStatsHits < _cacheStatsMisses)
{
    // Posibles causas:
    // 1. IDs de partido cambian entre scrapes
    // 2. Caché expira muy rápido
    // 3. Se limpian partidos frecuentemente
}
```

### Reiniciar estadísticas

```csharp
OverlayForm.Instance?.ClearScrapingCache();
// Inicia con: Hits: 0, Misses: 0
```

### Ver caché en debugger

```csharp
// En Debug Console
_scrapingCache.Count  // Cantidad de items
_cacheStatsHits       // Total de hits
_cacheStatsMisses     // Total de misses
```

