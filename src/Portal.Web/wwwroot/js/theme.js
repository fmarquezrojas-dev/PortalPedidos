(() => {
    "use strict";

    const storageKey = "portalpedidos.theme";
    const root = document.documentElement;
    const validTheme = value => value === "dark" || value === "light";

    function savedTheme() {
        try {
            const value = window.localStorage.getItem(storageKey);
            return validTheme(value) ? value : "light";
        } catch {
            // La preferencia es opcional; el portal funciona si el almacenamiento está bloqueado.
            return "light";
        }
    }

    function applyTheme(theme) {
        const dark = theme === "dark";
        root.setAttribute("data-bs-theme", dark ? "dark" : "light");
        root.style.colorScheme = dark ? "dark" : "light";

        document.querySelectorAll("[data-theme-toggle]").forEach(button => {
            button.hidden = false;
            button.setAttribute("aria-pressed", String(dark));
            button.title = dark ? "Activar modo claro" : "Activar modo noche";
            const label = button.querySelector("[data-theme-label]");
            if (label) label.textContent = dark ? "Modo día" : "Modo noche";
        });
    }

    // Se ejecuta en <head>, antes de las hojas de estilo, para aplicar la preferencia al pintar.
    applyTheme(savedTheme());

    document.addEventListener("DOMContentLoaded", () => {
        applyTheme(root.getAttribute("data-bs-theme"));
        document.querySelectorAll("[data-theme-toggle]").forEach(button => {
            button.addEventListener("click", () => {
                const next = root.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
                applyTheme(next);
                try {
                    window.localStorage.setItem(storageKey, next);
                } catch {
                    // El cambio sigue disponible para esta página sin persistencia.
                }
            });
        });
    }, { once: true });

    // Mantener coherentes otras pestañas del mismo portal.
    window.addEventListener("storage", event => {
        if (event.key === storageKey || event.key === null) applyTheme(savedTheme());
    });

    // Al volver con Atrás, recuperar la elección vigente aunque la página estuviera en caché.
    window.addEventListener("pageshow", event => {
        if (event.persisted) applyTheme(savedTheme());
    });
})();
