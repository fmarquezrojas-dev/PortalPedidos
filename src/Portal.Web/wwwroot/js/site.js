(() => {
    "use strict";
    const sidebar = document.getElementById("portal-sidebar");
    const toggle = document.getElementById("sidebar-toggle");
    const backdrop = document.querySelector(".sidebar-backdrop");
    const workspace = document.querySelector(".portal-workspace");
    const mobile = window.matchMedia("(max-width: 991.98px)");
    let lastFocus = null;

    function setMenu(open, restoreFocus = true) {
        if (!sidebar || !toggle) return;
        open = open && mobile.matches;
        if (open) lastFocus = document.activeElement;
        document.body.classList.toggle("sidebar-open", open);
        toggle.setAttribute("aria-expanded", String(open));
        toggle.setAttribute("aria-label", open ? "Cerrar menú" : "Abrir menú");
        if (backdrop) backdrop.hidden = !open;
        sidebar.inert = mobile.matches && !open;
        if (workspace) workspace.inert = open;
        if (open) sidebar.querySelector(".sidebar-close")?.focus();
        else if (restoreFocus && lastFocus instanceof HTMLElement) lastFocus.focus();
    }
    toggle?.addEventListener("click", () => setMenu(!document.body.classList.contains("sidebar-open")));
    document.querySelectorAll("[data-sidebar-close]").forEach(button => button.addEventListener("click", () => setMenu(false)));
    document.addEventListener("keydown", event => {
        if (!document.body.classList.contains("sidebar-open")) return;
        if (event.key === "Escape") { event.preventDefault(); setMenu(false); }
        if (event.key !== "Tab") return;
        const focusable = [...sidebar.querySelectorAll("a[href],button:not([disabled])")].filter(node => node.getClientRects().length);
        const first = focusable[0], last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    });
    mobile.addEventListener("change", () => setMenu(false, false));
    setMenu(false, false);

    document.querySelectorAll("[data-password-toggle]").forEach(button => {
        const input = document.getElementById(button.dataset.passwordToggle);
        if (!(input instanceof HTMLInputElement)) return;
        button.hidden = false;
        button.addEventListener("click", () => {
            const reveal = input.type === "password";
            input.type = reveal ? "text" : "password";
            button.setAttribute("aria-pressed", String(reveal));
            button.setAttribute("aria-label", reveal ? "Ocultar contraseña" : "Mostrar contraseña");
        });
    });

    // Optativo: no modifica los formularios de pedidos que tienen su propio control.
    document.querySelectorAll("form[data-busy-form]").forEach(form => {
        const button = form.querySelector("[data-busy-button]");
        if (!button) return;
        const originalNodes = [...button.childNodes].map(node => node.cloneNode(true));
        const initiallyDisabled = button.disabled;
        let busy = false;
        function reset() {
            busy = false; button.disabled = initiallyDisabled;
            button.replaceChildren(...originalNodes.map(node => node.cloneNode(true)));
            form.removeAttribute("aria-busy");
        }
        form.addEventListener("submit", event => {
            if (busy) { event.preventDefault(); return; }
            if (event.defaultPrevented || !form.checkValidity()) return;
            if (window.jQuery?.validator && !window.jQuery(form).valid()) return;
            // El navegador construye primero los campos que enviará al servidor.
            window.setTimeout(() => {
                if (event.defaultPrevented) return;
                busy = true; button.disabled = true;
                button.textContent = button.dataset.busyLabel || "Guardando…";
                form.setAttribute("aria-busy", "true");
            }, 0);
        });
        window.addEventListener("pageshow", reset);
    });

    // Las tablas extensas también se pueden desplazar con el teclado.
    function markScrollable(element) {
        const overflows = element.scrollWidth > element.clientWidth + 1;
        if (overflows) {
            element.tabIndex = 0;
            element.setAttribute("role", "region");
            element.setAttribute("aria-label", "Tabla con desplazamiento horizontal");
        } else {
            element.removeAttribute("tabindex"); element.removeAttribute("role"); element.removeAttribute("aria-label");
        }
    }
    const tables = document.querySelectorAll(".table-responsive");
    if ("ResizeObserver" in window) {
        const observer = new ResizeObserver(entries => entries.forEach(entry => markScrollable(entry.target)));
        tables.forEach(table => { markScrollable(table); observer.observe(table); });
    } else tables.forEach(markScrollable);
})();
