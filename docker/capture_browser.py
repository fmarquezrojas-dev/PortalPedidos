"""Chromium real, formularios MVC, comprobaciones persistidas en la demo aislada."""
import json
import re
import time
import uuid
from contextlib import contextmanager
from datetime import datetime, timedelta
from decimal import Decimal

from playwright.sync_api import expect, sync_playwright


@contextmanager
def chromium(demo):
    certificate = (demo.PRIVATE / "portal.crt").read_text()
    fingerprint = demo.process(demo.COMPOSE + ["exec", "-T", "portal", "bash", "-o", "pipefail", "-c",
        "openssl x509 -pubkey -noout | openssl pkey -pubin -outform der | "
        "openssl dgst -sha256 -binary | openssl base64 -A"], certificate).strip()
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(args=["--ignore-certificate-errors-spki-list=" + fingerprint])
        try:
            yield browser
        finally:
            browser.close()


def login(browser, demo, role, errors):
    context = browser.new_context(base_url=demo.BASE, locale="es-PE")
    page = context.new_page()
    page.on("pageerror", lambda error: errors.append(str(error)))
    page.goto("/Account/Login")
    page.locator("#Email").fill(role + "@portal.test")
    page.locator("#Password").fill((demo.PRIVATE / "seed-password").read_text().strip())
    page.locator("form button[type=submit]").click()
    expect(page).not_to_have_url(re.compile("/Account/Login"))
    return page


def choose(page, index, code):
    cell = page.locator("#customer-search").locator("..") if index is None else page.locator(".order-line td:first-child").nth(index)
    search, select = cell.locator(".capture-search"), cell.locator("select")
    search.fill(code)
    option = select.locator("option").filter(has_text=re.compile("^" + code + " -"))
    expect(option).to_have_count(1)
    value = option.get_attribute("value")
    if not select.input_value():
        search.press("ArrowDown")
        expect(select).to_be_focused()
        select.press("ArrowDown")
    else:
        select.select_option(value)
    expect(select).to_have_value(value)
    return value


def save(page):
    if page.locator("#AcceptCurrentPrices").count():
        page.locator("#AcceptCurrentPrices").check()
    with page.expect_response(lambda r: r.request.method == "POST" and "/Orders/" in r.url) as submission:
        page.locator("#save-draft").click()
    page.wait_for_load_state()
    return submission.value.status


def check_search_states(page):
    search = page.locator("#customer-search")
    status = page.locator("#customer-search-status")
    terms = []
    pattern = "**/SearchCustomers?*"

    def record(route):
        term = route.request.url.split("term=")[-1]
        terms.append(term)
        if term == "fallo":
            route.fulfill(status=503, body="")
        else:
            route.continue_()

    page.route(pattern, record)
    try:
        search.press_sequentially("inexistente", delay=5)
        expect(status).to_have_text("Sin coincidencias.")
        assert terms == ["inexistente"]
        search.fill("fallo")
        expect(status).to_contain_text("No se pudo buscar")
        search.fill("TC")
        expect(status).to_contain_text("Hay más coincidencias")
        expect(page.locator("#CustomerId option")).to_have_count(21)
        page.get_by_role("button", name="Ver más coincidencias").click()
        expect(page.locator("#CustomerId option")).to_have_count(41)
        expect(page.locator("#CustomerId option").last).to_contain_text("TC0040")
        # Retener una respuesta ya recibida y entregarla después de otra búsqueda.
        page.evaluate("""() => {
            const original = window.fetch;
            window.fetch = async (...args) => {
                const response = await original(...args);
                if (new URL(args[0]).searchParams.get('term') === 'TC0002') {
                    await new Promise(resolve => { window.releaseOldSearch = resolve; });
                }
                return response;
            };
            window.restoreSearch = () => { window.fetch = original; };
        }""")
        search.fill("TC0002")
        page.wait_for_function("() => typeof window.releaseOldSearch === 'function'")
        search.fill("TC0003")
        expect(page.locator("#CustomerId option").last).to_contain_text("TC0003")
        page.evaluate("async () => { window.releaseOldSearch(); await new Promise(requestAnimationFrame); window.restoreSearch(); }")
        expect(page.locator("#CustomerId option")).to_have_count(2)
        expect(page.locator("#CustomerId option").last).to_contain_text("TC0003")
    finally:
        page.unroute(pattern, record)


