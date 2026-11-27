// Vehicle form (Part 3c): the Model list shows only the models of the chosen make;
// "Other" under Make shows two boxes to type the make and model instead.
(function () {
    var make = document.querySelector('[data-tl-make]');
    var model = document.querySelector('[data-tl-model]');
    var modelBox = document.querySelector('[data-tl-model-box]');
    var others = document.querySelectorAll('[data-tl-other]');
    if (!make || !model) return;

    function update() {
        var value = make.value;
        var isOther = value === 'other';
        var hasMake = value !== '' && !isOther;

        for (var i = 0; i < model.options.length; i++) {
            var option = model.options[i];
            var forMake = option.getAttribute('data-make');
            if (forMake === null) continue; // "Choose the model"
            var show = forMake === value;
            option.hidden = !show;
            option.disabled = !show;
            // The make is already chosen, so the model list shows just the model name.
            if (!option.getAttribute('data-full')) option.setAttribute('data-full', option.text);
            var makeName = make.options[make.selectedIndex] ? make.options[make.selectedIndex].text : '';
            option.text = show && option.getAttribute('data-full').indexOf(makeName + ' ') === 0
                ? option.getAttribute('data-full').substring(makeName.length + 1)
                : option.getAttribute('data-full');
        }
        var selected = model.options[model.selectedIndex];
        if (selected && selected.disabled) model.value = '';

        if (modelBox) modelBox.hidden = !hasMake;
        for (var j = 0; j < others.length; j++) others[j].hidden = !isOther;
    }

    make.addEventListener('change', update);
    update();
})();
