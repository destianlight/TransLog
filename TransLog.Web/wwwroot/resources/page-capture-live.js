// Part 5b: live slips on My slips and the Captures list.
// Listens on /hubs/captures. "capture" {id, status, reading}: the row is fetched again from the server (so the progress
// bar, "Reading now…", type, slip date and what the AI read change without a refresh); a removed slip (status 6) leaves
// the list when data-drop-removed="1". "added" {id} (5b2): with data-insert="1" the new row is fetched with the page's
// filters (match=true) and put on top; when it does not fit the filters, "1 new slip · Show" appears instead.
// The row address comes from data-live on #liveNew (…?handler=Row&id=). Without signalR (old browser) nothing happens.
(function () {
    'use strict';
    var banner = document.getElementById('liveNew');
    if (!banner || !window.signalR) return;
    var rowUrl = banner.getAttribute('data-live');
    var list = document.getElementById('liveList'), empty = document.getElementById('liveEmpty');
    var insert = banner.getAttribute('data-insert') === '1' && list;
    var dropRemoved = banner.getAttribute('data-drop-removed') === '1';
    var added = {}, addedCount = 0;

    function rowOf(id) { return document.querySelector('[data-row="' + id + '"]'); }

    // The page's own filters go along (the server needs them for Remove and Restore, and for match=true).
    function urlFor(id, match) {
        var q = location.search.replace(/^\?/, '');
        return rowUrl + encodeURIComponent(id) + (match ? '&match=true' : '') + (q ? '&' + q : '');
    }

    function load(id, match) {
        return fetch(urlFor(id, match), { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.text() : null; })
            .then(function (html) {
                if (!html) return null;
                var t = document.createElement('template');
                t.innerHTML = html.trim();
                return t.content.firstElementChild;
            });
    }

    function showEmpty() { if (empty && list) empty.hidden = list.querySelector('[data-row]') !== null; }

    function drop(id) {
        var row = rowOf(id);
        if (row) row.remove();
        showEmpty();
    }

    function refresh(id, status) {
        if (!rowOf(id)) return; // not on this page (other filter or page)
        if (status === 6 && dropRemoved) { drop(id); return; }
        load(id, false)
            .then(function (fresh) {
                var current = rowOf(id);
                if (!fresh || !current) return;
                // Keep "What the AI read" open when it was open.
                var wasOpen = current.querySelector('details[open]'), d = fresh.querySelector('details');
                if (wasOpen && d) d.open = true;
                current.replaceWith(fresh);
            })
            .catch(function () { /* the next message or a refresh shows it */ });
    }

    // After a lost connection: look again at the slips that may have moved meanwhile (sending, waiting, reading).
    function refreshBusy() {
        document.querySelectorAll('[data-row]').forEach(function (row) {
            if (Number(row.getAttribute('data-status')) <= 1) refresh(row.getAttribute('data-row'));
        });
    }

    var conn = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/captures')
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.None)
        .build();

    function tellNew() {
        addedCount++;
        banner.querySelector('[data-count]').textContent = addedCount === 1 ? '1 new slip' : addedCount + ' new slips';
        banner.hidden = false;
    }

    conn.on('capture', function (m) { if (m && m.id) refresh(m.id, m.status); });
    conn.on('added', function (m) {
        if (!m || !m.id || rowOf(m.id) || added[m.id]) return;
        added[m.id] = true;
        if (!insert) { tellNew(); return; }
        load(m.id, true)
            .then(function (fresh) {
                if (!fresh) { tellNew(); return; } // does not fit this page's filters
                if (rowOf(m.id)) return;
                list.insertBefore(fresh, list.firstChild);
                showEmpty();
            })
            .catch(tellNew);
    });
    conn.onreconnected(refreshBusy);
    conn.onclose(function () { setTimeout(start, 30000); });

    function start() {
        if (conn.state !== signalR.HubConnectionState.Disconnected) return;
        conn.start().then(refreshBusy).catch(function () { setTimeout(start, 15000); });
    }
    start();
})();
