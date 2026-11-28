// AI prompt edit page (Part 5a1): add and remove field rows, show the company list only for
// "Selected companies", and number the field rows again before sending (Form.Fields[0], [1], …),
// because ASP.NET stops reading a list at the first missing number.
(function () {
    var form = document.querySelector('[data-tl-prompt-form]');
    if (!form) return;

    var list = form.querySelector('[data-tl-fields]');
    var template = document.getElementById('tl-field-template');
    var addButton = form.querySelector('[data-tl-add-field]');

    function renumber() {
        if (!list) return;
        var rows = list.querySelectorAll('[data-tl-field]');
        rows.forEach(function (row, i) {
            row.querySelectorAll('[data-name]').forEach(function (input) {
                var name = 'Form.Fields[' + i + '].' + input.getAttribute('data-name');
                input.name = name;
                if (input.type === 'checkbox') {
                    input.id = name;
                    var label = input.parentNode.querySelector('label');
                    if (label) label.htmlFor = name;
                }
            });
        });
    }

    if (list && template && addButton) {
        addButton.addEventListener('click', function () {
            var row = template.content.firstElementChild.cloneNode(true);
            list.appendChild(row);
            renumber();
            var first = row.querySelector('input');
            if (first) first.focus();
        });

        list.addEventListener('click', function (e) {
            var button = e.target.closest('[data-tl-remove-field]');
            if (!button) return;
            var row = button.closest('[data-tl-field]');
            if (row) row.remove();
            renumber();
        });
    }

    // Company tick boxes only matter for "Selected companies".
    var companies = form.querySelector('[data-tl-companies]');
    var scopes = form.querySelectorAll('[data-tl-scope]');
    function showCompanies() {
        if (!companies) return;
        var picked = form.querySelector('[data-tl-scope]:checked');
        companies.hidden = !picked || picked.value !== 'SelectedCompanies';
    }
    scopes.forEach(function (radio) { radio.addEventListener('change', showCompanies); });
    showCompanies();

    form.addEventListener('submit', renumber);
})();
