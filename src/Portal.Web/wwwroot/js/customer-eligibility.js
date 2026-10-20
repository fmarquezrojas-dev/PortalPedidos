(function () {
    "use strict";

    function initializeNotice(notice) {
        // Evitar registrar eventos dos veces
        // si el archivo se incluye accidentalmente más de una vez.
        if (notice.dataset.initialized === "true") {
            return;
        }

        const form = notice.closest("form");

        if (!form) {
            return;
        }

        const customerField =
            form.querySelector('[name="CustomerId"]');

        const badge =
            notice.querySelector(".js-customer-eligibility-badge");

        const message =
            notice.querySelector(".js-customer-eligibility-message");

        const endpointBase = notice.dataset.endpointBase;

        if (!customerField || !badge || !message || !endpointBase) {
            return;
        }

        notice.dataset.initialized = "true";

        let activeRequest = null;
        let requestVersion = 0;

        function showStatus(badgeText, badgeClasses, messageText) {
            notice.hidden = false;

            badge.className =
                "js-customer-eligibility-badge badge " + badgeClasses;

            badge.textContent = badgeText;
            message.textContent = messageText;

            notice.setAttribute("aria-busy", "false");
        }

        function hideStatus() {
            notice.hidden = true;
            notice.setAttribute("aria-busy", "false");
            message.textContent = "";
        }

        async function checkCustomer() {
            const currentVersion = ++requestVersion;

            if (activeRequest) {
                activeRequest.abort();
                activeRequest = null;
            }

            const customerId = customerField.value.trim();

            if (!customerId ||
                customerId === "00000000-0000-0000-0000-000000000000") {
                hideStatus();
                return;
            }

            const requestController = new AbortController();
            activeRequest = requestController;

            showStatus(
                "Consultando",
                "bg-secondary",
                "Comprobando la disponibilidad actual del cliente."
            );

            notice.setAttribute("aria-busy", "true");

            try {
                const response = await fetch(
                    endpointBase + encodeURIComponent(customerId),
                    {
                        method: "GET",
                        credentials: "same-origin",
                        cache: "no-store",
                        headers: {
                            "Accept": "application/json"
                        },
                        signal: requestController.signal
                    }
                );

                // Si el usuario cambió de cliente, esta respuesta
                // ya no corresponde a la selección actual.
                if (currentVersion !== requestVersion) {
                    return;
                }

                if (response.status === 401 ||
                    response.status === 403 ||
                    response.redirected) {
                    showStatus(
                        "No verificado",
                        "bg-warning text-dark",
                        "No se pudo consultar con tu sesión actual. "
                        + "Comprueba que sigues conectado y tienes permiso."
                    );

                    return;
                }

                if (response.status === 404) {
                    showStatus(
                        "No disponible",
                        "bg-danger",
                        "El cliente ya no está disponible. "
                        + "Actualiza el formulario antes de continuar."
                    );

                    return;
                }

                if (response.status === 400) {
                    showStatus(
                        "Selección inválida",
                        "bg-danger",
                        "Selecciona un cliente válido."
                    );

                    return;
                }

                if (!response.ok) {
                    throw new Error("Eligibility request failed.");
                }

                const contentType =
                    response.headers.get("content-type") || "";

                if (!contentType.includes("application/json")) {
                    throw new Error("Unexpected eligibility response.");
                }

                const result = await response.json();

                if (currentVersion !== requestVersion) {
                    return;
                }

                if (typeof result.isEligible !== "boolean") {
                    throw new Error("Invalid eligibility result.");
                }

                if (result.isEligible) {
                    showStatus(
                        "Cliente habilitado",
                        "bg-success",
                        "El cliente está disponible para pedidos. "
                        + "Se volverá a validar al guardar."
                    );
                } else {
                    const reason =
                        typeof result.message === "string" &&
                            result.message.trim().length > 0
                            ? result.message
                            : "El cliente no está disponible para pedidos.";

                    showStatus(
                        "Cliente no habilitado",
                        "bg-danger",
                        reason
                    );
                }
            } catch (error) {
                if (currentVersion !== requestVersion ||
                    requestController.signal.aborted) {
                    return;
                }

                showStatus(
                    "No verificado",
                    "bg-warning text-dark",
                    "No se pudo comprobar la disponibilidad. "
                    + "El servidor volverá a validar al guardar."
                );
            } finally {
                if (currentVersion === requestVersion) {
                    activeRequest = null;
                    notice.setAttribute("aria-busy", "false");
                }
            }
        }

        customerField.addEventListener("change", checkCustomer);

        form.addEventListener("reset", function () {
            // Esperar a que el navegador restaure los valores.
            window.setTimeout(checkCustomer, 0);
        });

        // Comprobar también un cliente preseleccionado
        // o el cliente de un borrador abierto para editar.
        checkCustomer();
    }

    function initialize() {
        document
            .querySelectorAll(".js-customer-eligibility-notice")
            .forEach(initializeNotice);
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initialize,
            { once: true }
        );
    } else {
        initialize();
    }
})();