// AI test page (Part 5a2b1). The SysAdmin picks a photo, turns it if needed and taps Read it. With "Shrink like the
// phone" the photo is made the same way as on the driver's phone (longest side 1600 px, JPEG 0.8); otherwise the file is
// sent as it is (unless it was turned). The answer comes back as HTML (partial _AiTestResult) and is shown below.
(function () {
    'use strict';

    var MAX_EDGE = 1600, QUALITY = 0.8;
    function $(id) { return document.getElementById(id); }
    var form = $('aitest'), input = $('photo'), shrink = $('shrink'), run = $('run'), status = $('status');
    var box = $('previewBox'), canvas = $('preview'), info = $('photoInfo'), result = $('result');
    var token = form.querySelector('input[name="__RequestVerificationToken"]');
    var file = null, img = null, turn = 0, busy = false;

    function draw(target, maxEdge) {
        var w = img.naturalWidth, h = img.naturalHeight;
        var scale = maxEdge ? Math.min(1, maxEdge / Math.max(w, h)) : 1;
        var dw = Math.round(w * scale), dh = Math.round(h * scale);
        var side = turn % 180 !== 0;
        target.width = side ? dh : dw;
        target.height = side ? dw : dh;
        var ctx = target.getContext('2d');
        ctx.save();
        ctx.translate(target.width / 2, target.height / 2);
        ctx.rotate(turn * Math.PI / 180);
        ctx.drawImage(img, -dw / 2, -dh / 2, dw, dh);
        ctx.restore();
    }

    function preview() {
        draw(canvas, 600);
        info.textContent = file.name + ' · ' + img.naturalWidth + ' × ' + img.naturalHeight + ' px · ' +
            Math.round(file.size / 1024) + ' KB' + (turn ? ' · turned ' + turn + '°' : '');
    }

    input.addEventListener('change', function () {
        file = input.files && input.files[0];
        img = null; turn = 0;
        run.disabled = true;
        box.classList.add('d-none');
        if (!file) return;
        var url = URL.createObjectURL(file);
        var i = new Image();
        i.onload = function () { img = i; box.classList.remove('d-none'); preview(); run.disabled = false; status.textContent = ''; };
        i.onerror = function () { status.textContent = 'This file cannot be opened here. Save it as JPEG first.'; };
        i.src = url; // the browser applies the photo's own orientation
    });

    $('turnLeft').addEventListener('click', function () { if (img) { turn = (turn + 270) % 360; preview(); } });
    $('turnRight').addEventListener('click', function () { if (img) { turn = (turn + 90) % 360; preview(); } });

    function photoBlob() {
        if (!shrink.checked && turn === 0) return Promise.resolve(file);
        var c = document.createElement('canvas');
        draw(c, shrink.checked ? MAX_EDGE : 0);
        return new Promise(function (resolve) { c.toBlob(resolve, 'image/jpeg', shrink.checked ? QUALITY : 0.92); });
    }

    form.addEventListener('submit', function (e) {
        e.preventDefault();
        if (!img || busy) return;
        busy = true;
        run.disabled = true;
        result.innerHTML = '';
        var started = Date.now();
        var timer = setInterval(function () {
            status.textContent = 'Reading… ' + Math.round((Date.now() - started) / 1000) + ' s (usually 10 to 25 s)';
        }, 500);

        photoBlob().then(function (blob) {
            var data = new FormData();
            data.append('companyId', $('companyId').value);
            data.append('providerId', $('providerId').value);
            data.append('photo', blob, 'slip.jpg');
            return fetch(form.action, {
                method: 'POST',
                body: data,
                headers: { 'RequestVerificationToken': token ? token.value : '' },
                credentials: 'same-origin'
            });
        }).then(function (r) {
            if (!r.ok) throw new Error('The server answered ' + r.status + '.');
            return r.text();
        }).then(function (html) {
            result.innerHTML = html;
            status.textContent = 'Done.';
            result.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }).catch(function (err) {
            status.textContent = 'Failed: ' + (err && err.message ? err.message : err);
        }).finally(function () {
            clearInterval(timer);
            busy = false;
            run.disabled = !img;
        });
    });

    // Copy buttons in the answer (added after the page loaded, so one listener on the result box).
    result.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-copy]');
        if (!btn) return;
        var box = btn.closest('details');
        var text = box.querySelector('textarea');
        var done = box.querySelector('[data-copied]');
        function ok() { done.classList.remove('d-none'); }
        function fallback() { text.select(); try { document.execCommand('copy'); ok(); } catch (x) { /* select is enough */ } }
        if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text.value).then(ok, fallback);
        else fallback();
    });
})();
