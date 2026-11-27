// Camera page (Part 4a, from camera test v4). The browser's own camera fills the window; buttons float over it.
// Shot -> review (turn, Retake, Use) -> shrink to 1600 px JPEG + 320 px preview -> saved on the phone -> sent (page-capture-queue.js).
(function () {
    'use strict';

    var MAX_EDGE = 1600, QUALITY = 0.8, THUMB_EDGE = 320, THUMB_QUALITY = 0.7, REVIEW_EDGE = 1200;
    var $ = function (id) { return document.getElementById(id); };
    var cam = $('cam'), video = $('video');
    var stream = null, track = null, caps = {}, imageCapture = null, photoCaps = null;
    var facing = 'environment', cameraWanted = false, torchOn = false, resumeOnShow = false, lastFix = null, busy = false, cameraCount = 0;
    var pending = null; // photo in review: { blob, bmp, fix, rot, source, takenAt }

    // ---------- Small helpers ----------
    function toBlob(c, q) { return new Promise(function (r) { c.toBlob(r, 'image/jpeg', q); }); }
    function buzz(ms) { try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) { } }
    var toastTimer;
    function toast(text, kind, ms) {
        clearTimeout(toastTimer);
        var t = $('toast');
        if (!text) { t.hidden = true; return; }
        t.textContent = text;
        t.className = 'toast glass' + (kind ? ' ' + kind : '');
        t.hidden = false;
        if (ms) toastTimer = setTimeout(function () { t.hidden = true; }, ms);
    }

    // ---------- Location: recorded with each photo; the photo still works without it ----------
    if ('geolocation' in navigator) {
        navigator.geolocation.watchPosition(function (p) {
            lastFix = { lat: p.coords.latitude, lng: p.coords.longitude, acc: p.coords.accuracy, time: p.timestamp };
        }, function () { /* denied or no fix: photos go without a location */ },
        { enableHighAccuracy: true, maximumAge: 30000, timeout: 20000 });
    }

    // ---------- Start: at once if the camera is already allowed ----------
    if (navigator.permissions && navigator.permissions.query) {
        navigator.permissions.query({ name: 'camera' }).then(function (p) {
            if (p.state === 'granted') startCamera();
            else if (p.state === 'denied') showBlocked();
        }).catch(function () { });
    }
    $('startBtn').onclick = function () { startCamera(); };

    function showStart(html) {
        $('startPanel').hidden = false;
        $('startText').innerHTML = html;
    }
    function showBlocked() {
        showStart('The camera is blocked for TransLog. Tap the icon left of the web address, then <b>Permissions › Camera › Allow</b>, and open this page again. You can still use the <b>phone camera</b> or <b>gallery</b> buttons.');
        $('liveTop').style.zIndex = 5; // keep the phone camera and gallery buttons usable above the panel
    }

    // ---------- Camera ----------
    function startCamera() {
        stopCamera();
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            showStart('This browser cannot show the camera here. Use the <b>phone camera</b> or <b>gallery</b> buttons.');
            $('liveTop').style.zIndex = 5;
            return Promise.resolve();
        }
        return navigator.mediaDevices.getUserMedia({
            video: { facingMode: { ideal: facing }, width: { ideal: 4096 }, height: { ideal: 2160 } },
            audio: false
        }).then(function (s) {
            stream = s;
            track = s.getVideoTracks()[0];
            video.srcObject = s;
            video.classList.toggle('mirror', facing === 'user');
            $('startPanel').hidden = true;
            $('liveTop').style.zIndex = '';
            return video.play().catch(function () { });
        }).then(function () {
            if (!track) return;
            caps = track.getCapabilities ? track.getCapabilities() : {};
            torchOn = false;
            $('torchBtn').classList.remove('on');
            $('torchBtn').hidden = !caps.torch;
            $('torchSpacer').hidden = !!caps.torch;
            imageCapture = null; photoCaps = null;
            if ('ImageCapture' in window) {
                imageCapture = new ImageCapture(track);
                imageCapture.getPhotoCapabilities().then(function (pc) { photoCaps = pc; }).catch(function () { });
            }
            $('shutter').disabled = false;
            cameraWanted = true;
            countCameras();
        }).catch(function (e) {
            if (e && e.name === 'NotAllowedError') showBlocked();
            else {
                showStart('The camera could not start. Close other apps that use the camera and try again, or use the <b>phone camera</b> or <b>gallery</b> buttons.');
                $('liveTop').style.zIndex = 5;
            }
        });
    }

    function stopCamera() {
        if (stream) stream.getTracks().forEach(function (t) { t.stop(); });
        stream = null; track = null; imageCapture = null;
        video.srcObject = null;
        $('shutter').disabled = true;
    }

    // The switch button shows only when the phone has more than one camera.
    function countCameras() {
        if (cameraCount || !navigator.mediaDevices.enumerateDevices) { $('switchBtn').hidden = cameraCount < 2; return; }
        navigator.mediaDevices.enumerateDevices().then(function (list) {
            cameraCount = list.filter(function (d) { return d.kind === 'videoinput'; }).length;
            $('switchBtn').hidden = cameraCount < 2;
        }).catch(function () { });
    }
    $('switchBtn').onclick = function () {
        facing = facing === 'environment' ? 'user' : 'environment';
        startCamera();
    };

    // The camera switches off when the page is hidden (battery, camera light) and back on when it returns.
    document.addEventListener('visibilitychange', function () {
        if (document.hidden) {
            if (stream) { resumeOnShow = true; stopCamera(); }
        } else if (resumeOnShow) {
            resumeOnShow = false;
            if (!pending) startCamera();
        }
        if (!document.hidden && window.TLQueue) sendWaiting();
    });

    $('torchBtn').onclick = function () {
        if (!track) return;
        track.applyConstraints({ advanced: [{ torch: !torchOn }] }).then(function () {
            torchOn = !torchOn;
            $('torchBtn').classList.toggle('on', torchOn);
        }).catch(function () { });
    };

    // ---------- Tap to focus: a ring, and focus sent to that spot if the phone allows it ----------
    $('tap').addEventListener('click', function (e) {
        if (!track || pending) return;
        var ring = document.createElement('div');
        ring.className = 'ring';
        ring.style.left = e.clientX + 'px';
        ring.style.top = e.clientY + 'px';
        cam.appendChild(ring);
        setTimeout(function () { ring.remove(); }, 1400);

        var vw = video.videoWidth, vh = video.videoHeight;
        if (!vw || !vh) return;
        var sc = Math.max(innerWidth / vw, innerHeight / vh);
        var ox = (innerWidth - vw * sc) / 2, oy = (innerHeight - vh * sc) / 2;
        var x = (e.clientX - ox) / sc / vw, y = (e.clientY - oy) / sc / vh;
        if (facing === 'user') x = 1 - x;
        var modes = caps.focusMode || [], c = { pointsOfInterest: [{ x: Math.min(1, Math.max(0, x)), y: Math.min(1, Math.max(0, y)) }] };
        if (modes.indexOf('single-shot') >= 0) c.focusMode = 'single-shot';
        else if (modes.indexOf('continuous') >= 0) c.focusMode = 'continuous';
        track.applyConstraints({ advanced: [c] }).then(function () {
            if (c.focusMode === 'single-shot' && modes.indexOf('continuous') >= 0) {
                setTimeout(function () { if (track) track.applyConstraints({ advanced: [{ focusMode: 'continuous' }] }).catch(function () { }); }, 4000);
            }
        }).catch(function () { });
    });

    // ---------- Shutter: a full-resolution still where the phone allows it, otherwise a frame from the video ----------
    function grabFrame() {
        var c = document.createElement('canvas');
        c.width = video.videoWidth; c.height = video.videoHeight;
        c.getContext('2d').drawImage(video, 0, 0);
        return toBlob(c, 0.95);
    }
    $('shutter').onclick = function () {
        if (!track || busy) return;
        busy = true;
        $('shutter').disabled = true;
        var fix = lastFix, takenAt = Date.now();
        buzz(30);
        $('flash').animate([{ opacity: 0.9 }, { opacity: 0 }], { duration: 280, easing: 'ease-out' });

        var shot;
        if (imageCapture) {
            var settings = {};
            if (photoCaps && photoCaps.imageWidth) { settings.imageWidth = photoCaps.imageWidth.max; settings.imageHeight = photoCaps.imageHeight.max; }
            shot = imageCapture.takePhoto(settings).catch(function () { return grabFrame(); });
        } else {
            shot = grabFrame();
        }
        shot.then(function (blob) { return review(blob, fix, 'camera', takenAt); })
            .catch(function () { toast('The photo did not work. Try again.', 'bad', 4000); })
            .then(function () {
                if (track && track.readyState !== 'live') startCamera();
                busy = false;
                if (track) $('shutter').disabled = false;
            });
    };

    // ---------- Phone camera and gallery go to the same review screen ----------
    function fileInput(input, source) {
        input.addEventListener('change', function () {
            var f = input.files && input.files[0];
            input.value = '';
            if (f) review(f, lastFix, source, f.lastModified || Date.now());
        });
    }
    fileInput($('nativeInput'), 'camera');
    fileInput($('galleryInput'), 'gallery');

    // ---------- Review: turn, Retake, Use ----------
    function review(blob, fix, source, takenAt) {
        return createImageBitmap(blob, { imageOrientation: 'from-image' }).then(function (bmp) {
            pending = { blob: blob, bmp: bmp, fix: fix, rot: 0, source: source, takenAt: takenAt };
            drawReview();
            $('startPanel').hidden = true;
            $('review').hidden = false; video.hidden = true;
            $('guide').hidden = true; $('hint').hidden = true;
            $('liveTop').hidden = true; $('liveBottom').hidden = true; $('reviewBottom').hidden = false;
        }, function () {
            toast('This photo cannot be read. Use a JPEG or PNG photo.', 'bad', 5000);
        });
    }
    function drawReview() {
        var c = drawScaled(pending.bmp, REVIEW_EDGE, pending.rot), rc = $('review');
        rc.width = c.width; rc.height = c.height;
        rc.getContext('2d').drawImage(c, 0, 0);
    }
    function backToCamera() {
        if (pending && pending.bmp.close) pending.bmp.close();
        pending = null;
        $('review').hidden = true; video.hidden = false;
        $('guide').hidden = false; $('hint').hidden = false;
        $('liveTop').hidden = false; $('liveBottom').hidden = false; $('reviewBottom').hidden = true;
        // Camera off (page was hidden, or it never started because it is blocked): start it again, or show the start panel.
        if (!track) {
            resumeOnShow = false;
            if (cameraWanted) startCamera(); else $('startPanel').hidden = false;
        }
    }
    $('rotL').onclick = function () { if (pending) { pending.rot = (pending.rot + 270) % 360; drawReview(); } };
    $('rotR').onclick = function () { if (pending) { pending.rot = (pending.rot + 90) % 360; drawReview(); } };
    $('retake').onclick = backToCamera;
    $('use').onclick = function () {
        if (!pending) return;
        var p = pending;
        $('use').disabled = true;
        prepare(p).then(function (item) {
            var from = $('review').getBoundingClientRect(), thumbUrl = URL.createObjectURL(item.thumb);
            return TLQueue.add(item).then(function () {
                $('use').disabled = false;
                backToCamera();
                flyToSlips(thumbUrl, from);
                sendWaiting();
            });
        }).catch(function () {
            $('use').disabled = false;
            toast('The photo could not be saved on the phone. Try again.', 'bad', 5000);
        });
    };

    // Shrinks the photo (longest side 1600 px, JPEG 0.8) and makes the preview. The original is kept only when it is a JPEG,
    // not turned, not huge, and already smaller than the shrunk copy.
    function prepare(p) {
        var W = p.bmp.width, H = p.bmp.height;
        var main = drawScaled(p.bmp, MAX_EDGE, p.rot);
        return toBlob(main, QUALITY).then(function (sent) {
            if (p.rot === 0 && p.blob.type === 'image/jpeg' && p.blob.size <= sent.size && Math.max(W, H) <= MAX_EDGE * 1.5) sent = p.blob;
            return toBlob(drawScaled(p.bmp, THUMB_EDGE, p.rot), THUMB_QUALITY).then(function (thumb) {
                var item = {
                    key: TLQueue.newKey(), photo: sent, thumb: thumb, capturedAt: p.takenAt,
                    source: p.source, rotation: p.rot
                };
                if (p.fix) { item.lat = p.fix.lat; item.lng = p.fix.lng; item.acc = p.fix.acc; item.locAt = p.fix.time; }
                return item;
            });
        });
    }

    // Draws the bitmap with its longest side at most `edge`, turned clockwise by `rot` degrees.
    function drawScaled(bmp, edge, rot) {
        var scale = Math.min(1, edge / Math.max(bmp.width, bmp.height));
        var w = Math.round(bmp.width * scale), h = Math.round(bmp.height * scale), turned = rot === 90 || rot === 270;
        var c = document.createElement('canvas');
        c.width = turned ? h : w; c.height = turned ? w : h;
        var g = c.getContext('2d');
        g.imageSmoothingQuality = 'high';
        g.translate(c.width / 2, c.height / 2);
        g.rotate(rot * Math.PI / 180);
        g.drawImage(bmp, -w / 2, -h / 2, w, h);
        return c;
    }

    // The photo shrinks into the My slips button bottom-left.
    function flyToSlips(url, from) {
        var to = $('slipsBtn').getBoundingClientRect();
        var img = document.createElement('img');
        img.src = url; img.className = 'fly'; img.alt = '';
        var w = Math.min(from.width * 0.7, 360), h = w * 4 / 3;
        img.style.left = (innerWidth - w) / 2 + 'px';
        img.style.top = (innerHeight - h) / 2 + 'px';
        img.style.width = w + 'px';
        img.style.height = h + 'px';
        document.body.appendChild(img);
        var dx = to.left + to.width / 2 - innerWidth / 2, dy = to.top + to.height / 2 - innerHeight / 2, s = to.width / w;
        img.animate([
            { transform: 'translate(0,0) scale(1)', opacity: 1, borderRadius: '10px' },
            { transform: 'translate(' + dx + 'px,' + dy + 'px) scale(' + s + ')', opacity: 0.9, borderRadius: '50%' }
        ], { duration: 450, easing: 'cubic-bezier(.5,0,.3,1)' }).onfinish = function () {
            img.remove();
            var b = $('slipsBtn');
            b.style.backgroundImage = 'url(' + url + ')';
            var icon = b.querySelector('svg');
            if (icon) icon.style.display = 'none';
            b.animate([{ transform: 'scale(1.25)' }, { transform: 'scale(1)' }], { duration: 250 });
            buzz(15);
        };
    }

    // ---------- Sending ----------
    function sendWaiting() {
        TLQueue.sendAll({
            onProgress: function (item, pct) { toast('Sending… ' + pct + '%'); },
            onSent: function () { toast('Sent', 'ok', 2000); },
            onFailed: function (item, message) { toast(message, 'bad', 6000); },
            onWait: function (item, message) { toast(message, null, 4000); }
        });
    }
    window.addEventListener('tlqueue', function (e) {
        var n = e.detail.count, badge = $('badge');
        badge.hidden = n === 0;
        badge.textContent = n;
    });
    $('slipsBtn').onclick = function () { location.href = cam.getAttribute('data-slips'); };
    sendWaiting();
})();