def draft(page, customer, lines, check_validation=False):
    page.goto("/Orders")
    page.get_by_role("link", name="Nuevo pedido", exact=True).last.click()
    oid = str(uuid.UUID(page.locator("#DraftId").input_value()))
    if check_validation:
        check_search_states(page)
    customer_id = choose(page, None, customer)
    products = []
    for i, (code, quantity) in enumerate(lines):
        if i:
            page.locator("#add-order-line").click()
        products.append(choose(page, i, code))
        page.locator(".line-quantity").nth(i).fill(str(quantity))
    if check_validation:
        # El servidor debe reconstruir incluso una petición que un navegador suele bloquear.
        first = page.locator(".line-quantity").first
        first.evaluate("input => input.removeAttribute('min')")
        first.fill("0")
        assert save(page) == 200
        expect(page.get_by_role("alert").filter(has_text="entre 1 y 9999")).to_be_visible()
        assert page.locator("#CustomerId").input_value() == customer_id
        assert page.locator(".line-product").evaluate_all("xs=>xs.map(x=>x.value)") == products
        assert page.locator(".line-quantity").evaluate_all("xs=>xs.map(x=>x.value)") == ["0", "3"]
        first.fill(str(lines[0][1]))
    assert save(page) == 302
    expect(page).to_have_url(re.compile("/Orders/Details/" + oid))
    return oid


def browser_case(demo, sync):
    # Preparar los tres registros dedicados incluso al repetir este caso tras un fallo.
    demo.execute_sql("""USE ErpSimulatorDockerDemo;
        BEGIN TRANSACTION;
        UPDATE ErpCustomers SET Active=1 WHERE Code='TC0500';
        UPDATE ErpItems SET Active=1 WHERE Code='TP0499';
        UPDATE ErpItems SET UnitPrice=12.3456 WHERE Code='TP0500';
        UPDATE ErpCatalogStates SET CurrentVersion=CurrentVersion+1, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=1;
        COMMIT;""")
    sync()
    errors = []
    with chromium(demo) as browser:
        page = login(browser, demo, "vendedor", errors)
        oid = draft(page, "TC0500", [("TP0500", 2), ("TP0499", 3)], check_validation=True)
        page.get_by_role("link", name="Editar borrador").click()
        expect(page.locator("#CustomerId option:checked")).to_contain_text("TC0500")
        assert page.locator(".line-product option:checked").all_text_contents() == [
            "TP0500 - Producto captura 0500", "TP0499 - Producto captura 0499"]
        assert page.locator(".line-quantity").evaluate_all("xs=>xs.map(x=>x.value)") == ["2", "3"]
        assert page.locator("select option[value]:not([value=''])").count() == 3

        operator = login(browser, demo, "operador", errors)
        operator.goto("/Orders")
        assert operator.get_by_role("link", name="Nuevo pedido", exact=True).count() == 0
        assert operator.goto("/Orders/Details/" + oid).status == 200
        assert operator.get_by_role("link", name="Editar borrador").count() == 0
        session = demo.Session()
        session.login("operador@portal.test")
        token = demo.Form(session.request("/Orders")[2]).inputs["__RequestVerificationToken"]
        for path in ["/Orders/Create", "/Orders/Edit/" + oid, "/Orders/Submit/" + oid]:
            assert session.request(path)[0] == 403
            assert session.request(path, {"__RequestVerificationToken": token})[0] == 403
        for action in ["SearchCustomers", "SearchProducts"]:
            assert session.request("/Orders/" + action + "?term=T")[0] == 403

        version = page.locator("#RowVersion").input_value()
        admin = login(browser, demo, "admin", errors)
        admin.goto("/Orders/Edit/" + oid)
        admin.locator(".line-quantity").first.fill("4")
        assert save(admin) == 302
        page.locator(".line-quantity").first.fill("8")
        assert save(page) == 409
        expect(page.get_by_role("alert").filter(has_text="Tus cambios no se guardaron")).to_be_visible()
        assert page.locator("#RowVersion").input_value() == version
        assert page.locator(".line-quantity").first.input_value() == "8"
        page.goto("/Orders/Edit/" + oid)
        assert page.locator(".line-quantity").first.input_value() == "4"
        original = demo.order_state(oid)[0]
        demo.execute_sql("""USE ErpSimulatorDockerDemo;
            BEGIN TRANSACTION;
            UPDATE ErpCustomers SET Active=0 WHERE Code='TC0500';
            UPDATE ErpItems SET Active=0 WHERE Code='TP0499';
            UPDATE ErpItems SET UnitPrice=14.4321 WHERE Code='TP0500';
            UPDATE ErpCatalogStates SET CurrentVersion=CurrentVersion+1, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=1;
            COMMIT;""")
        sync()
        page.locator(".line-quantity").last.fill("5")
        assert save(page) == 200
        expect(page.locator("form [role=alert]")).to_contain_text("El catálogo cambió")
        expect(page.locator("body")).to_contain_text("El cliente seleccionado ya no está disponible")
        expect(page.locator(".product-review").first).to_contain_text("Precio guardado PEN 12.3456")
        expect(page.locator(".product-review").last).to_contain_text("Producto no disponible")
        assert page.locator(".line-quantity").last.input_value() == "5"
        assert demo.order_state(oid)[0] == original
        assert not page.locator("#AcceptCurrentPrices").is_checked()
        choose(page, None, "TC0498")
        choose(page, 1, "TP0498")
        assert save(page) == 302
        saved = demo.sql(f"SELECT ItemCode, UnitPrice, Quantity FROM SalesOrderLines WHERE OrderId='{oid}' ORDER BY LineNumber")
        assert [(r["ItemCode"], r["UnitPrice"], r["Quantity"]) for r in saved] == [
            ("TP0500", Decimal("14.4321"), 4), ("TP0498", Decimal("12.3456"), 5)]
        assert demo.order_state(oid)[0]["Subtotal"] == Decimal("119.46")
        assert not errors, errors
        return dict(orderId=oid, chromium=browser.version, lines=saved, conflict=409, operatorAccess=403)


