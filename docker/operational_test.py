"""Comprobaciones de la bandeja sobre el proyecto privado validado por capture-test.connect."""
import json
import re
import time
import uuid
from contextlib import contextmanager
from datetime import datetime, timedelta

from playwright.sync_api import expect

from capture_browser import chromium, login


INBOX = "/Integration/Attention"


def concurrent_recovery(demo, operator, seller):
    lost = json.loads((demo.PRIVATE / "capture-lost-response.json").read_text(encoding="utf-8"))
    assert lost["observedUncertain"]
    message = lost["outbox"]["Id"]
    order = lost["order"]["Id"]
    page = "/IntegrationRecovery/Index/" + message
    confirm = "/IntegrationRecovery/Confirm/" + message
    for url in (INBOX, INBOX + "?category=Review", "/Integration/Details/" + message, page):
        assert seller.request(url)[0] == 403, url
    assert seller.request(confirm, {})[0] == 403

    # El recibo del pedido ya existe en el ERP. Dos operadores abren la misma versión.
    demo.execute_sql(f"""BEGIN TRAN;
        UPDATE SalesOrders SET Status='Queued', ExternalOrderId=NULL WHERE Id='{order}';
        UPDATE OutboxMessages SET Status='DeadLetter', LastErrorCode='ERP_LOOKUP_EXHAUSTED'
        WHERE Id='{message}'; COMMIT;""")
    status, _, html = operator.request(page)
    assert status == 200
    second_operator = demo.Session()
    second_operator.login("operador@portal.test")
    stale = demo.Form(second_operator.request(page)[2]).inputs
    form = demo.Form(html).inputs
    payload = {key: form[key] for key in ("MessageId", "RowVersion", "__RequestVerificationToken")}
    payload.update(Reason="Referencia original verificada", ConfirmRecovery="true")
    assert operator.request(confirm, dict(payload, __RequestVerificationToken=""))[0] == 400
    assert operator.request(confirm, payload)[0] == 200
    for _ in range(60):
        state = demo.order_state(order)[0]
        if state["Status"] == "Confirmed":
            break
        time.sleep(1)
    assert state["ExternalOrderId"] == lost["receipt"]["ExternalOrderId"]
    assert stale["RowVersion"] == payload["RowVersion"]
    stale_payload = dict(payload, __RequestVerificationToken=stale["__RequestVerificationToken"])
    assert second_operator.request(confirm, stale_payload)[0] == 409
    attempts = demo.sql(f"SELECT Operation FROM IntegrationAttempts WHERE MessageId='{message}' ORDER BY AttemptNumber")
    operations = [a["Operation"] for a in attempts]
    assert operations == ["Submit", "Lookup", "Recovery", "Lookup"]
    return operations


@contextmanager
def stable_fixture(demo):
    fixture = demo.PRIVATE.parent / "docker" / "operational-fixture.sql"
    version = int(demo.sql("SELECT CurrentVersion FROM CatalogSyncStates WHERE Id=1")[0]["CurrentVersion"])
    ids = json.loads(demo.execute_sql(fixture.read_text(encoding="utf-8")))
    keys = ",".join(f"'{uuid.UUID(row['Id'])}'" for row in ids)
    try:
        yield
    finally:
        demo.execute_sql(f"""BEGIN TRAN;
            DELETE FROM OutboxMessages WHERE OrderId IN ({keys});
            DELETE FROM SalesOrders WHERE Id IN ({keys});
            UPDATE CatalogSyncStates SET CurrentVersion={version} WHERE Id=1; COMMIT;""")
        demo.process(demo.COMPOSE + ["run", "--rm", "--no-deps", "portal", "--sync-catalog"])


