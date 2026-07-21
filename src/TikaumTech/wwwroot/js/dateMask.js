// Máscara automática dd/mm/aaaa nos campos de data (MudDatePicker com Editable="true").
//
// Por que JS puro e não o parâmetro Mask do MudBlazor: o Mask do MudDatePicker (MudPicker<T>,
// implementação separada do Mask que já funciona bem no CPF/Celular via MudTextField) tem bug
// verificado nesta versão do MudBlazor — combinado com Editable, o caret sempre volta pro
// início ao digitar, tornando o campo inutilizável (ver comentários em Vendas.razor e
// VendaEditDialog.razor). Este script só reformata o texto já digitado, em cima do <input>
// nativo, sem participar do parâmetro Mask e sem round-trip com o Blazor por tecla — o parse
// da data continua acontecendo do jeito que já funcionava (no blur/enter, dentro do próprio
// MudDatePicker), então não há conflito com o bug do Mask nem com a navegação aprimorada do
// Blazor (delegação no document, igual shell.js).
(function () {
    function formatar(digitos) {
        digitos = digitos.slice(0, 8);
        if (digitos.length > 4) return digitos.slice(0, 2) + '/' + digitos.slice(2, 4) + '/' + digitos.slice(4);
        if (digitos.length > 2) return digitos.slice(0, 2) + '/' + digitos.slice(2);
        return digitos;
    }

    document.addEventListener('input', function (e) {
        const el = e.target;
        if (!(el instanceof HTMLInputElement)) return;
        if (!el.closest('.tk-data-mascarada')) return;
        if (e.inputType && e.inputType.indexOf('delete') === 0) return; // não atrapalha backspace/delete

        const antes = el.value;
        const formatado = formatar(antes.replace(/\D/g, ''));
        if (formatado === antes) return;

        const posAntes = el.selectionStart ?? antes.length;
        const diff = formatado.length - antes.length;
        el.value = formatado;
        const posDepois = Math.max(0, posAntes + diff);
        el.setSelectionRange(posDepois, posDepois);
    }, true); // capture: roda antes de qualquer listener de blur/change do MudDatePicker
})();
