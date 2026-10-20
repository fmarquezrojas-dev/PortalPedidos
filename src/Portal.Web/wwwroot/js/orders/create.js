(() => {
    "use strict";

    const form = document.getElementById("create-order-form");

    if (!form) {
        return;
    }

    const tbody = document.getElementById("order-lines");
    const template = document.getElementById("order-line-template");
    const addButton = document.getElementById("add-order-line");
    const saveButton = document.getElementById("save-draft");
    const subtotalElement = document.getElementById("order-subtotal");

    if (!tbody || !template || !addButton ||
        !saveButton || !subtotalElement) {
        return;
    }

    const lineCount = document.getElementById("order-line-count");

    const maximumLines = 100;
    const maximumQuantity = 9999;

    let submitting = false;

    // Conserva la selección anterior para poder restaurarla
    // cuando no sea posible acumular las cantidades.
    const previousProducts = new WeakMap();

    const notice = document.createElement("div");

    notice.className = "alert alert-info";
    notice.setAttribute("role", "status");
    notice.setAttribute("aria-live", "polite");
    notice.setAttribute("aria-atomic", "true");
    notice.hidden = true;

    tbody.closest(".table-responsive")?.before(notice);

    const amountFormatter = new Intl.NumberFormat("es-PE", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    const priceFormatter = new Intl.NumberFormat("es-PE", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 4
    });

    function getRows() {
        return [...tbody.querySelectorAll(".order-line")];
    }

    function showNotice(message, isWarning = false) {
        notice.className = isWarning
            ? "alert alert-warning"
            : "alert alert-info";

        notice.textContent = message;
        notice.hidden = false;
    }

    function clearNotice() {
        notice.textContent = "";
        notice.hidden = true;
    }

    function readQuantity(input) {
        const quantity = Number(input.value);

        return Number.isInteger(quantity) &&
            quantity >= 1 &&
            quantity <= maximumQuantity
            ? quantity
            : null;
    }

    function registerSelections() {
        for (const row of getRows()) {
            const select = row.querySelector(".line-product");

            if (!previousProducts.has(select)) {
                previousProducts.set(select, select.value);
            }
        }
    }

    function updateForm() {
        const rows = getRows();
        const productCounts = new Map();

        let totalCents = 0n;

        // Detectar duplicados que puedan venir de un formulario
        // previamente mostrado. No modificar esas líneas automáticamente.
        for (const row of rows) {
            const select = row.querySelector(".line-product");

            if (select.value) {
                productCounts.set(
                    select.value,
                    (productCounts.get(select.value) ?? 0) + 1
                );
            }
        }

        rows.forEach((row, index) => {
            const productSelect = row.querySelector(".line-product");
            const quantityInput = row.querySelector(".line-quantity");
            const removeButton = row.querySelector(".remove-line");

            productSelect.name = `Lines[${index}].ProductId`;
            quantityInput.name = `Lines[${index}].Quantity`;

            productSelect.id = `Lines_${index}__ProductId`;
            quantityInput.id = `Lines_${index}__Quantity`;

            removeButton.disabled = rows.length === 1;
            productSelect.setAttribute("aria-label", `Producto de la línea ${index + 1}`);
            quantityInput.setAttribute("aria-label", `Cantidad de la línea ${index + 1}`);
            removeButton.setAttribute("aria-label", `Quitar línea ${index + 1}`);

            const duplicated =
                productSelect.value !== "" &&
                productCounts.get(productSelect.value) > 1;

            productSelect.setCustomValidity(
                duplicated
                    ? "El producto está repetido. Conserva una sola línea."
                    : ""
            );

            const selectedOption =
                productSelect.options[productSelect.selectedIndex];

            const rawPrice = selectedOption?.dataset.price;
            const quantity = readQuantity(quantityInput);

            // La vista genera los precios con formato invariante
            // y exactamente cuatro decimales.
            const validPrice =
                typeof rawPrice === "string" &&
                /^\d+\.\d{4}$/.test(rawPrice);

            if (!validPrice || quantity === null) {
                row.querySelector(".line-price").textContent = "—";
                row.querySelector(".line-amount").textContent = "—";
                return;
            }

            const priceUnits = BigInt(rawPrice.replace(".", ""));

            // Redondeo de importes no negativos:
            // de diezmilésimas a céntimos, mitad hacia arriba.
            const lineCents =
                (priceUnits * BigInt(quantity) + 50n) / 100n;

            totalCents += lineCents;

            row.querySelector(".line-price").textContent =
                priceFormatter.format(Number(rawPrice));

            row.querySelector(".line-amount").textContent =
                amountFormatter.format(Number(lineCents) / 100);
        });

        subtotalElement.textContent =
            amountFormatter.format(Number(totalCents) / 100);

        addButton.disabled = rows.length >= maximumLines;
        if (lineCount) lineCount.textContent = String(rows.length);
    }

    function handleProductChange(select) {
        const sourceRow = select.closest(".order-line");
        const productId = select.value;
        const previousProductId = previousProducts.get(select) ?? "";

        if (!sourceRow || !productId) {
            previousProducts.set(select, productId);
            return;
        }

        const matchingRows = getRows().filter(row =>
            row !== sourceRow &&
            row.querySelector(".line-product").value === productId
        );

        if (matchingRows.length === 0) {
            previousProducts.set(select, productId);
            return;
        }

        // No resolver silenciosamente un formulario que ya
        // contenga varias líneas repetidas para el mismo producto.
        if (matchingRows.length > 1) {
            select.value = previousProductId;

            showNotice(
                "Ese producto ya tiene varias líneas. " +
                "Corrige las líneas repetidas antes de continuar.",
                true
            );

            return;
        }

        const targetRow = matchingRows[0];
        const sourceInput = sourceRow.querySelector(".line-quantity");
        const targetInput = targetRow.querySelector(".line-quantity");

        const sourceQuantity = readQuantity(sourceInput);
        const targetQuantity = readQuantity(targetInput);

        if (sourceQuantity === null || targetQuantity === null) {
            select.value = previousProductId;

            showNotice(
                "No se acumularon las cantidades. " +
                "Ambas deben ser números enteros entre 1 y 9999. " +
                "Corrígelas y vuelve a seleccionar el producto.",
                true
            );

            const invalidInput = sourceQuantity === null
                ? sourceInput
                : targetInput;

            invalidInput.focus();
            invalidInput.reportValidity();

            return;
        }

        const combinedQuantity = sourceQuantity + targetQuantity;

        if (combinedQuantity > maximumQuantity) {
            select.value = previousProductId;

            showNotice(
                `La suma sería ${combinedQuantity} unidades. ` +
                "El máximo permitido por producto es 9999. " +
                "Se conservaron las líneas anteriores.",
                true
            );

            sourceInput.focus();
            return;
        }

        const productName =
            select.options[select.selectedIndex]?.textContent?.trim()
            ?? "El producto";

        targetInput.value = String(combinedQuantity);

        sourceRow.remove();

        showNotice(
            `${productName}: se acumularon las cantidades. ` +
            `La línea tiene ahora ${combinedQuantity} unidades.`
        );

        targetInput.focus();
    }

    addButton.addEventListener("click", () => {
        if (getRows().length >= maximumLines) {
            return;
        }

        clearNotice();

        tbody.appendChild(template.content.cloneNode(true));

        registerSelections();
        updateForm();

        tbody.lastElementChild
            ?.querySelector(".line-product")
            ?.focus();
    });

    tbody.addEventListener("click", event => {
        if (!(event.target instanceof Element)) {
            return;
        }

        const button = event.target.closest(".remove-line");

        if (!button || getRows().length <= 1) {
            return;
        }

        const row = button.closest(".order-line");

        if (!row) {
            return;
        }

        clearNotice();

        const nextRow = row.nextElementSibling || row.previousElementSibling;
        row.remove();
        updateForm();
        nextRow?.querySelector(".line-product")?.focus();
    });

    tbody.addEventListener("input", updateForm);

    tbody.addEventListener("change", event => {
        if (event.target instanceof HTMLSelectElement &&
            event.target.matches(".line-product")) {
            clearNotice();
            handleProductChange(event.target);
        }

        updateForm();
    });

    form.addEventListener("submit", event => {
        updateForm();

        if (event.defaultPrevented) {
            return;
        }

        if (submitting) {
            event.preventDefault();
            return;
        }

        if (!form.checkValidity()) {
            event.preventDefault();
            form.reportValidity();
            return;
        }

        submitting = true;

        // Esperar a que terminen los demás manejadores de submit.
        // Si alguno cancela el envío, el botón debe seguir disponible.
        queueMicrotask(() => {
            if (event.defaultPrevented) {
                submitting = false;
                saveButton.disabled = false;
                saveButton.textContent = "Guardar borrador";
                return;
            }

            saveButton.disabled = true;
            saveButton.textContent = "Guardando...";
        });
    });

    window.addEventListener("pageshow", () => {
        submitting = false;

        saveButton.disabled = false;
        saveButton.textContent = "Guardar borrador";

        registerSelections();
        updateForm();
    });

    registerSelections();
    updateForm();
})();