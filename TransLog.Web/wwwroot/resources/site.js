// Site-wide scripts. Keep this small; page-specific scripts go in each page's Scripts section.

// Close the mobile menu after tapping a link that jumps within the page.
document.querySelectorAll('#mainNav a[href^="/#"]').forEach(function (link) {
    link.addEventListener('click', function () {
        var menu = document.getElementById('mainNav');
        if (menu.classList.contains('show')) {
            bootstrap.Collapse.getOrCreateInstance(menu).hide();
        }
    });
});

// Login details box (_LoginDetails): Copy puts the text on the clipboard; Share opens the phone's
// share sheet (WhatsApp, SMS, email) where the browser supports it.
document.querySelectorAll('[data-share]').forEach(function (box) {
    var text = box.querySelector('[data-share-text]');
    var copy = box.querySelector('[data-share-copy]');
    var send = box.querySelector('[data-share-send]');
    var done = box.querySelector('[data-share-done]');

    function copied() { done.classList.remove('d-none'); }
    function fallback() {
        text.select();
        try { if (document.execCommand('copy')) copied(); } catch (e) { /* the user can still select and copy */ }
    }

    copy.addEventListener('click', function () {
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text.value).then(copied, fallback);
        } else {
            fallback();
        }
    });

    if (navigator.share) {
        send.classList.remove('d-none');
        send.addEventListener('click', function () {
            navigator.share({ text: text.value }).catch(function () { /* cancelled */ });
        });
    }
});
