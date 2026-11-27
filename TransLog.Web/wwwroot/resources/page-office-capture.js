// Office capture (Part 4b). The office user picks a driver and vehicle, chooses several photos, turns them if needed and
// sends them. Each photo is shrunk in the browser the same way as on the driver's phone (longest side 1600 px, JPEG 0.8,
// plus a 320 px preview) and posted on its own to /company/capture?handler=Upload. Each photo keeps one key, so Retry
// never stores it twice. No waiting-to-send queue: the office has signal, and unsent photos are lost if the page closes
// (the browser asks first).
(function () {
    'use strict';

    var MAX_EDGE = 1600, QUALITY = 0.8, THUMB_EDGE = 320, THUMB_QUALITY = 0.7, TILE_EDGE = 300;
    var MAX_FILE = 40 * 1024 * 1024, UPLOAD_URL = '/company/capture?handler=Upload';

    function $(id) { return document.getElementById(id); }
    var driver = $('driverId'), vehicle = $('vehicleId'), input = $('files'), tiles = $('tiles');
    var sendBtn = $('send'), sendbar = $('sendbar'), summary = $('summary'), toList = $('toList');
    var sprite = tiles.getAttribute('data-sprite');
    var links = {};
    try { links = JSON.parse($('driverLinks').textContent || '{}'); } catch (e) { links = {}; }

    var items = [], sending = false;

    // ---------- The vehicle follows the chosen driver's link ----------
    driver.addEventListener('change', function () {
        var v = links[driver.value];
        vehicle.value = v ? String(v) : '';
        if (vehicle.value !== (v ? String(v) : '')) vehicle.value = '';
    });

    // ---------- Helpers ----------
    function toBlob(c, q) { return new Promise(function (r) { c.toBlob(r, 'image/jpeg', q); }); }

    function newKey() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0;
            return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
        });
    }

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function icon(name) {
        return '<svg class="bi" aria-hidden="true"><use href="' + sprite + '#' + name + '"></use></svg>';
    }

    // Draws the bitmap with its longest side at most `edge`, turned clockwise by `rot` degrees.
    function drawScaled(bmp, edge, rot, canvas) {
        var scale = Math.min(1, edge / Math.max(bmp.width, bmp.height));
        var w = Math.round(bmp.width * scale), h = Math.round(bmp.height * scale), turned = rot === 90 || rot === 270;
        var c = canvas || document.createElement('canvas');
        c.width = turned ? h : w; c.height = turned ? w : h;
        var g = c.getContext('2d');
        g.imageSmoothingQuality = 'high';
        g.translate(c.width / 2, c.height / 2);
        g.rotate(rot * Math.PI / 180);
        g.drawImage(bmp, -w / 2, -h / 2, w, h);
        return c;
    }

    function decode(file) {
        // The browser applies the photo's own orientation (EXIF) so it shows the way the camera held it.
        return createImageBitmap(file, { imageOrientation: 'from-image' }).catch(function () { return createImageBitmap(file); });
    }

    // ---------- Tiles ----------
    function addFile(file) {
        var item = { key: newKey(), file: file, rot: 0, state: 'loading', bmp: null };
        var el = document.createElement('div');
        el.className = 'tl-tile';
        el.innerHTML =
            '<div class="tl-tile-img"><canvas></canvas></div>' +
            '<div class="tl-tile-bar">' +
            '<button type="button" class="btn btn-sm btn-outline-secondary" data-act="left" aria-label="Turn left">' + icon('arrow-counterclockwise') + '</button>' +
            '<button type="button" class="btn btn-sm btn-outline-danger" data-act="remove" aria-label="Remove">' + icon('trash') + '</button>' +
            '<button type="button" class="btn btn-sm btn-outline-secondary" data-act="right" aria-label="Turn right">' + icon('arrow-clockwise') + '</button>' +
            '</div>' +
            '<div class="tl-tile-state text-body-secondary"></div>';
        item.el = el;
        item.canvas = el.querySelector('canvas');
        item.stateEl = el.querySelector('.tl-tile-state');
        el.addEventListener('click', function (e) {
            var b = e.target.closest('button[data-act]');
            if (!b || sending) return;
            var act = b.getAttribute('data-act');
            if (act === 'remove') return removeItem(item);
            if (act === 'retry') return sendItems([item]);
            if (item.state !== 'ready' && item.state !== 'failed') return;
            item.rot = (item.rot + (act === 'right' ? 90 : 270)) % 360;
            drawScaled(item.bmp, TILE_EDGE, item.rot, item.canvas);
        });
        tiles.appendChild(el);
        items.push(item);
        setState(item, 'loading', 'Opening…');

        if (!/^image\/(jpeg|png|webp)$/.test(file.type)) return setState(item, 'bad', 'Not a JPEG, PNG or WebP photo.');
        if (file.size > MAX_FILE) return setState(item, 'bad', 'The file is too big.');
        decode(file).then(function (bmp) {
            item.bmp = bmp;
            drawScaled(bmp, TILE_EDGE, 0, item.canvas);
            setState(item, 'ready', 'Ready');
        }, function () {
            setState(item, 'bad', 'This file cannot be opened. Save it as JPEG and try again.');
        });
    }

    function removeItem(item) {
        if (item.bmp && item.bmp.close) item.bmp.close();
        item.el.remove();
        items = items.filter(function (i) { return i !== item; });
        refresh();
    }

    function setState(item, state, text, tone) {
        item.state = state;
        item.el.className = 'tl-tile' + (state === 'sent' || state === 'failed' || state === 'bad' ? ' tl-tile-' + state : '');
        item.stateEl.className = 'tl-tile-state ' + (tone || (state === 'failed' || state === 'bad' ? 'text-danger' : state === 'sent' ? 'text-success' : 'text-body-secondary'));
        item.stateEl.textContent = text;
        if (state === 'failed') {
            var retry = document.createElement('button');
            retry.type = 'button'; retry.className = 'btn btn-sm btn-link p-0 ms-1'; retry.textContent = 'Retry';
            retry.setAttribute('data-act', 'retry');
            item.stateEl.appendChild(retry);
        }
        // Turning is only possible before the photo is sent; a sent photo can no longer be removed here.
        var turnable = state === 'ready' || state === 'failed';
        item.el.querySelectorAll('[data-act="left"],[data-act="right"]').forEach(function (b) { b.disabled = !turnable; });
        var rm = item.el.querySelector('[data-act="remove"]');
        if (rm) rm.hidden = state === 'sent' || state === 'sending';
        refresh();
    }

    function count(state) { return items.filter(function (i) { return i.state === state; }).length; }

    function refresh() {
        var toSend = count('ready') + count('failed'), sent = count('sent'), loading = count('loading');
        sendbar.hidden = items.length === 0;
        sendBtn.disabled = sending || toSend === 0 || loading > 0;
        sendBtn.textContent = sending ? 'Sending…' : toSend === 1 ? 'Send 1 photo' : 'Send ' + toSend + ' photos';
        var parts = [];
        if (sent) parts.push(sent + ' sent');
        if (count('failed')) parts.push(count('failed') + ' not sent');
        if (count('bad')) parts.push(count('bad') + ' cannot be used');
        summary.textContent = parts.join(' · ');
        toList.hidden = sent === 0 || sending;
        driver.disabled = vehicle.disabled = input.disabled = sending;
    }

    input.addEventListener('change', function () {
        Array.prototype.forEach.call(input.files, addFile);
        input.value = '';
    });

    // ---------- Sending, one photo at a time ----------
    // Shrinks the photo and makes the preview. The original is kept only when it is a JPEG, not turned, not huge,
    // and already smaller than the shrunk copy (the same rule as the driver's camera page).
    function prepare(item) {
        var W = item.bmp.width, H = item.bmp.height;
        return toBlob(drawScaled(item.bmp, MAX_EDGE, item.rot), QUALITY).then(function (photo) {
            if (item.rot === 0 && item.file.type === 'image/jpeg' && item.file.size <= photo.size && Math.max(W, H) <= MAX_EDGE * 1.5) photo = item.file;
            return toBlob(drawScaled(item.bmp, THUMB_EDGE, item.rot), THUMB_QUALITY).then(function (thumb) {
                return { photo: photo, thumb: thumb };
            });
        });
    }

    function post(item, files, driverId, vehicleId) {
        return new Promise(function (resolve, reject) {
            var fd = new FormData();
            fd.append('clientKey', item.key);
            fd.append('photo', files.photo, 'photo.jpg');
            fd.append('thumb', files.thumb, 'thumb.jpg');
            fd.append('source', 'office');
            fd.append('rotation', String(item.rot));
            if (driverId) fd.append('driverId', driverId);
            if (vehicleId) fd.append('vehicleId', vehicleId);

            var x = new XMLHttpRequest();
            x.open('POST', UPLOAD_URL);
            x.setRequestHeader('RequestVerificationToken', token());
            x.timeout = 120000;
            x.upload.onprogress = function (e) {
                if (e.lengthComputable) item.stateEl.textContent = 'Sending ' + Math.round(e.loaded * 100 / e.total) + '%';
            };
            x.onload = function () {
                var json = null;
                if ((x.getResponseHeader('content-type') || '').indexOf('json') >= 0) { try { json = JSON.parse(x.responseText); } catch (e) { json = null; } }
                if (x.status === 200 && json && json.ok) resolve(json);
                else if (x.status === 400 && json && json.error) reject(new Error(json.error));
                else if ((x.responseURL || '').indexOf('/login') >= 0) reject(new Error('Log in again, then tap Retry.'));
                else reject(new Error('The server did not take the photo (' + x.status + ').'));
            };
            x.onerror = x.ontimeout = function () { reject(new Error('No connection.')); };
            x.send(fd);
        });
    }

    function sendItems(list) {
        if (sending) return Promise.resolve();
        sending = true;
        var driverId = driver.value, vehicleId = vehicle.value, chain = Promise.resolve();
        list.forEach(function (item) {
            chain = chain.then(function () {
                if (item.state !== 'ready' && item.state !== 'failed') return;
                setState(item, 'sending', 'Preparing…');
                return prepare(item)
                    .then(function (files) { return post(item, files, driverId, vehicleId); })
                    .then(function () { setState(item, 'sent', 'Sent'); },
                        function (e) { setState(item, 'failed', e.message || 'Not sent.'); });
            });
        });
        return chain.then(function () { sending = false; refresh(); });
    }

    sendBtn.addEventListener('click', function () { sendItems(items.slice()); });

    // Unsent photos are lost when the page closes, so the browser asks first.
    window.addEventListener('beforeunload', function (e) {
        if (sending || count('ready') + count('failed') > 0) { e.preventDefault(); e.returnValue = ''; }
    });

    refresh();
})();
