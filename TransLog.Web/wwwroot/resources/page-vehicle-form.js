// Vehicle form (Part 3d): make, model and variant are free text. The suggestion lists come from every
// make, model and variant known to TransLog; Model shows only the chosen make's models, Variant only that
// model's variants. Capitals, spaces and dashes do not matter ("mercedes benz" finds "Mercedes-Benz").
(function () {
    var data = document.getElementById('tl-vehicle-names');
    var make = document.querySelector('[data-tl-make]');
    var model = document.querySelector('[data-tl-model]');
    var variant = document.querySelector('[data-tl-variant]');
    if (!data || !make || !model || !variant) return;

    var names = [];
    try { names = JSON.parse(data.textContent || '[]'); } catch (e) { return; }

    function key(text) {
        return (text || '').toUpperCase().replace(/[^0-9A-Z]/g, '');
    }

    // One spelling per key: the most used one.
    function pick(values) {
        var best = {};
        values.forEach(function (v) {
            var k = key(v.text);
            if (!k) return;
            var b = best[k];
            if (!b) { best[k] = { text: v.text, uses: v.uses }; return; }
            if (b.text === v.text) b.uses += v.uses;
            else if (v.uses > b.uses) best[k] = { text: v.text, uses: v.uses };
        });
        return Object.keys(best).map(function (k) { return best[k].text; })
            .sort(function (a, b) { return a.localeCompare(b); });
    }

    function fill(listId, values) {
        var list = document.getElementById(listId);
        list.textContent = '';
        values.forEach(function (text) {
            var option = document.createElement('option');
            option.value = text;
            list.appendChild(option);
        });
    }

    function update() {
        var makeKey = key(make.value);
        var modelKey = key(model.value);
        fill('tl-makes', pick(names.map(function (n) { return { text: n.a, uses: n.u }; })));
        fill('tl-models', pick(names
            .filter(function (n) { return n.b && key(n.a) === makeKey; })
            .map(function (n) { return { text: n.b, uses: n.u }; })));
        fill('tl-variants', pick(names
            .filter(function (n) { return n.c && key(n.a) === makeKey && key(n.b) === modelKey; })
            .map(function (n) { return { text: n.c, uses: n.u }; })));
    }

    make.addEventListener('change', update);
    make.addEventListener('input', update);
    model.addEventListener('change', update);
    model.addEventListener('input', update);
    update();
})();
