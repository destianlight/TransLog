// Photos waiting to send (Part 4a). Every photo is saved on the phone (IndexedDB) before it is sent, so nothing is lost
// when there is no signal or the browser closes. Sending happens when a photo is added, when the signal comes back,
// every 30 seconds while photos wait, and whenever a driver page opens. Each photo carries a key made on the phone,
// so a photo that is sent twice is still stored only once.
//
// Used by the camera page, the Driver home and My slips. Pages listen for the "tlqueue" event (detail: { count }).
(function () {
    'use strict';

    var DB_NAME = 'translog-capture', STORE = 'queue', UPLOAD_URL = '/driver/capture?handler=Upload';
    var dbPromise = null, sending = false;

    function openDb() {
        if (!dbPromise) {
            dbPromise = new Promise(function (resolve, reject) {
                var req = indexedDB.open(DB_NAME, 1);
                req.onupgradeneeded = function () { req.result.createObjectStore(STORE, { keyPath: 'key' }); };
                req.onsuccess = function () { resolve(req.result); };
                req.onerror = function () { reject(req.error); };
            });
        }
        return dbPromise;
    }

    function tx(mode, work) {
        return openDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var t = db.transaction(STORE, mode), store = t.objectStore(STORE), result;
                var r = work(store);
                if (r) r.onsuccess = function () { result = r.result; };
                t.oncomplete = function () { resolve(result); };
                t.onerror = function () { reject(t.error); };
                t.onabort = function () { reject(t.error); };
            });
        });
    }

    function all() {
        return tx('readonly', function (s) { return s.getAll(); }).then(function (items) {
            return (items || []).sort(function (a, b) { return a.createdAt - b.createdAt; });
        });
    }
    function put(item) { return tx('readwrite', function (s) { return s.put(item); }); }
    function remove(key) { return tx('readwrite', function (s) { return s.delete(key); }).then(notify); }

    function notify() {
        return all().then(function (items) {
            window.dispatchEvent(new CustomEvent('tlqueue', { detail: { count: items.length, items: items } }));
            return items.length;
        }).catch(function () { return 0; });
    }

    function token() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function failure(message, permanent) {
        var e = new Error(message);
        e.permanent = permanent;
        return e;
    }

    // Sends one photo. Resolves with the server's answer; rejects with .permanent = true when the server refused the
    // photo itself (it will never be accepted), false when it is worth trying again later.
    function sendOne(item, onProgress) {
        return new Promise(function (resolve, reject) {
            var fd = new FormData();
            fd.append('clientKey', item.key);
            fd.append('photo', item.photo, 'photo.jpg');
            if (item.thumb) fd.append('thumb', item.thumb, 'thumb.jpg');
            fd.append('capturedAt', String(item.capturedAt));
            fd.append('source', item.source);
            fd.append('rotation', String(item.rotation || 0));
            if (item.lat != null) {
                fd.append('lat', String(item.lat));
                fd.append('lng', String(item.lng));
                fd.append('acc', String(Math.round(item.acc || 0)));
                fd.append('locAt', String(item.locAt || 0));
            }

            var x = new XMLHttpRequest();
            x.open('POST', UPLOAD_URL);
            x.setRequestHeader('RequestVerificationToken', token());
            x.timeout = 120000;
            if (onProgress) {
                x.upload.onprogress = function (e) { if (e.lengthComputable) onProgress(Math.round(e.loaded * 100 / e.total)); };
            }
            x.onload = function () {
                var type = x.getResponseHeader('content-type') || '', json = null;
                if (type.indexOf('json') >= 0) { try { json = JSON.parse(x.responseText); } catch (e) { json = null; } }
                if (x.status === 200 && json && json.ok) resolve(json);
                else if (x.status === 400 && json && json.error) reject(failure(json.error, true));
                else if ((x.responseURL || '').indexOf('/login') >= 0) reject(failure('Log in again to send your photos.', false));
                else reject(failure('The server did not take the photo (' + x.status + '). It will try again.', false));
            };
            x.onerror = function () { reject(failure('No signal. The photo will send later.', false)); };
            x.ontimeout = function () { reject(failure('No signal. The photo will send later.', false)); };
            x.send(fd);
        });
    }

    // Sends every waiting photo, oldest first. Stops at the first photo that cannot go now (no signal).
    // hooks: onStart(item), onProgress(item, percent), onSent(item, answer), onFailed(item, message), onWait(item, message)
    function sendAll(hooks) {
        hooks = hooks || {};
        if (sending || !navigator.onLine) return notify();
        sending = true;
        return all().then(function (items) {
            var chain = Promise.resolve(), stop = false;
            items.forEach(function (item) {
                chain = chain.then(function () {
                    if (stop || item.failed) return;
                    if (hooks.onStart) hooks.onStart(item);
                    return sendOne(item, hooks.onProgress && function (p) { hooks.onProgress(item, p); })
                        .then(function (answer) {
                            return remove(item.key).then(function () { if (hooks.onSent) hooks.onSent(item, answer); });
                        }, function (e) {
                            if (e.permanent) {
                                item.failed = e.message;
                                return put(item).then(function () { if (hooks.onFailed) hooks.onFailed(item, e.message); });
                            }
                            stop = true;
                            if (hooks.onWait) hooks.onWait(item, e.message);
                        });
                });
            });
            return chain;
        }).catch(function () { /* storage not available */ }).then(function () {
            sending = false;
            return notify();
        });
    }

    function add(item) {
        item.createdAt = Date.now();
        return put(item).then(notify);
    }

    function newKey() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0;
            return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
        });
    }

    window.TLQueue = { add: add, all: all, remove: remove, sendAll: sendAll, count: notify, newKey: newKey };

    // Keep trying in the background while a driver page is open.
    window.addEventListener('online', function () { sendAll(); });
    setInterval(function () {
        if (sending) return;
        all().then(function (items) { if (items.some(function (i) { return !i.failed; })) sendAll(); }).catch(function () { });
    }, 30000);
})();
