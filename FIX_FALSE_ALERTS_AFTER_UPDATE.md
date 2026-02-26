# ✅ Fix: Alertas Falsas Después de Update

## Problema Identificado

Después del primer update, todo se iluminaba en rojo y los marcadores se ponían a "- -" porque:

1. **Extracción fallida de marcador**: Si los selectores no encontraban los marcadores, retornaban "-" como valor default
2. **Detección de cambios defectuosa**: El sistema comparaba "valor anterior" con "-" y lo interpretaba como cambio de marcador
3. **Alerta falsa**: Esto gatillaba la alerta roja durante 10 segundos

### Ejemplo del flujo incorrecto:

```
Scrape 1:
  - HomeScore: "2" (extraído correctamente)
  - AwayScore: "1" (extraído correctamente)
  
Scrape 2:
  - Falla extracción
  - HomeScore: "-" (default)
  - AwayScore: "-" (default)
  - Sistema detecta: 2 ≠ - (cambio!) 
  - Gatilla alerta roja ❌
```

## Solución Implementada

### 1. Mejora de Extracción de Marcador

Cambié de este patrón:
```csharp
data.HomeScore = (await scoreSpans[0].TextContentAsync())?.Trim() ?? "-";
```

A este patrón separado:
```csharp
var homeScoreContent = await scoreSpans[0].TextContentAsync();
var homeScoreTxt = homeScoreContent?.Trim();

if (!string.IsNullOrEmpty(homeScoreTxt))
{
    data.HomeScore = homeScoreTxt;
}
// else mantiene default "-"
```

### 2. Estrategia en Cascada Mejorada

```
Intento 1: .detailScore__wrapper span indices 0 y 2
    ↓ Si ambos son válidos (no vacíos), usar
    
Intento 2: .duelParticipant__home .duelParticipant__score (si 1 falló)
    ↓ Si ambos son válidos, usar
    
Intento 3: .detailScore > span indices 0 y 2 (si 2 falló)
    ↓ Si ambos son válidos, usar
    
Default: "-" (no se pudo extraer)
```

### 3. Lógica de Detección de Cambios Mejorada

**Antes** (INCORRECTO):
```csharp
if (existing.HomeScore != nd.HomeScore || existing.AwayScore != nd.AwayScore)
{
    nd.AlertExpiresMs = nowMs + 10000;  // ← Dispara alerta en cualquier cambio
}
```

**Después** (CORRECTO):
```csharp
// Solo trigger alert si AMBOS marcadores son válidos
bool oldScoreValid = existing.HomeScore != "-" && existing.AwayScore != "-";
bool newScoreValid = nd.HomeScore != "-" && nd.AwayScore != "-";

if (oldScoreValid && newScoreValid && 
    (existing.HomeScore != nd.HomeScore || existing.AwayScore != nd.AwayScore))
{
    nd.AlertExpiresMs = nowMs + 10000;  // ← Alerta real, no falsa
}
else
{
    nd.AlertExpiresMs = existing.AlertExpiresMs;  // ← Mantiene estado anterior
}
```

## Beneficios

✅ **Sin alertas falsas**: Solo alerta si hay cambio REAL de marcador  
✅ **Mejor extracción**: Validación de contenido antes de usar  
✅ **Cascada robusta**: Múltiples estrategias sin defaults inseguros  
✅ **Distingue casos**: Diferencia entre "-" válido y extracción fallida  

## Ejemplo del Flujo Correcto

```
Scrape 1:
  - HomeScore: "2"
  - AwayScore: "1"
  - AlertExpiresMs: 0 (sin alerta)
  
Scrape 2 (falla extracción):
  - HomeScore: "-" (falla, pero no cambia estado anterior)
  - AwayScore: "-" (falla, pero no cambia estado anterior)
  - oldScoreValid: true (2 y 1 ≠ "-")
  - newScoreValid: false ("-" y "-")
  - Resultado: NO dispara alerta ✓
  - AlertExpiresMs: mantiene anterior
  
Scrape 3 (extracción exitosa):
  - HomeScore: "2"
  - AwayScore: "2" (¡gol!)
  - oldScoreValid: true
  - newScoreValid: true
  - Cambio detectado: 1 ≠ 2
  - Resultado: Dispara alerta ✓
  - AlertExpiresMs: nowMs + 10000
```

## Casos Manejados

| Caso | Antes | Ahora |
|------|-------|-------|
| Extracción falla | ❌ Alerta falsa | ✅ Sin alerta |
| Marcador real cambia | ✓ Alerta correcta | ✓ Alerta correcta |
| Partido sin empezar (sin marcador) | ❌ Alerta al cambiar a "0-0" | ✅ Sin alerta |
| Datos incompletos | ❌ Alerta falsa | ✅ Sin alerta |

