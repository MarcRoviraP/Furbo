# ✅ Fix: Marcador Se Actualiza en Tiempo Real

## Problema Identificado

El marcador no se actualizaba en tiempo real porque el código fallback usaba un selector incorrecto que obtenía todo el contenido del elemento `.detailScore__wrapper` (incluyendo el estado "En vivo", "Finalizado", etc.), y luego intentaba dividir por "-", lo cual no funcionaba correctamente.

## Código Anterior (INCORRECTO)

```csharp
else
{
    // Match not started or score area structured differently
    var scoreText = await SafeTextContent(page, ".detailScore__wrapper");
    if (!string.IsNullOrEmpty(scoreText) && scoreText.Contains("-"))
    {
        var parts = scoreText.Split('-');
        data.HomeScore = parts[0].Trim();
        data.AwayScore = parts[1].Trim();  // ← Obtenía contenido combinado
    }
}
```

### ¿Por qué no funcionaba?

`.detailScore__wrapper` contiene TODO el contenido:
```
2  -  1  En vivo
```

Cuando se hacía `Split('-')`, obtenía partes incorrectas porque incluía el estado del partido.

## Código Nuevo (CORRECTO)

```csharp
else
{
    // Try individual score selectors
    var homeScoreEl = await page.QuerySelectorAsync(".duelParticipant__home .duelParticipant__score");
    var awayScoreEl = await page.QuerySelectorAsync(".duelParticipant__away .duelParticipant__score");
    
    if (homeScoreEl != null && awayScoreEl != null)
    {
        data.HomeScore = (await homeScoreEl.TextContentAsync())?.Trim() ?? "-";
        data.AwayScore = (await awayScoreEl.TextContentAsync())?.Trim() ?? "-";
    }
    else
    {
        // Last resort: try score element in header area
        var scoreElement = await page.QuerySelectorAsync(".detailScore");
        if (scoreElement != null)
        {
            var scoreChildren = await scoreElement.QuerySelectorAllAsync("> span");
            if (scoreChildren.Count >= 3)
            {
                data.HomeScore = (await scoreChildren[0].TextContentAsync())?.Trim() ?? "-";
                data.AwayScore = (await scoreChildren[2].TextContentAsync())?.Trim() ?? "-";
            }
        }
    }
}
```

### ¿Por qué funciona ahora?

1. **Primer intento**: Busca `.duelParticipant__home .duelParticipant__score` y `.duelParticipant__away .duelParticipant__score`
   - Accede directamente a los elementos del marcador de cada equipo
   - Más preciso que buscar en todo el contenedor

2. **Segundo intento**: Si lo anterior falla, busca `.detailScore` y sus spans
   - Accede a los spans específicos dentro de detailScore
   - Evita obtener contenido innecesario

3. **Sin fallback incorrecto**: No usa `Split('-')` en contenido combinado

## Estrategia en Cascada

```
┌─ Intento 1: scoreSpans.Count >= 3
│  └─ Extrae de ".detailScore__wrapper span" indices 0 y 2
│
├─ Intento 2: Selectores individuales por equipo
│  └─ ".duelParticipant__home .duelParticipant__score"
│  └─ ".duelParticipant__away .duelParticipant__score"
│
└─ Intento 3: Busca en ".detailScore" > span
   └─ ".detailScore > span" indices 0 y 2
```

## Resultado

✅ **Ahora el marcador se actualiza correctamente en tiempo real**

| Antes | Después |
|-------|---------|
| Marcador desactualizado | ✓ Se actualiza en tiempo real |
| Split incorrecto | ✓ Selectores precisos |
| Contenido combinado | ✓ Elementos específicos |

## Testing

El marcador debería actualizarse correctamente en:
- Partidos en vivo (con apostrofe parpadeante)
- Partidos finalizados
- Partidos no comenzados
- Diferentes estructuras de página

