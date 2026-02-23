// ==UserScript==
// @name         Flashscore Overlay FINAL (Persistencia + Borrado Remoto)
// @namespace    http://tampermonkey.net/
// @version      30.0
// @description  Fix: bandera sin placeholder vacio, superindices tenis, actualizacion persistente
// @author       TuNombre
// @match        https://www.flashscore.es/*
// @match        https://www.flashscore.com/*
// @grant        GM_xmlhttpRequest
// @grant        GM_addStyle
// @run-at       document-end
// ==/UserScript==

(function() {
    'use strict';

    const CSHARP_SERVER_URL  = "http://localhost:19000/";
    const UPDATE_INTERVAL_MS = 2000;
    const STORAGE_KEY        = 'fc_overlay_v27'; // Mismo key para no perder datos existentes

    // ── MAPA NOMBRE DE PAÍS → ISO 3166-1 alpha-2 ────────────────────────────
    const COUNTRY_TO_ISO = {
        'afghanistan':'af','albania':'al','algeria':'dz','argelia':'dz',
        'andorra':'ad','angola':'ao','argentina':'ar','armenia':'am',
        'australia':'au','austria':'at',
        'azerbaijan':'az','azerbaiyan':'az','azerbaiján':'az','azerbaiyán':'az',
        'bahrain':'bh','bangladesh':'bd',
        'belarus':'by','bielorrusia':'by',
        'belgium':'be','belgica':'be','bélgica':'be',
        'bolivia':'bo',
        'bosnia':'ba','bosnia & herzegovina':'ba','bosnia and herzegovina':'ba',
        'bosnia y herzegovina':'ba','bosnia i hercegovina':'ba',
        'brazil':'br','brasil':'br','bulgaria':'bg',
        'cambodia':'kh','camboya':'kh',
        'cameroon':'cm','camerun':'cm','camerún':'cm',
        'canada':'ca','chile':'cl','china':'cn','colombia':'co','costa rica':'cr',
        'croatia':'hr','croacia':'hr','cuba':'cu',
        'cyprus':'cy','chipre':'cy',
        'czech republic':'cz','czechia':'cz','republica checa':'cz','república checa':'cz',
        'denmark':'dk','dinamarca':'dk',
        'ecuador':'ec','egypt':'eg','egipto':'eg','el salvador':'sv',
        'england':'gb-eng','inglaterra':'gb-eng',
        'estonia':'ee','ethiopia':'et','etiopia':'et','etiopía':'et',
        'faroe islands':'fo','islas feroe':'fo',
        'finland':'fi','finlandia':'fi',
        'france':'fr','francia':'fr',
        'georgia':'ge','germany':'de','alemania':'de','ghana':'gh',
        'greece':'gr','grecia':'gr','guatemala':'gt','honduras':'hn',
        'hungary':'hu','hungria':'hu','hungría':'hu',
        'iceland':'is','islandia':'is','india':'in','indonesia':'id',
        'iran':'ir','irán':'ir','iraq':'iq','irak':'iq',
        'ireland':'ie','irlanda':'ie','israel':'il',
        'italy':'it','italia':'it',
        'ivory coast':'ci','cote d\'ivoire':'ci','costa de marfil':'ci',
        'jamaica':'jm','japan':'jp','japon':'jp','japón':'jp',
        'jordan':'jo','jordania':'jo',
        'kazakhstan':'kz','kazajistan':'kz','kazajistán':'kz',
        'kenya':'ke','kenia':'ke','kosovo':'xk',
        'south korea':'kr','corea del sur':'kr','korea':'kr',
        'north korea':'kp','corea del norte':'kp',
        'kuwait':'kw','latvia':'lv','letonia':'lv',
        'lebanon':'lb','libano':'lb','líbano':'lb',
        'libya':'ly','libia':'ly','liechtenstein':'li',
        'lithuania':'lt','lituania':'lt',
        'luxembourg':'lu','luxemburgo':'lu',
        'malaysia':'my','malasia':'my','malta':'mt',
        'mexico':'mx','méxico':'mx',
        'moldova':'md','moldavia':'md',
        'monaco':'mc','mongolia':'mn','montenegro':'me',
        'morocco':'ma','marruecos':'ma',
        'netherlands':'nl','holanda':'nl','paises bajos':'nl','países bajos':'nl',
        'new zealand':'nz','nueva zelanda':'nz',
        'nicaragua':'ni','nigeria':'ng',
        'north macedonia':'mk','macedonia del norte':'mk','macedonia':'mk',
        'northern ireland':'gb-nir','irlanda del norte':'gb-nir',
        'norway':'no','noruega':'no','oman':'om',
        'pakistan':'pk','palestine':'ps','palestina':'ps',
        'panama':'pa','panamá':'pa','paraguay':'py',
        'peru':'pe','perú':'pe','philippines':'ph',
        'poland':'pl','polonia':'pl','portugal':'pt','qatar':'qa',
        'romania':'ro','rumania':'ro','rumanía':'ro',
        'russia':'ru','rusia':'ru',
        'san marino':'sm',
        'saudi arabia':'sa','arabia saudi':'sa','arabia saudí':'sa',
        'scotland':'gb-sct','escocia':'gb-sct',
        'senegal':'sn','serbia':'rs',
        'slovakia':'sk','eslovaquia':'sk',
        'slovenia':'si','eslovenia':'si',
        'south africa':'za','sudafrica':'za','sudáfrica':'za',
        'spain':'es','espana':'es','españa':'es',
        'sweden':'se','suecia':'se','switzerland':'ch','suiza':'ch',
        'syria':'sy','siria':'sy',
        'taiwan':'tw','taiwán':'tw','tajikistan':'tj','tanzania':'tz',
        'thailand':'th','tailandia':'th',
        'tunisia':'tn','tunez':'tn','túnez':'tn',
        'turkey':'tr','turquia':'tr','turquía':'tr',
        'ukraine':'ua','ucrania':'ua',
        'united arab emirates':'ae','emiratos arabes unidos':'ae','emiratos árabes unidos':'ae',
        'united kingdom':'gb','reino unido':'gb',
        'united states':'us','estados unidos':'us','usa':'us',
        'uruguay':'uy','uzbekistan':'uz','uzbekistán':'uz',
        'venezuela':'ve','vietnam':'vn',
        'wales':'gb-wls','gales':'gb-wls',
        'zambia':'zm','zimbabwe':'zw',
        'world':'un','international':'un','internacional':'un','europe':'eu',
    };

    function normalizeStr(str) {
        return str.toLowerCase()
            .replace(/[áàâä]/g,'a').replace(/[éèêë]/g,'e')
            .replace(/[íìîï]/g,'i').replace(/[óòôö]/g,'o')
            .replace(/[úùûü]/g,'u').replace(/ñ/g,'n').trim();
    }

    // Genera HTML de bandera — si no hay title válido o ISO no coincide: cadena vacía (nada visible)
    function getFlagHtml(flagSpan) {
        if (!flagSpan) return '';
        var rawTitle = (flagSpan.getAttribute('title') || '').trim();
        // Sin title → sin bandera ni placeholder (ej. tenis: fl_3473162)
        if (!rawTitle) return '';
        var iso = COUNTRY_TO_ISO[rawTitle.toLowerCase()]
               || COUNTRY_TO_ISO[normalizeStr(rawTitle)]
               || COUNTRY_TO_ISO[normalizeStr(rawTitle.split(' ')[0])]
               || null;
        // Title existe pero no está en el mapa: mostrar placeholder pequeño semitransparente
        if (!iso) return '<span style="display:inline-block;width:20px;height:15px;flex-shrink:0;' +
                         'background:rgba(255,255,255,0.1);border-radius:1px;" title="' + rawTitle + '"></span>';
        return '<img src="https://flagcdn.com/20x15/' + iso + '.png"' +
               ' class="fc-flag-img" width="20" height="15"' +
               ' title="' + rawTitle + '"' +
               ' onerror="this.style.visibility=\'hidden\'">';
    }

    // ── PERSISTENCIA ─────────────────────────────────────────────────────────
    function loadStorage() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (!raw) return { ids: [], matches: {} };
            return JSON.parse(raw);
        } catch(e) { return { ids: [], matches: {} }; }
    }
    function saveStorage() {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify({
                ids:     [...trackedMatchIds],
                matches: trackedMatchData
            }));
        } catch(e) {}
    }

    const _stored        = loadStorage();
    let trackedMatchIds  = new Set(_stored.ids   || []);
    let trackedMatchData = _stored.matches        || {};

    // ── ESTILOS EN FLASHSCORE ────────────────────────────────────────────────
    GM_addStyle(`
        .fc-overlay-btn {
            position: absolute; right: 10px; top: 50%; transform: translateY(-50%);
            width: 14px; height: 14px; border-radius: 50%;
            background-color: #C80037 !important;
            border: none !important; cursor: pointer; z-index: 999999;
            box-shadow: 0 0 2px rgba(0,0,0,0.8);
            transition: background-color 0.2s, transform 0.2s;
        }
        .fc-overlay-btn.added { background-color: #0787FA !important; transform: translateY(-50%) scale(1.2); }
        .event__match { position: relative !important; }
    `);

    // ── CABECERA DE LIGA ─────────────────────────────────────────────────────
    function getLeagueHeaderData(matchElement) {
        let body = null, cur = matchElement;
        while (cur && cur.tagName !== 'BODY') {
            let sib = cur.previousElementSibling;
            while (sib) {
                if (sib.classList && sib.classList.contains('headerLeague__body')) { body = sib; break; }
                const inner = sib.querySelector && sib.querySelector('.headerLeague__body');
                if (inner) { body = inner; break; }
                sib = sib.previousElementSibling;
            }
            if (body) break;
            cur = cur.parentElement;
        }
        if (!body) {
            const p = matchElement.parentElement;
            if (p) body = p.querySelector('.headerLeague__body');
        }
        if (!body) return { name: 'Competicion', headerInnerHtml: '<span class="fc-league-name">Competicion</span>', href: '' };

        const flagSpan   = body.querySelector('.headerLeague__flag, .icon--flag');
        const flagHtml   = getFlagHtml(flagSpan);
        const categoryEl = body.querySelector('.headerLeague__category-text');
        const category   = categoryEl ? categoryEl.innerText.trim() : '';
        const titleEl    = body.querySelector('.headerLeague__title-text');
        const linkEl     = body.querySelector('a.headerLeague__title');
        const title      = titleEl ? titleEl.innerText.trim() : (linkEl ? (linkEl.innerText||'').trim() : '');
        const href       = linkEl  ? (linkEl.getAttribute('href') || '') : '';
        const name       = category ? (category + ': ' + title) : title;

        // Solo añadir flagHtml si no es cadena vacía
        let inner = flagHtml ? flagHtml : '';
        if (category) inner += '<span style="flex-shrink:0;white-space:nowrap;">' + category + ':</span>';
        inner += '<span class="fc-league-name">' + title + '</span>';

        return { name, headerInnerHtml: inner, href };
    }

    // ── LIMPIAR CLONE ─────────────────────────────────────────────────────────
    function cleanClone(clone) {
        clone.querySelectorAll('.fc-overlay-btn, .eventRowLink, .wcl-favorite_ggUc2, .anclar-partido-btn').forEach(el => el.remove());
        return clone;
    }

    // ── BOTONES EN FLASHSCORE ────────────────────────────────────────────────
    function createOverlayButton(matchElement) {
        if (matchElement.querySelector('.fc-overlay-btn')) return;
        const btn = document.createElement('div');
        btn.className = 'fc-overlay-btn';
        if (trackedMatchIds.has(matchElement.id)) btn.classList.add('added');
        btn.addEventListener('click', function(e) {
            e.stopPropagation(); e.preventDefault();
            const matchId = matchElement.id;
            if (!matchId) return;
            if (trackedMatchIds.has(matchId)) removeMatch(matchId);
            else addMatch(matchElement, btn);
        });
        matchElement.appendChild(btn);
    }

    function addMatch(matchElement, btn) {
        const matchId = matchElement.id;
        const { name, headerInnerHtml, href } = getLeagueHeaderData(matchElement);
        const clone = cleanClone(matchElement.cloneNode(true));
        trackedMatchIds.add(matchId);
        trackedMatchData[matchId] = { leagueName: name, leagueInnerHtml: headerInnerHtml, leagueHref: href, html: clone.outerHTML };
        if (btn) btn.classList.add('added');
        saveStorage();
        updateExternalOverlay();
    }

    function removeMatch(matchId) {
        if (!trackedMatchIds.has(matchId)) return;
        trackedMatchIds.delete(matchId);
        delete trackedMatchData[matchId];
        saveStorage();
        const matchEl = document.getElementById(matchId);
        if (matchEl) { const btn = matchEl.querySelector('.fc-overlay-btn'); if (btn) btn.classList.remove('added'); }
        updateExternalOverlay();
    }

    // ── CONSTRUIR Y ENVIAR HTML ──────────────────────────────────────────────
    function updateExternalOverlay() {
        if (trackedMatchIds.size === 0) {
            sendDataToCSharp('<div style="display:flex;height:60px;align-items:center;justify-content:center;color:#667;font-size:11px;">Selecciona partidos</div>');
            return;
        }
        let storageChanged = false;
        const leaguesMap = new Map();

        trackedMatchIds.forEach(function(id) {
            const liveEl = document.getElementById(id);
            if (liveEl) {
                const { name, headerInnerHtml, href } = getLeagueHeaderData(liveEl);
                const clone = cleanClone(liveEl.cloneNode(true));
                const html  = clone.outerHTML;
                const stored = trackedMatchData[id];
                if (!stored || stored.html !== html || stored.leagueName !== name || stored.leagueInnerHtml !== headerInnerHtml) {
                    trackedMatchData[id] = { leagueName: name, leagueInnerHtml: headerInnerHtml, leagueHref: href, html };
                    storageChanged = true;
                }
                if (!leaguesMap.has(name)) leaguesMap.set(name, { innerHtml: headerInnerHtml, href, matches: [] });
                leaguesMap.get(name).matches.push(html);
            } else if (trackedMatchData[id]) {
                const d = trackedMatchData[id];
                if (!leaguesMap.has(d.leagueName)) leaguesMap.set(d.leagueName, { innerHtml: d.leagueInnerHtml || '', href: d.leagueHref || '', matches: [] });
                leaguesMap.get(d.leagueName).matches.push(d.html);
            }
        });

        if (storageChanged) saveStorage();

        let contentHtml = '';
        leaguesMap.forEach(function(data) {
            contentHtml += '<div class="fc-league-section">';
            contentHtml += '<div class="fc-overlay-header" data-href="' + (data.href || '') + '">' + data.innerHtml + '</div>';
            contentHtml += '<div class="fc-matches-container">';
            data.matches.forEach(h => { contentHtml += h; });
            contentHtml += '</div></div>';
        });

        sendDataToCSharp(contentHtml);
    }

    function sendDataToCSharp(htmlData) {
        GM_xmlhttpRequest({
            method: "POST", url: CSHARP_SERVER_URL, data: htmlData,
            headers: { "Content-Type": "text/plain" },
            onload: function(response) {
                if (response.responseText && response.responseText.startsWith("REMOVE:")) {
                    const idToRemove = response.responseText.replace("REMOVE:", "").trim();
                    if (idToRemove) removeMatch(idToRemove);
                }
            },
            onerror: function() {}
        });
    }

    setInterval(updateExternalOverlay, UPDATE_INTERVAL_MS);
    document.addEventListener('visibilitychange', function() { if (!document.hidden) updateExternalOverlay(); });
    window.addEventListener('focus', updateExternalOverlay);

    function addButtonsToAllMatches() {
        document.querySelectorAll('.event__match:not(.fc-processed)').forEach(function(match) {
            match.classList.add('fc-processed');
            createOverlayButton(match);
        });
    }

    const observer = new MutationObserver(function(mutations) {
        if (mutations.some(m => m.addedNodes.length > 0)) addButtonsToAllMatches();
    });

    setTimeout(function() {
        addButtonsToAllMatches();
        observer.observe(document.body, { childList: true, subtree: true });
        updateExternalOverlay();
    }, 1500);

})();