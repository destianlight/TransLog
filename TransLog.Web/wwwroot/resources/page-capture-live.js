// Part 5b: live slips on My slips and the Captures list.
// Listens on /hubs/captures. "capture" {id, status, reading}: the row is fetched again from the server (so the progress
// bar, "Reading now…", type, slip date and what the AI read change without a refresh). "added" {id}: "1 new slip · Show".
// The row address comes from data-live on #liveNew (…?handler=Row&id=). Without signalR (old browser) nothing happens.
(function () {
    'use strict';
    var banner = document.getElementById('liveNew');
    if (!banner || !window.signalR) return;
    var rowUrl = banner.getAttribute('data-live');
    var added = {}, addedCount = 0;

    function rowOf(id) { return document.querySelector('[data-row="' + id + '"]'); }

    function refresh(id) {
        if (!rowOf(id)) return; // not on this page (other filter or page)
        fetch(rowUrl + encodeURIComponent(id), { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.text() : null; })
            .then(function (html) {
                var current = rowOf(id);
                if (!html || !current) return;
                var t = document.createElement('template');
                t.innerHTML = html.trim();
                var fresh = t.content.firstElementChild;
                if (!fresh) return;
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

    conn.on('capture', function (m) { if (m && m.id) refresh(m.id); });
    conn.on('added', function (m) {
        if (!m || !m.id || rowOf(m.id) || added[m.id]) return;
        added[m.id] = true;
        addedCount++;
        banner.querySelector('[data-count]').textContent = addedCount === 1 ? '1 new slip' : addedCount + ' new slips';
        banner.hidden = false;
    });
    conn.onreconnected(refreshBusy);
    conn.onclose(function () { setTimeout(start, 30000); });

    function start() {
        if (conn.state !== signalR.HubConnectionState.Disconnected) return;
        conn.start().then(refreshBusy).catch(function () { setTimeout(start, 15000); });
    }
    start();
})();
