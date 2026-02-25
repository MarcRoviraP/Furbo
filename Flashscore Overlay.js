// ==UserScript==
// @name         Flashscore Overlay WS (Scraping en C#)
// @namespace    http://tampermonkey.net/
// @version      31.0
// @description  Envía Match IDs a C# por WebSocket
// @author       TuNombre
// @match        https://www.flashscore.es/*
// @match        https://www.flashscore.com/*
// @grant        GM_addStyle
// @run-at       document-end
// ==/UserScript==

(function () {
  "use strict";

  const WS_URL = "ws://localhost:19000/flashscore";
  const STORAGE_KEY = "fc_overlay_v31"; // key para ids

  let ws = null;
  let wsConnected = false;
  let reconnectTimeout = null;

  function loadStorage() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : [];
    } catch (e) {
      return [];
    }
  }
  function saveStorage() {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify([...trackedMatchIds]));
    } catch (e) {}
  }

  let trackedMatchIds = new Set(loadStorage());

  // ── ESTILOS EN FLASHSCORE ────────────────────────────────────────────────
  GM_addStyle(
    ".fc-overlay-btn { position: absolute; right: 10px; top: 50%; transform: translateY(-50%); width: 14px; height: 14px; border-radius: 50%; background-color: #C80037 !important; border: none !important; cursor: pointer; z-index: 999999; box-shadow: 0 0 2px rgba(0,0,0,0.8); transition: background-color 0.2s, transform 0.2s; } .fc-overlay-btn.added { background-color: #0787FA !important; transform: translateY(-50%) scale(1.2); } .event__match { position: relative !important; }",
  );

  function connectWs() {
    if (
      ws &&
      (ws.readyState === WebSocket.OPEN ||
        ws.readyState === WebSocket.CONNECTING)
    )
      return;
    ws = new WebSocket(WS_URL);
    ws.onopen = function () {
      wsConnected = true;
      syncWithCSharp();
    };
    ws.onclose = function () {
      wsConnected = false;
      ws = null;
      clearTimeout(reconnectTimeout);
      reconnectTimeout = setTimeout(connectWs, 2000);
    };
    ws.onerror = function (err) {
      ws.close();
    };
    ws.onmessage = function (msg) {
      try {
        const data = JSON.parse(msg.data);
        if (data.action === "remove") {
          removeMatch(data.matchId);
        }
      } catch (e) {}
    };
  }

  function syncWithCSharp() {
    if (!wsConnected) return;
    ws.send(JSON.stringify({ action: "sync", ids: [...trackedMatchIds] }));
  }

  function addMatch(matchId, btn) {
    trackedMatchIds.add(matchId);
    saveStorage();
    if (btn) btn.classList.add("added");
    if (wsConnected)
      ws.send(JSON.stringify({ action: "add", matchId: matchId }));
    else syncWithCSharp();
  }

  function removeMatch(matchId) {
    trackedMatchIds.delete(matchId);
    saveStorage();
    const matchEl = document.getElementById(matchId);
    if (matchEl) {
      const btn = matchEl.querySelector(".fc-overlay-btn");
      if (btn) btn.classList.remove("added");
    }
    if (wsConnected)
      ws.send(JSON.stringify({ action: "remove", matchId: matchId }));
  }

  function createOverlayButton(matchElement) {
    if (matchElement.querySelector(".fc-overlay-btn")) return;
    const btn = document.createElement("div");
    btn.className = "fc-overlay-btn";
    if (trackedMatchIds.has(matchElement.id)) btn.classList.add("added");
    btn.addEventListener("click", function (e) {
      e.stopPropagation();
      e.preventDefault();
      const matchId = matchElement.id;
      if (!matchId) return;
      if (trackedMatchIds.has(matchId)) removeMatch(matchId);
      else addMatch(matchId, btn);
    });
    matchElement.appendChild(btn);
  }

  function addButtonsToAllMatches() {
    document
      .querySelectorAll(".event__match:not(.fc-processed)")
      .forEach(function (match) {
        match.classList.add("fc-processed");
        createOverlayButton(match);
      });
  }

  const observer = new MutationObserver(function (mutations) {
    if (mutations.some((m) => m.addedNodes.length > 0))
      addButtonsToAllMatches();
  });

  setTimeout(function () {
    connectWs();
    addButtonsToAllMatches();
    observer.observe(document.body, { childList: true, subtree: true });
  }, 1500);
})();
