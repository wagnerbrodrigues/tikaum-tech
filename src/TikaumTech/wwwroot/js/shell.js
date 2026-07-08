// Toggle da sidebar em JS puro. O MainLayout é renderizado estaticamente
// (interatividade por página desde a Rodada 19), então @onclick não funciona nele —
// era exatamente o bug do hamburger sem efeito. Delegação de eventos no document e
// estado em sessionStorage sobrevivem à navegação aprimorada do Blazor, que troca o
// DOM do shell a cada página; o MutationObserver reaplica as classes quando isso ocorre.
(function () {
    const CHAVE = 'tikaum-sidebar-aberta';

    function desejadaAberta() {
        const salvo = sessionStorage.getItem(CHAVE);
        if (salvo !== null) return salvo === '1';
        return window.innerWidth >= 900; // em telas estreitas o off-canvas começa fechado
    }

    function aplicar() {
        const shell = document.querySelector('.tk-shell');
        if (!shell) return;
        const aberta = desejadaAberta();
        // toggle(nome, força) só mexe no DOM quando o estado muda — o observer não entra em loop
        shell.classList.toggle('tk-sb-open', aberta);
        shell.classList.toggle('tk-sb-closed', !aberta);
    }

    function definir(aberta) {
        sessionStorage.setItem(CHAVE, aberta ? '1' : '0');
        aplicar();
    }

    document.addEventListener('click', function (e) {
        if (e.target.closest('.tk-burger')) {
            const shell = document.querySelector('.tk-shell');
            definir(!(shell && shell.classList.contains('tk-sb-open')));
        } else if (e.target.closest('.tk-sidebar-overlay')) {
            definir(false);
        } else if (window.innerWidth < 900 && e.target.closest('.tk-nav a')) {
            definir(false); // no modo off-canvas, navegar fecha a sidebar
        }
    });

    new MutationObserver(aplicar).observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', aplicar);
    aplicar();
})();
