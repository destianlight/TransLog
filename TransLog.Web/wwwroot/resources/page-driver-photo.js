// Driver profile photo (Part 4c), used on the driver's own Photo page and on the office driver page.
// The photo comes from the phone camera or the gallery (no camera page). The browser applies the photo's own
// orientation, the user can turn it, and Save cuts the centre square: 600 px JPEG plus a 160 px copy, posted to
// data-url with the anti-forgery token. Markup: an element with data-photo-form, data-url and data-done (where to go after).
(function () {
    'use strict';

    var SIZE = 600, THUMB = 160, PREVIEW = 320;

    function toBlob(c, q) { return new Promise(function (r) { c.toBlob(r, 'image/jpeg', q); }); }

    // Draws the centre square of the bitmap, turned clockwise by rot degrees, into a size × size canvas.
    function square(bmp, rot, size, canvas) {
        var c = canvas || document.createElement('canvas');
        c.width = c.height = size;
        var g = c.getContext('2d');
        var scale = size / Math.min(bmp.width, bmp.height), w = bmp.width * scale, h = bmp.height * scale;
        g.imageSmoothingQuality = 'high';
        g.setTransform(1, 0, 0, 1, 0, 0);
        g.clearRect(0, 0, size, size);
        g.translate(size / 2, size / 2);
        g.rotate(rot * Math.PI / 180);
        g.drawImage(bmp, -w / 2, -h / 2, w, h);
        return c;
    }

    function decode(file) {
        return createImageBitmap(file, { imageOrientation: 'from-image' }).catch(function () { return createImageBitmap(file); });
    }

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function setup(box) {
        var q = function (sel) { return box.querySelector(sel); };
        var current = q('[data-current]'), canvas = q('[data-preview]'), pick = q('[data-pick]'), review = q('[data-review]');
        var status = q('[data-status]'), save = q('[data-save]');
        var bmp = null, rot = 0, busy = false;

        function say(text, bad) {
            status.textContent = text || '';
            status.className = 'small mt-2 ' + (bad ? 'text-danger' : 'text-body-secondary');
        }

        function showReview(on) {
            canvas.hidden = !on;
            if (current) current.hidden = on;
            review.hidden = !on;
            pick.hidden = on;
        }

        box.querySelectorAll('input[type=file]').forEach(function (input) {
            input.addEventListener('change', function () {
                var file = input.files && input.files[0];
                input.value = '';
                if (!file) return;
                say('Opening…');
                decode(file).then(function (b) {
                    if (bmp && bmp.close) bmp.close();
                    bmp = b; rot = 0;
                    square(bmp, rot, PREVIEW, canvas);
                    showReview(true);
                    say('Turn it if needed, then Save.');
                }, function () {
                    say('This photo cannot be opened. Try another one, or take a new photo.', true);
                });
            });
        });

        box.addEventListener('click', function (e) {
            var b = e.target.closest('[data-act]');
            if (!b || busy || !bmp) return;
            var act = b.getAttribute('data-act');
            if (act === 'again') { showReview(false); say(''); return; }
            rot = (rot + (act === 'right' ? 90 : 270)) % 360;
            square(bmp, rot, PREVIEW, canvas);
        });

        save.addEventListener('click', function () {
            if (!bmp || busy) return;
            busy = true;
            save.disabled = true;
            say('Saving…');
            Promise.all([toBlob(square(bmp, rot, SIZE), 0.85), toBlob(square(bmp, rot, THUMB), 0.8)]).then(function (blobs) {
                var fd = new FormData();
                fd.append('photo', blobs[0], 'photo.jpg');
                fd.append('thumb', blobs[1], 'thumb.jpg');
                return fetch(box.getAttribute('data-url'), {
                    method: 'POST',
                    body: fd,
                    headers: { 'RequestVerificationToken': token() },
                    credentials: 'same-origin'
                });
            }).then(function (r) {
                return r.json().catch(function () { return null; }).then(function (json) {
                    if (r.ok && json && json.ok) {
                        say('Saved.');
                        location.href = box.getAttribute('data-done') || location.href;
                        return;
                    }
                    throw new Error(json && json.error ? json.error : 'The photo was not saved (' + r.status + '). Try again.');
                });
            }).catch(function (e) {
                say(e && e.message && e.message !== 'Failed to fetch' ? e.message : 'No signal. Try again.', true);
                busy = false;
                save.disabled = false;
            });
        });
    }

    document.querySelectorAll('[data-photo-form]').forEach(setup);
})();