def lost_response(demo):
    errors = []
    with chromium(demo) as browser:
        page = login(browser, demo, "vendedor", errors)
        totals_query = ("SELECT (SELECT COUNT(*) FROM SalesOrders) AS orders, "
                        "(SELECT COUNT(*) FROM OutboxMessages) AS messages, "
                        "(SELECT COUNT(*) FROM ErpSimulatorDockerDemo.dbo.ErpOrderReceipts) AS receipts")
        totals_before = demo.sql(totals_query)[0]
        oid = draft(page, "TC0400", [("TP0400", 7)])
        number = demo.order_state(oid)[0]["PublicNumber"]
        operator = demo.Session()
        operator.login("operador@portal.test")
        inbox = f"/Integration/Attention?orderNumber={number}&category=Uncertain"
        assert operator.request(inbox)[0] == 200
        config_path = demo.PRIVATE / "erp.json"
        original = config_path.read_bytes()
        config = json.loads(original)
        config["Simulator"]["Faults"] = {"LoseOrderResponseOnce": True, "TargetOrderId": oid}
        stock_query = ("SELECT OnHand, Reserved FROM ErpSimulatorDockerDemo.dbo.ErpStocks "
                       "WHERE ItemCode='TP0400' AND WarehouseCode='01'")
        receipt_query = (f"SELECT Id, IdempotencyKey, Status, ExternalOrderId FROM "
                         f"ErpSimulatorDockerDemo.dbo.ErpOrderReceipts WHERE ExternalReference='{oid}'")
        before = demo.sql(stock_query)[0]
        assert not demo.sql(receipt_query)
        try:
            config_path.write_text(json.dumps(config), encoding="utf-8")
            restart_erp(demo)
            page.goto("/Orders/Submit/" + oid)
            page.locator("#ConfirmSubmission").check()
            payload = page.locator("#ConfirmSubmission").evaluate(
                "el => new URLSearchParams(new FormData(el.form)).toString()")
            page.get_by_role("button", name="Confirmar envío").click()
            expect(page).to_have_url(re.compile("/Orders/Details/" + oid))
            replay = """async ([url, data]) => {
                const response = await fetch(url, {method: 'POST', body: new URLSearchParams(data)});
                return [response.status, response.url];
            }"""
            def repeat_submit():
                status, url = page.evaluate(replay, ["/Orders/Submit/" + oid, payload])
                assert status == 200 and "/Orders/Details/" in url

            repeat_submit()
            deadline = time.monotonic() + 100
            while time.monotonic() < deadline:
                status, _, html = operator.request(inbox)
                assert status == 200
                if f"Pedido #{number}</a>" in html and "referencia original" in html:
                    break
                time.sleep(0.25)
            else:
                raise AssertionError("El operador no observó el resultado incierto.")
            schedule = demo.sql(f"""SELECT m.NextAttemptAtUtc, a.StartedAtUtc FROM OutboxMessages m
                JOIN IntegrationAttempts a ON a.MessageId=m.Id
                WHERE m.OrderId='{oid}' AND a.Operation='Submit'""")[0]
            for utc in schedule.values():
                peru = datetime.fromisoformat(utc) - timedelta(hours=5)
                assert peru.strftime("%d/%m/%Y %H:%M:%S") in html
            while time.monotonic() < deadline:
                receipts = demo.sql(receipt_query)
                if receipts:
                    break
                time.sleep(0.5)
            assert len(receipts) == 1 and receipts[0]["Status"] == "Accepted"
            receipt = receipts[0]
            assert receipt["IdempotencyKey"].lower() == oid
            assert receipt["ExternalOrderId"]
            while time.monotonic() < deadline:
                orders = demo.order_state(oid)
                if len(orders) == 1 and orders[0]["Status"] == "Confirmed":
                    break
                time.sleep(1)
            assert len(orders) == 1 and orders[0]["Status"] == "Confirmed"
            assert orders[0]["ExternalOrderId"] == receipt["ExternalOrderId"]
            status, _, html = operator.request(inbox)
            assert status == 200 and f"Pedido #{number}</a>" not in html
            repeat_submit()
            assert demo.sql(receipt_query) == receipts
            messages = demo.sql(f"SELECT Id FROM OutboxMessages WHERE OrderId='{oid}'")
            assert len(messages) == 1
            attempts = demo.sql(f"SELECT Operation, ResultCode, HttpStatus FROM IntegrationAttempts "
                                f"WHERE MessageId='{messages[0]['Id']}' ORDER BY AttemptNumber")
            assert [a["Operation"] for a in attempts] == ["Submit", "Lookup"], attempts
            assert attempts[0]["ResultCode"] in ("ERP_CONNECTION_FAILED", "ERP_TIMEOUT")
            assert attempts[1]["HttpStatus"] == 200
            logs = demo.process(demo.COMPOSE + ["logs", "--no-color", "erp"])
            assert len(re.findall("SIMULATED_RESPONSE_LOSS: el pedido " + oid, logs, re.I)) == 1
            after = demo.sql(stock_query)[0]
            assert after["OnHand"] == before["OnHand"] and after["Reserved"] - before["Reserved"] == 7
            page.reload()
            expect(page.locator("body")).to_contain_text(receipt["ExternalOrderId"])
            assert not errors, errors
            totals_after = demo.sql(totals_query)[0]
            assert all(totals_after[key] - value == 1 for key, value in totals_before.items())
            return dict(order=orders[0], receipt=receipt, outbox=messages[0], attempts=attempts,
                        totalsBefore=totals_before, totalsAfter=totals_after,
                        faultMarkerCount=1, observedUncertain=True, observedSchedule=schedule,
                        stockBefore=before, stockAfter=after)
        finally:
            config_path.write_bytes(original)
            restart_erp(demo)


def restart_erp(demo):
    demo.process(demo.COMPOSE + ["restart", "erp"])
    demo.process(demo.COMPOSE + ["up", "-d", "--wait", "--no-deps", "erp"])
