using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace FlashscoreOverlay
{
    public partial class MainWindow : Window
    {
        private HttpListener _httpListener;
        private const string LocalServerUrl = "http://localhost:19000/";
        private bool _isWebViewReady = false;
        private ConcurrentQueue<string> _idsToRemove = new ConcurrentQueue<string>();

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int SC_MOVE = 0xF010;
        private const int MA_NOACTIVATE = 3;
        private const int HTCAPTION = 2;

        private static readonly string BaseHtml = BuildHtml();

        private static string BuildHtml()
        {
            return "<!DOCTYPE html>\n" +
"<html>\n" +
"<head>\n" +
"<meta charset='utf-8'>\n" +
"<style>\n" +
"@import url('https://fonts.googleapis.com/css2?family=Ubuntu:wght@400;500;700&display=swap');\n" +
"body {\n" +
"    background-color: transparent;\n" +
"    margin: 0; padding: 0;\n" +
"    font-family: 'Ubuntu', sans-serif;\n" +
"    color: white;\n" +
"    overflow-y: auto !important;\n" +
"    overflow-x: hidden;\n" +
"    user-select: none;\n" +
"    cursor: default;\n" +
"}\n" +
"::-webkit-scrollbar { width: 4px; }\n" +
"::-webkit-scrollbar-track { background: transparent; }\n" +
"::-webkit-scrollbar-thumb { background: #1c3542; border-radius: 2px; }\n" +

/* ── CABECERA LIGA ───────────────────────────────────────────────────── */
".fc-overlay-header {\n" +
"    background-color: #1F2F37;\n" +
"    padding: 0 10px;\n" +
"    height: 30px;\n" +
"    border-bottom: 1px solid rgba(0,0,0,0.5);\n" +
"    color: #accbd9;\n" +
"    font-size: 10px !important;\n" +
"    font-weight: 700;\n" +
"    display: flex !important;\n" +
"    align-items: center;\n" +
"    gap: 6px;\n" +
"    white-space: nowrap;\n" +
"    overflow: hidden;\n" +
"    cursor: default;\n" +
"}\n" +
/* Imagen de bandera: 20×15 via flagcdn.com */
".fc-flag-img {\n" +
"    width: 20px !important;\n" +
"    height: 15px !important;\n" +
"    min-width: 20px !important;\n" +
"    flex-shrink: 0 !important;\n" +
"    object-fit: cover;\n" +
"    border-radius: 1px;\n" +
"    display: inline-block !important;\n" +
"    vertical-align: middle;\n" +
"}\n" +
".fc-league-name {\n" +
"    white-space: nowrap;\n" +
"    overflow: hidden;\n" +
"    text-overflow: ellipsis;\n" +
"    flex: 1;\n" +
"}\n" +

/* ── FILA DE PARTIDO ──────────────────────────────────────────────────
   Columna 1 (minuto): 65px
   Columna 2 (equipos): 1fr  → más estrecha pero con flex-wrap en los jugadores
   Columna 3 (score): auto
   Columna 4+ (parciales): 0px por defecto, se expande via JS
   Filas: auto, sin altura fija → crecen para dobles (2 jugadores por equipo)
   ─────────────────────────────────────────────────────────────────── */
".event__match {\n" +
"    display: grid !important;\n" +
"    grid-template-columns: 65px 1fr auto 0px !important;\n" +
"    grid-template-rows: auto auto !important;\n" +
"    padding: 5px 8px;\n" +
"    background-color: #00141E;\n" +
"    border-bottom: 1px solid rgba(0,0,0,0.3);\n" +
"    min-height: 56px;\n" +
"    align-items: center;\n" +
"    gap: 0px 6px;\n" +
"    position: relative;\n" +
"    box-sizing: border-box;\n" +
"}\n" +
".event__match:hover { background-color: #001e2e; }\n" +
".bg-alert { background-color: #3D0314 !important; }\n" +

/* ── COL 1: MINUTO / ESTADO ──────────────────────────────────────── */
".event__stage, .event__time, .event__stage--live, .event__stage--finished {\n" +
"    grid-column: 1; grid-row: 1 / 3;\n" +
"    text-align: center;\n" +
"    font-size: 11px !important;\n" +
"    opacity: 0.9;\n" +
"    font-weight: 400;\n" +
"    display: flex;\n" +
"    justify-content: center;\n" +
"    align-items: center;\n" +
"    border-radius: 2px;\n" +
"    height: 100%;\n" +
"    align-self: stretch;\n" +
"}\n" +
".fc-blink-apos { display: inline-block; margin-left: 1px; }\n" +

/* ── COL 2: EQUIPOS ──────────────────────────────────────────────────
   Las filas del grid son auto-height.
   Para dobles, wcl-participants_ASufu apila los 2 jugadores en columna.
   ─────────────────────────────────────────────────────────────────── */
".event__homeParticipant, .event__participant--home {\n" +
"    grid-column: 2; grid-row: 1;\n" +
"    display: flex !important;\n" +
"    align-items: center !important;\n" +
"    overflow: hidden;\n" +
"    justify-content: flex-start;\n" +
"    min-width: 0;\n" +
"    padding: 4px 0;\n" +
"    min-height: 24px;\n" +
"    align-self: center;\n" +
"}\n" +
".event__awayParticipant, .event__participant--away {\n" +
"    grid-column: 2; grid-row: 2;\n" +
"    display: flex !important;\n" +
"    align-items: center !important;\n" +
"    overflow: hidden;\n" +
"    justify-content: flex-start;\n" +
"    min-width: 0;\n" +
"    padding: 4px 0;\n" +
"    min-height: 24px;\n" +
"    align-self: center;\n" +
"}\n" +

/* Contenedor de jugadores en dobles — apilados en columna */
".wcl-participants_ASufu {\n" +
"    display: flex !important;\n" +
"    flex-direction: column !important;\n" +
"    align-items: flex-start !important;\n" +
"    width: 100% !important;\n" +
"    gap: 2px 0px;\n" +
"}\n" +
/* Cada jugador dentro del contenedor */
".wcl-item_DKWjj {\n" +
"    display: flex !important;\n" +
"    align-items: center !important;\n" +
"    width: 100% !important;\n" +
"    min-width: 0;\n" +
"    white-space: nowrap;\n" +
"    overflow: hidden;\n" +
"}\n" +

"img.wcl-logo_UrSpU, .event__logo img, img[data-testid='wcl-participantLogo'] {\n" +
"    width: 14px !important; height: 14px !important;\n" +
"    margin-right: 5px !important; object-fit: contain;\n" +
"    display: block !important; flex-shrink: 0 !important;\n" +
"}\n" +
".wcl-name_jjfMf, .event__participant {\n" +
"    font-size: 12px !important; color: white; font-weight: 500;\n" +
"    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;\n" +
"    min-width: 0;\n" +
"}\n" +
".wcl-message_PBMJS {\n" +
"    margin-left: auto !important;\n" +
"    margin-right: 8px !important;\n" +
"    padding-left: 6px !important;\n" +
"    font-weight: 700 !important;\n" +
"    font-size: 11px !important;\n" +
"    text-transform: uppercase;\n" +
"    color: #C80037 !important;\n" +
"    display: inline-block !important;\n" +
"    flex-shrink: 0 !important;\n" +
"    white-space: nowrap;\n" +
"}\n" +

/* ── SVG ─────────────────────────────────────────────────────────── */
".event__match svg { display: none !important; }\n" +
".event__participant svg, .wcl-item_DKWjj svg,\n" +
".event__homeParticipant svg, .event__awayParticipant svg {\n" +
"    display: inline-block !important; visibility: visible !important;\n" +
"    width: 12px !important; height: 12px !important;\n" +
"    margin-left: 5px !important; fill: currentColor;\n" +
"    opacity: 1 !important; flex-shrink: 0;\n" +
"}\n" +

/* ── COL 3: SCORE PRINCIPAL ──────────────────────────────────────── */
".event__score--home, .event__score:nth-of-type(1) {\n" +
"    grid-column: 3; grid-row: 1;\n" +
"    font-size: 12px !important; font-weight: 700 !important;\n" +
"    display: flex; align-items: center; justify-content: flex-end;\n" +
"    white-space: nowrap; padding-right: 10px;\n" +
"    align-self: center;\n" +
"}\n" +
".event__score--away, .event__score:nth-of-type(2) {\n" +
"    grid-column: 3; grid-row: 2;\n" +
"    font-size: 12px !important; font-weight: 700 !important;\n" +
"    display: flex; align-items: center; justify-content: flex-end;\n" +
"    white-space: nowrap; padding-right: 10px;\n" +
"    align-self: center;\n" +
"}\n" +

/* Superíndice en scores de tenis (tie-break) */
".event__score--home sup, .event__score--away sup,\n" +
".event__score:nth-of-type(1) sup, .event__score:nth-of-type(2) sup,\n" +
".fc-sup {\n" +
"    font-size: 8px !important;\n" +
"    vertical-align: super;\n" +
"    line-height: 0;\n" +
"    font-weight: 700;\n" +
"}\n" +

/* ── OCULTOS ─────────────────────────────────────────────────────── */
".event__part--home, .event__part--away, .icon--preview, .event__info,\n" +
".wcl-icon_7k0gV, .eventRowLink, .wcl-favorite_ggUc2, .anclar-partido-btn,\n" +
".event__check { display: none !important; }\n" +
"</style>\n" +
"</head>\n" +
"<body>\n" +
"<div id='app'>\n" +
"    <div style='padding:10px;text-align:center;color:#567;font-size:11px;'>Esperando Datos...</div>\n" +
"</div>\n" +
"<script>\n" +
"    var LIVE_COLOR  = '#FF0043';\n" +
"    var WHITE_COLOR = '#FFFFFF';\n" +
"    var ALERT_COLOR = '#C80037';\n" +
"    var STAGE_BG    = '#3D0314';\n" +
"    var PART_COLOR  = '#7A8F99';\n" +
"\n" +
"    var scoresMemory   = {};\n" +
"    var removedMatches = {};\n" +
"    var blinkCounter   = 0;\n" +
"\n" +
"    setInterval(function() {\n" +
"        blinkCounter = blinkCounter === 0 ? 1 : 0;\n" +
"        var op = blinkCounter === 0 ? '1' : '0';\n" +
"        var els = document.querySelectorAll('.fc-blink-apos');\n" +
"        for (var i = 0; i < els.length; i++) { els[i].style.opacity = op; }\n" +
"    }, 1000);\n" +
"\n" +
"    function getStageCategory(text) {\n" +
"        if (!text) return 'empty';\n" +
"        var t = text.trim(), lower = t.toLowerCase();\n" +
"        if (t === 'HT' || lower === 'descanso') return 'halftime';\n" +
"        if (lower.indexOf('fin') !== -1 || t === 'F' || lower.indexOf('post') !== -1) return 'finished';\n" +
"        if (t.indexOf(':') !== -1) return 'scheduled';\n" +
"        if (/\\d/.test(t)) return 'live';\n" +
"        return 'other';\n" +
"    }\n" +
"\n" +
"    function shouldBeCorporateColor(text) {\n" +
"        var cat = getStageCategory(text);\n" +
"        return (cat === 'live' || cat === 'halftime');\n" +
"    }\n" +
"\n" +
"    function initMemory(id) {\n" +
"        if (id && !scoresMemory[id]) {\n" +
"            scoresMemory[id] = { home:'0', away:'0', alertExpires:0, stageAlertExpires:0, lastStageCategory:'' };\n" +
"        }\n" +
"    }\n" +
"\n" +
"    document.addEventListener('mousedown', function(e) {\n" +
"        if (e.button === 0) { window.chrome.webview.postMessage('DRAG'); }\n" +
"    });\n" +
"\n" +
"    document.addEventListener('dblclick', function(e) {\n" +
"        var hdr = e.target.closest('.fc-overlay-header');\n" +
"        if (hdr) { var href = hdr.getAttribute('data-href'); if (href) window.chrome.webview.postMessage('OPEN:' + href); }\n" +
"    });\n" +
"\n" +
"    document.addEventListener('contextmenu', function(e) {\n" +
"        var matchRow = e.target.closest('.event__match');\n" +
"        if (!matchRow) return;\n" +
"        e.preventDefault();\n" +
"        var matchId = matchRow.getAttribute('id') || matchRow.getAttribute('data-id');\n" +
"        if (!matchId) return;\n" +
"        var mc = matchRow.closest('.fc-matches-container');\n" +
"        var ls = mc ? mc.closest('.fc-league-section') : null;\n" +
"        removedMatches[matchId] = true;\n" +
"        matchRow.remove();\n" +
"        if (mc && mc.querySelectorAll('.event__match').length === 0 && ls) ls.remove();\n" +
"        window.chrome.webview.postMessage('REMOVE:' + matchId);\n" +
"        sendResize();\n" +
"    });\n" +
"\n" +

/* ─────────────────────────────────────────────────────────────────────────
   processScoreCell
   FIX: preserva <sup> (superíndices de tenis, ej. tie-break 6⁷)
   Estrategia:
   1. Extraer <sup> si existe → guardarlo separado
   2. Usar solo el texto base (sin el sup) para comparar/almacenar el número
   3. Al recomponer el HTML, volver a añadir el <sup> estilizado
   ───────────────────────────────────────────────────────────────────────── */
"    function processScoreCell(cell, matchId, isHome) {\n" +
"        if (!cell || !matchId) return false;\n" +
"        initMemory(matchId);\n" +
"\n" +
// Extraer superíndice si lo hay (tenis tie-break)
"        var supEl  = cell.querySelector('sup');\n" +
"        var supTxt = supEl ? supEl.innerText.trim() : '';\n" +
"        var raw    = cell.innerText.trim();\n" +
// Quitar el texto del sup del texto completo para obtener solo el número base
"        if (supTxt) raw = raw.replace(supTxt, '').trim();\n" +
"        var upper = raw.toUpperCase();\n" +
"\n" +
// Número limpio (sin sup)
"        if (/^\\d+$/.test(raw)) {\n" +
"            if (isHome) scoresMemory[matchId].home = raw;\n" +
"            else        scoresMemory[matchId].away = raw;\n" +
            // Reconstruir con o sin superíndice
"            cell.innerHTML = supTxt\n" +
"                ? raw + '<span class=\"fc-sup\">' + supTxt + '</span>'\n" +
"                : raw;\n" +
"            return false;\n" +
"        }\n" +
"\n" +
"        var lastNum = isHome ? scoresMemory[matchId].home : scoresMemory[matchId].away;\n" +
"        var finalText = '', cssColor = '#C80037';\n" +
"        if (upper.indexOf('GOL') !== -1 || upper.indexOf('GOAL') !== -1) { finalText = 'GOL'; }\n" +
"        else if (upper.indexOf('PENALT') !== -1) { finalText = 'PENALTI'; }\n" +
"        else if (upper.indexOf('VAR') !== -1 || upper.indexOf('REVIS') !== -1 || upper.indexOf('CORREC') !== -1) { finalText = 'REVISION'; cssColor = '#FF4444'; }\n" +
"        if (finalText !== '') {\n" +
"            cell.innerHTML = '<span style=\"font-size:11px;font-weight:700;text-transform:uppercase;color:' + cssColor + ';margin-right:6px;\">' + finalText + '</span>' +\n" +
"                             '<span style=\"font-weight:700;\">' + lastNum + '</span>';\n" +
"            return true;\n" +
"        }\n" +
"        cell.innerHTML = lastNum;\n" +
"        return false;\n" +
"    }\n" +
"\n" +

"    function getPartNum(el) {\n" +
"        var m = el.className.match(/event__part--(\\d+)/);\n" +
"        return m ? parseInt(m[1]) : 999;\n" +
"    }\n" +
"\n" +

/* ─────────────────────────────────────────────────────────────────────────
   applyLiveColors
   ───────────────────────────────────────────────────────────────────────── */
"    function applyLiveColors(container) {\n" +
"        var now = Date.now();\n" +
"        var matches = container.querySelectorAll('.event__match');\n" +
"        for (var mi = 0; mi < matches.length; mi++) {\n" +
"            var match   = matches[mi];\n" +
"            var matchId = match.getAttribute('id') || match.getAttribute('data-id');\n" +
"            if (matchId && removedMatches[matchId]) { match.remove(); continue; }\n" +
"            if (matchId) initMemory(matchId);\n" +
"\n" +
"            var stageEl   = match.querySelector('.event__stage, .event__stage--live, .event__stage--finished, .event__time');\n" +
"            var scoreHome = match.querySelector('.event__score--home');\n" +
"            var scoreAway = match.querySelector('.event__score--away');\n" +
"            var homeNode  = match.querySelector('.event__participant--home, .event__homeParticipant');\n" +
"            var awayNode  = match.querySelector('.event__participant--away, .event__awayParticipant');\n" +
"\n" +
"            var alertH = processScoreCell(scoreHome, matchId, true);\n" +
"            var alertA = processScoreCell(scoreAway, matchId, false);\n" +
"\n" +
"            var homeMsgEl = homeNode ? homeNode.querySelector('.wcl-message_PBMJS') : null;\n" +
"            var awayMsgEl = awayNode ? awayNode.querySelector('.wcl-message_PBMJS') : null;\n" +
"            var alertPart = (homeMsgEl !== null || awayMsgEl !== null);\n" +
"\n" +
"            var homeNameEls = homeNode ? homeNode.querySelectorAll('.wcl-name_jjfMf, .event__participant') : [];\n" +
"            var awayNameEls = awayNode ? awayNode.querySelectorAll('.wcl-name_jjfMf, .event__participant') : [];\n" +
"\n" +
"            for (var hn = 0; hn < homeNameEls.length; hn++) {\n" +
"                if (homeMsgEl) { homeNameEls[hn].style.setProperty('color', ALERT_COLOR, 'important'); homeNameEls[hn].style.setProperty('font-weight','700','important'); }\n" +
"                else           { homeNameEls[hn].style.setProperty('color', WHITE_COLOR, 'important'); homeNameEls[hn].style.setProperty('font-weight','500','important'); }\n" +
"            }\n" +
"            for (var an = 0; an < awayNameEls.length; an++) {\n" +
"                if (awayMsgEl) { awayNameEls[an].style.setProperty('color', ALERT_COLOR, 'important'); awayNameEls[an].style.setProperty('font-weight','700','important'); }\n" +
"                else           { awayNameEls[an].style.setProperty('color', WHITE_COLOR, 'important'); awayNameEls[an].style.setProperty('font-weight','500','important'); }\n" +
"            }\n" +
"\n" +
"            if (matchId && (alertH || alertA || alertPart)) scoresMemory[matchId].alertExpires = now + 10000;\n" +
"            var isAlertActive = matchId ? (scoresMemory[matchId].alertExpires > now) : false;\n" +
"\n" +
"            var useCorporateColor = false;\n" +
"            if (stageEl && matchId) {\n" +
"                var textClean = stageEl.innerText.replace(/['\\u2019\\u02BC]/g,'').trim();\n" +
"                var stageCat  = getStageCategory(textClean);\n" +
"                var prevCat   = scoresMemory[matchId].lastStageCategory;\n" +
"                if (prevCat !== '' && prevCat !== stageCat) scoresMemory[matchId].stageAlertExpires = now + 10000;\n" +
"                scoresMemory[matchId].lastStageCategory = stageCat;\n" +
"\n" +
"                var stageFlash = scoresMemory[matchId].stageAlertExpires > now;\n" +
"                useCorporateColor = stageFlash || shouldBeCorporateColor(textClean);\n" +
"\n" +
"                if (stageFlash) { stageEl.style.backgroundColor = STAGE_BG; stageEl.style.borderRadius = '2px'; }\n" +
"                else            { stageEl.style.backgroundColor = ''; stageEl.style.borderRadius = ''; }\n" +
"\n" +
"                if (/\\d/.test(textClean) && textClean.indexOf(':') === -1) {\n" +
"                    stageEl.innerHTML = textClean + '<span class=\"fc-blink-apos\">\\'</span>';\n" +
"                    var aposEl = stageEl.querySelector('.fc-blink-apos');\n" +
"                    if (aposEl) aposEl.style.opacity = blinkCounter === 0 ? '1' : '0';\n" +
"                } else {\n" +
"                    stageEl.innerText = textClean;\n" +
"                }\n" +
"                stageEl.style.setProperty('color', useCorporateColor ? LIVE_COLOR : WHITE_COLOR, 'important');\n" +
"                stageEl.style.setProperty('font-weight', useCorporateColor ? '700' : '400', 'important');\n" +
"            }\n" +
"\n" +
"            if (scoreHome) scoreHome.style.setProperty('color', useCorporateColor ? LIVE_COLOR : WHITE_COLOR, 'important');\n" +
"            if (scoreAway) scoreAway.style.setProperty('color', useCorporateColor ? LIVE_COLOR : WHITE_COLOR, 'important');\n" +
"\n" +
"            if (isAlertActive) match.classList.add('bg-alert');\n" +
"            else               match.classList.remove('bg-alert');\n" +
"\n" +

/* ── MARCADORES PARCIALES ──────────────────────────────────────────────────
   Recoge todos los .event__part--home y away, los ordena por número de clase.
   El score principal (col 3) se desplaza con menos padding a la derecha y los
   parciales ocupan columnas 4, 5, 6... con 22px de separación en el primero.
   ─────────────────────────────────────────────────────────────────────────── */
"            var partHomeEls = Array.prototype.slice.call(match.querySelectorAll('.event__part--home'));\n" +
"            var partAwayEls = Array.prototype.slice.call(match.querySelectorAll('.event__part--away'));\n" +
"            partHomeEls.sort(function(a,b){ return getPartNum(a)-getPartNum(b); });\n" +
"            partAwayEls.sort(function(a,b){ return getPartNum(a)-getPartNum(b); });\n" +
"            var numParts = partHomeEls.length;\n" +
"\n" +
"            if (numParts > 0) {\n" +
"                var cols = '65px 1fr auto';\n" +
"                for (var p = 0; p < numParts; p++) cols += ' auto';\n" +
"                match.style.setProperty('grid-template-columns', cols, 'important');\n" +
"                if (scoreHome) scoreHome.style.paddingRight = '4px';\n" +
"                if (scoreAway) scoreAway.style.paddingRight = '4px';\n" +
"                for (var ph = 0; ph < partHomeEls.length; ph++) {\n" +
"                    var plH = (ph === 0) ? '22px' : '6px';\n" +
"                    var prH = (ph === numParts-1) ? '10px' : '2px';\n" +
"                    partHomeEls[ph].style.cssText = 'display:flex !important;grid-column:' + (4+ph) + ';grid-row:1;' +\n" +
"                        'align-items:center;justify-content:center;min-width:18px;align-self:center;' +\n" +
"                        'padding-left:' + plH + ';padding-right:' + prH + ';' +\n" +
"                        'font-size:11px;font-weight:700;color:' + PART_COLOR + ';white-space:nowrap;';\n" +
"                }\n" +
"                for (var pa = 0; pa < partAwayEls.length; pa++) {\n" +
"                    var plA = (pa === 0) ? '22px' : '6px';\n" +
"                    var prA = (pa === numParts-1) ? '10px' : '2px';\n" +
"                    partAwayEls[pa].style.cssText = 'display:flex !important;grid-column:' + (4+pa) + ';grid-row:2;' +\n" +
"                        'align-items:center;justify-content:center;min-width:18px;align-self:center;' +\n" +
"                        'padding-left:' + plA + ';padding-right:' + prA + ';' +\n" +
"                        'font-size:11px;font-weight:700;color:' + PART_COLOR + ';white-space:nowrap;';\n" +
"                }\n" +
"            } else {\n" +
"                match.style.setProperty('grid-template-columns', '65px 1fr auto 0px', 'important');\n" +
"                if (scoreHome) scoreHome.style.paddingRight = '10px';\n" +
"                if (scoreAway) scoreAway.style.paddingRight = '10px';\n" +
"            }\n" +
"        }\n" +
"\n" +
"        var sections = container.querySelectorAll('.fc-league-section');\n" +
"        for (var si = 0; si < sections.length; si++) {\n" +
"            var mc2 = sections[si].querySelector('.fc-matches-container');\n" +
"            if (mc2 && mc2.querySelectorAll('.event__match').length === 0) sections[si].remove();\n" +
"        }\n" +
"    }\n" +
"\n" +
"    function sendResize() {\n" +
"        var app = document.getElementById('app');\n" +
"        if (!app || app.querySelectorAll('.event__match').length === 0) { window.chrome.webview.postMessage('RESIZE:80'); return; }\n" +
"        var h = app.scrollHeight;\n" +
"        window.chrome.webview.postMessage('RESIZE:' + (h < 80 ? 80 : h));\n" +
"    }\n" +
"\n" +
"    window.chrome.webview.addEventListener('message', function(event) {\n" +
"        var app     = document.getElementById('app');\n" +
"        var tempDiv = document.createElement('div');\n" +
"        tempDiv.innerHTML = event.data;\n" +
"        var incoming = tempDiv.querySelectorAll('.event__match');\n" +
"        for (var i = 0; i < incoming.length; i++) {\n" +
"            var mid = incoming[i].getAttribute('id');\n" +
"            if (mid && removedMatches[mid]) {\n" +
"                var imc = incoming[i].closest('.fc-matches-container');\n" +
"                var ils = imc ? imc.closest('.fc-league-section') : null;\n" +
"                incoming[i].remove();\n" +
"                if (imc && imc.querySelectorAll('.event__match').length === 0 && ils) ils.remove();\n" +
"            }\n" +
"        }\n" +
"        app.innerHTML = tempDiv.innerHTML;\n" +
"        var now2 = Date.now();\n" +
"        var fresh = app.querySelectorAll('.event__match');\n" +
"        for (var j = 0; j < fresh.length; j++) {\n" +
"            var fid = fresh[j].getAttribute('id') || fresh[j].getAttribute('data-id');\n" +
"            if (fid && scoresMemory[fid] && scoresMemory[fid].alertExpires > now2) fresh[j].classList.add('bg-alert');\n" +
"        }\n" +
"        applyLiveColors(app);\n" +
"        sendResize();\n" +
"    });\n" +
"</script>\n" +
"</body>\n" +
"</html>";
        }

        public MainWindow()
        {
            InitializeComponent();
            this.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00141E")
            );
            this.AllowsTransparency = true;
            this.WindowStyle = WindowStyle.None;
            this.Topmost = true;
            this.ShowInTaskbar = false;
            this.Width = 560;
            this.Height = 80;
            this.MinHeight = 80;

            InitializeWebViewAsync();
            StartWebServerAsync();
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle, WM_SYSCOMMAND, new IntPtr(SC_MOVE + HTCAPTION), IntPtr.Zero); }
                catch { }
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            src.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
            return IntPtr.Zero;
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                webView.NavigateToString(BaseHtml);
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                _isWebViewReady = true;
            }
            catch (Exception ex) { MessageBox.Show("Error WebView: " + ex.Message); }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message)) return;

            if (message == "DRAG")
            {
                try { ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle, WM_SYSCOMMAND, new IntPtr(SC_MOVE + HTCAPTION), IntPtr.Zero); }
                catch { }
                return;
            }
            if (message.StartsWith("REMOVE:")) { _idsToRemove.Enqueue(message.Substring(7).Trim()); return; }
            if (message.StartsWith("RESIZE:"))
            {
                int contentHeight;
                if (int.TryParse(message.Substring(7).Trim(), out contentHeight))
                {
                    double screenH = SystemParameters.PrimaryScreenHeight;
                    double newH = Math.Min(Math.Max((double)contentHeight, 80.0), screenH - 100.0);
                    this.Height = newH;
                }
                return;
            }
            if (message.StartsWith("OPEN:"))
            {
                string path = message.Substring(5).Trim();
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        string url = path.StartsWith("http") ? path : "https://www.flashscore.es" + path;
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                }
                return;
            }
        }

        private void StartWebServerAsync()
        {
            Task.Run(async () =>
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add(LocalServerUrl);
                _httpListener.Start();
                while (_httpListener.IsListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _ = Task.Run(() => HandleRequest(context));
                    }
                    catch { }
                }
            });
        }

        private async void HandleRequest(HttpListenerContext context)
        {
            try
            {
                context.Response.AddHeader("Access-Control-Allow-Origin", "*");
                if (context.Request.HttpMethod == "OPTIONS") { context.Response.Close(); return; }

                string bodyContent = "";
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    bodyContent = await reader.ReadToEndAsync();

                string responseString = "OK";
                string idToRemove;
                if (_idsToRemove.TryDequeue(out idToRemove))
                    responseString = "REMOVE:" + idToRemove;

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();

                if (_isWebViewReady && !string.IsNullOrEmpty(bodyContent))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        webView.CoreWebView2.PostWebMessageAsString(bodyContent);
                    });
                }
            }
            catch { }
        }
    }
}