def exercise_inbox(demo, operator):
    sample = json.loads(demo.execute_sql("""SELECT (SELECT o.PublicNumber, o.CustomerName, m.Id, m.NextAttemptAtUtc
        FROM SalesOrders o JOIN OutboxMessages m ON m.OrderId=o.Id WHERE CustomerName LIKE 'Caso operativo %'
        FOR JSON PATH)"""))
    cases = {int(row["CustomerName"].split()[-1]): row for row in sample}
    assert len(cases) == 29
    errors = []
    effects = """SELECT (SELECT COUNT(*) FROM OutboxMessages) AS messages,
        (SELECT MAX(CONVERT(bigint,RowVersion)) FROM OutboxMessages) AS version,
        (SELECT COUNT(*) FROM IntegrationAttempts) AS attempts,
        (SELECT COUNT(*) FROM ErpSimulatorDockerDemo.dbo.ErpOrderReceipts) AS receipts,
        (SELECT SUM(Reserved) FROM ErpSimulatorDockerDemo.dbo.ErpStocks) AS reserved"""
    before = demo.sql(effects)
    with chromium(demo) as browser:
        page = login(browser, demo, "operador", errors)
        page.get_by_role("link", name="Integraciones", exact=True).click()
        def visit(n):
            page.goto(INBOX + "?orderNumber=" + str(cases[n]["PublicNumber"]))
            return page.locator("tbody tr")

        review = page.get_by_role("navigation", name="Filtrar por prioridad").get_by_role("link", name="Requiere revisión 22")
        review.click()
        expect(page.locator("#list-heading")).to_have_text(re.compile("Requiere revisión\\s+22"))
        first = page.locator("tbody a").all_text_contents()
        assert len(first) == 20
        expect(page.locator("tbody tr").first).to_contain_text("Procesamiento detenido")
        page.get_by_role("link", name="Siguiente").click()
        second = page.locator("tbody a").all_text_contents()
        assert len(second) == 2 and len(set(first + second)) == 22
        page.locator("tbody a").first.click()
        page.get_by_role("link", name="Volver", exact=True).click()
        assert "category=review" in page.url.lower() and "pagenumber=2" in page.url.lower()
        assert page.locator("tbody a").all_text_contents() == second
        visit(1).get_by_role("link").click()
        expect(page.get_by_role("link", name="Revisar recuperación")).to_have_count(0)
        visit(2).get_by_role("link").click()
        expect(page.get_by_role("link", name="Revisar recuperación")).to_be_visible()
        expect(visit(23)).to_contain_text("Resultado ERP por confirmar")
        expect(visit(24)).to_contain_text("Procesamiento con plazo vencido")
        expect(visit(25)).to_contain_text("El pedido está en proceso. Espera hasta el fin del plazo indicado.")
        future = visit(26)
        scheduled = datetime.fromisoformat(cases[26]["NextAttemptAtUtc"]) - timedelta(hours=5)
        expect(future).to_contain_text(scheduled.strftime("%d/%m/%Y %H:%M:%S"))
        expect(future).to_contain_text("Reintento programado para más adelante")
        expect(visit(27)).to_contain_text("Ya llegó la hora del reintento")
        expect(visit(29)).to_have_count(0)
        page.goto("/Integration?status=Rejected&orderNumber=" + str(cases[29]["PublicNumber"]))
        expect(page.locator("tbody tr")).to_contain_text("Rechazado por el ERP")
        assert demo.sql(effects) == before

        message = cases[28]["Id"]
        demo.execute_sql(f"UPDATE OutboxMessages SET CreatedAtUtc=DATEADD(second,-295,SYSUTCDATETIME()) WHERE Id='{message}'")
        expect(visit(28)).to_contain_text("Seguimiento normal")
        version = demo.sql(f"SELECT RowVersion FROM OutboxMessages WHERE Id='{message}'")
        time.sleep(5.1)
        page.reload()
        expect(page.locator("tbody tr")).to_contain_text("Pendiente con demora")
        assert demo.sql(f"SELECT RowVersion FROM OutboxMessages WHERE Id='{message}'") == version

        for sql, label in [
            ("CurrentVersion=0, LastSuccessAtUtc=NULL", "Primera sincronización pendiente"),
            ("CurrentVersion=9, LastSuccessAtUtc=SYSUTCDATETIME()", "Vigente"),
            ("LastSuccessAtUtc=DATEADD(minute,-6,SYSUTCDATETIME()),LastErrorCode='SYNC_TEST'", "Fuera de vigencia"),
            ("LastSuccessAtUtc=DATEADD(minute,2,SYSUTCDATETIME())", "Fuera de vigencia"),
        ]:
            demo.execute_sql("UPDATE CatalogSyncStates SET " + sql + " WHERE Id=1")
            page.reload()
            expect(page.locator("#catalog-heading")).to_contain_text(label)
        expect(page.locator("body")).to_contain_text("SYNC_TEST")
        assert not errors, errors

    # Error real en la consulta de filas, después de poder consultar los contadores.
    demo.execute_sql("EXEC sp_rename 'IntegrationAttempts','OperationalTestAttempts'")
    try:
        status, _, html = operator.request(INBOX)
        assert status == 503 and "No se pudo consultar la bandeja" in html
        assert "No hay operaciones pendientes" not in html
    finally:
        demo.execute_sql("EXEC sp_rename 'OperationalTestAttempts','IntegrationAttempts'")
    return dict(review=len(first + second), pages=[len(first), len(second)], returnedToPage=2,
                thresholdSeconds=300, thresholdOrder=cases[28]["PublicNumber"],
                scheduledPeru=scheduled.isoformat(), expiredLeaseOrder=cases[24]["PublicNumber"],
                activeLeaseOrder=cases[25]["PublicNumber"], readOnlyEffects=before[0],
                catalogStates=["firstSyncPending", "fresh", "stale", "futureDate"], failedReadStatus=status)


def operational_case(demo):
    operator, seller = demo.Session(), demo.Session()
    operator.login("operador@portal.test")
    seller.login("vendedor@portal.test")
    operations = concurrent_recovery(demo, operator, seller)
    config_path = demo.PRIVATE / "portal.json"
    original = config_path.read_bytes()
    config = json.loads(original)
    try:
        for worker in ("Outbox", "CatalogSync"):
            config[worker]["Enabled"] = False
        config_path.write_text(json.dumps(config), encoding="utf-8")
        demo.process(demo.COMPOSE + ["up", "-d", "--wait", "--no-deps", "--force-recreate", "portal"])
        assert "No hay operaciones pendientes." in operator.request(INBOX)[2]
        with stable_fixture(demo):
            return dict(exercise_inbox(demo, operator), concurrency=operations, staleConfirmation=409,
                        sellerAccess=403, operatorAccess=200)
    finally:
        demo.process(demo.COMPOSE + ["stop", "portal"])
        config_path.write_bytes(original)
