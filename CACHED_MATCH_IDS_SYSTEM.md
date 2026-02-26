# ✅ Sistema de Caché de IDs de Partidos

Se ha agregado un sistema para cachear también los IDs de los partidos, permitiendo saber rápidamente cuáles ya han sido cacheados.

## Componentes Agregados

### 1. HashSet de IDs Cacheados

```csharp
private readonly HashSet<string> _cachedMatchIds = new();
```

Mantiene una lista rápida de acceso de todos los IDs que ya están cacheados.

### 2. Métodos de Gestión

#### `RegisterCachedMatchId(string matchId)`
Registra un ID en la lista de cacheados cuando se almacena en caché.

```csharp
private void RegisterCachedMatchId(string matchId)
{
    lock (_cachedMatchIds)
    {
        _cachedMatchIds.Add(matchId);
    }
}
```

#### `GetCachedMatchIds()`
Obtiene la lista completa de IDs cacheados. Útil para:
- Sincronizar con aplicaciones externas
- Depuración
- Estadísticas

```csharp
public List<string> GetCachedMatchIds()
{
    lock (_cachedMatchIds)
    {
        return _cachedMatchIds.ToList();
    }
}
```

#### `IsCachedMatch(string matchId)`
Verifica rápidamente si un ID ya está cacheado sin acceder al diccionario principal.

```csharp
public bool IsCachedMatch(string matchId)
{
    lock (_cachedMatchIds)
    {
        return _cachedMatchIds.Contains(matchId);
    }
}
```

### 3. Actualización de `InvalidateCacheForMatch()`

Ahora también remueve el ID de la lista de cacheados:

```csharp
private void InvalidateCacheForMatch(string matchId)
{
    if (_scrapingCache.TryRemove(matchId, out _))
    {
        _cachedMatchIds.Remove(matchId);  // ← AGREGADO
        Console.WriteLine($"[CACHE] Invalidated cache for {matchId}");
    }
}
```

### 4. Integración con ExtractMatchData()

Cuando se cachea un match, se registra automáticamente su ID:

```csharp
_scrapingCache.AddOrUpdate(matchId, cacheEntry, (_, __) => cacheEntry);
RegisterCachedMatchId(matchId);  // ← AGREGADO
Console.WriteLine($"[CACHE] ✓ STORED {matchId}...");
```

---

## Flujo de Funcionamiento

### Cuando se Cachea un Match

```
1. ExtractMatchData() scrappea datos
2. Crea ScrapingCacheEntry con todos los datos
3. AddOrUpdate() guarda en _scrapingCache
4. RegisterCachedMatchId() agrega ID a _cachedMatchIds
5. Se puede consultar rápidamente con IsCachedMatch()
```

### Cuando se Invalida un Match

```
1. InvalidateCacheForMatch() se llama
2. TryRemove() elimina de _scrapingCache
3. Remove() elimina de _cachedMatchIds
4. IsCachedMatch() retorna false
```

### Cuando se Consulta un Match

```
1. IsCachedMatch(matchId) → O(1) operación
2. Si retorna true → Sabemos que está cacheado
3. Si retorna false → Necesita scraping
```

---

## Ventajas

| Aspecto | Beneficio |
|--------|-----------|
| **Performance** | O(1) lookup en lugar de O(n) |
| **Sincronización** | Sabe qué IDs tiene cacheados |
| **Debugging** | Ver lista de IDs cacheados |
| **Thread-safe** | Uso de locks para acceso sincronizado |
| **Mantenimiento** | Automático cuando se cachea/invalida |

---

## Casos de Uso

### 1. Verificar si Match está Cacheado

```csharp
if (OverlayForm.Instance?.IsCachedMatch("match_123") == true)
{
    Console.WriteLine("Este match ya está cacheado");
}
```

### 2. Obtener Lista de Matches Cacheados

```csharp
var cachedIds = OverlayForm.Instance?.GetCachedMatchIds();
Console.WriteLine($"Matches cacheados: {string.Join(", ", cachedIds)}");
```

### 3. Sincronización Bidireccional

```csharp
// Verificar si un ID enviado ya está cacheado
foreach (var id in newMatchIds)
{
    if (OverlayForm.Instance?.IsCachedMatch(id) == true)
    {
        Console.WriteLine($"{id} ya estaba cacheado");
    }
}
```

---

## Logs Esperados

### Cuando se Cachea

```
[CACHE] ✓ STORED 3456789_es_1 (expires in 60 min)
```

El ID se registra automáticamente en `_cachedMatchIds`.

### Cuando se Invalida

```
[CACHE] Invalidated cache for 3456789_es_1
```

El ID se remueve automáticamente de `_cachedMatchIds`.

---

## Thread Safety

✅ Todas las operaciones sobre `_cachedMatchIds` usan `lock` para evitar race conditions.

```csharp
lock (_cachedMatchIds)
{
    // Operación segura en multithreading
}
```

---

## Performance

| Operación | Complejidad | Notas |
|-----------|-------------|-------|
| `RegisterCachedMatchId()` | O(1) | Agregar a HashSet |
| `IsCachedMatch()` | O(1) | Búsqueda en HashSet |
| `GetCachedMatchIds()` | O(n) | Copia de lista |
| `RemoveCachedMatchId()` | O(1) | Remover de HashSet |

---

## Integración con Caché Principal

| Componente | Propósito |
|-----------|-----------|
| `_scrapingCache` | Almacena datos completos (ConcurrentDict) |
| `_cachedMatchIds` | Índice rápido de IDs (HashSet) |

**Relación**: `_cachedMatchIds` es un índice de las claves de `_scrapingCache`.

