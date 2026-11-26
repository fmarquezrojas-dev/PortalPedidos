"""Aceptación de cartera dentro de la copia Compose privada de Test-Capture."""
import json
import re
import time
import uuid
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from html import unescape
from threading import Barrier

from playwright.sync_api import expect
from capture_browser import chromium, choose, login, save

TOKEN = "__RequestVerificationToken"


def session(demo, name):
    result = demo.Session()
    result.login(name + "@portal.test")
    return result


def form(demo, client, path):
    status, _, body = client.request(path)
    assert status == 200, (path, status)
    return demo.Form(body).inputs


def assignment(demo, admin, customer, alias):
    page = "/CustomerPortfolio/Details/" + customer
    inputs = form(demo, admin, page)
    fields = {k: inputs.get(k, "") for k in ("ExpectedAssignmentId", "ExpectedRowVersion", TOKEN)}
    fields.update(CustomerId=customer, SellerEmail=alias + "@portal.test", Reason="Rotación comercial de prueba")
    assert admin.request("/CustomerPortfolio/Assign", fields)[0] == 200
    result = demo.sql(f"""SELECT a.Id,u.Email FROM CustomerAssignments a JOIN SalesTeamMembers m ON m.Id=a.SalesTeamMemberId
        JOIN AspNetUsers u ON u.Id=m.UserId WHERE a.CustomerId='{customer}' AND a.EndedAtUtc IS NULL""")
    assert len(result) == 1 and result[0]["Email"] == fields["SellerEmail"]
    return result[0]["Id"]


def grant(demo, admin, customer, principal, alias, start, end, barrier=None):
    data = dict(CustomerId=customer, AssignmentId=principal, Email=alias+"@portal.test",
                From=start.isoformat(), To=end.isoformat(), Reason="Vacaciones del titular")
    data[TOKEN] = form(demo, admin, "/CustomerPortfolio/Details/" + customer)[TOKEN]
    if barrier:
        barrier.wait(timeout=30)
    status, _, body = admin.request("/CustomerPortfolio/GrantCoverage", data)
    if status == 409 and barrier:
        assert "superpone" in unescape(body)
    return status


def order_from_card(page, demo, customer, code, alias, second_line=False):
    page.goto("/MyPortfolio")
    page.locator("#search").fill(code)
    page.get_by_role("button", name="Buscar").click()
    expect(page.locator("tbody tr")).to_have_count(1)
    expect(page.locator("tbody tr")).to_contain_text("Titular" if alias == "vendedor" else "Suplente")
    page.locator("tbody a").click()
    page.get_by_role("link", name="Nuevo pedido", exact=True).last.click()
    expect(page.locator("#CustomerId")).to_have_value(customer.lower())
    oid = str(uuid.UUID(page.locator("#DraftId").input_value()))
    product = choose(page, 0, "TP0150")
    page.locator(".line-quantity").fill("2")
    if second_line:
        page.locator("#add-order-line").click()
        choose(page, 1, "TP0151")
        page.locator(".line-quantity").nth(1).fill("3")
    assert save(page) == 302
    expect(page).to_have_url(re.compile("/Orders/Details/" + oid))
    stored = demo.sql(f"""SELECT o.CustomerId,u.Email FROM SalesOrders o JOIN AspNetUsers u ON u.Id=o.OwnerUserId
        WHERE o.Id='{oid}'""")[0]
    assert stored["CustomerId"].lower() == customer.lower() and stored["Email"] == alias + "@portal.test"
    return oid, product


def configure(demo, config):
    (demo.PRIVATE / "portal.json").write_text(json.dumps(config), encoding="utf-8")
    demo.process(demo.COMPOSE + ["up", "-d", "--wait", "--no-deps", "--force-recreate", "portal"])


def submit_fields(demo, actor, oid):
    inputs = form(demo, actor, "/Orders/Submit/" + oid)
    return dict({key: inputs[key] for key in ("Id", "RowVersion", TOKEN)}, ConfirmSubmission="true")


def stored_lines(demo, oid):
    return demo.sql(f"""SELECT Id, LineNumber, ProductId, ItemCode, Quantity, UnitPrice, LineAmount
        FROM SalesOrderLines WHERE OrderId='{oid}' ORDER BY LineNumber""")


def assert_denied_submit(demo, seller, oid, fields, original_lines):
    status, _, body = seller.request("/Orders/Submit/" + oid, fields)
    assert status == 409 and "suplencia vigente" in unescape(body), (oid, status)
    assert demo.order_state(oid)[0]["Status"] == "Draft"
    assert len(original_lines) == 2 and stored_lines(demo, oid) == original_lines
    assert "/Orders/Edit/" + oid in body and "Editar borrador" in body
    assert not demo.sql(f"SELECT Id FROM OutboxMessages WHERE OrderId='{oid}'")
    return dict(order=oid, http=status, linesPreserved=len(original_lines), outbox=0)


def portfolio_case(demo):
    migration = demo.sql("SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId LIKE '%AddCustomerCoverages'")
    assert len(migration) == 1
    old = demo.sql("SELECT COUNT(*) AS n FROM SalesOrders WHERE CustomerAssignmentId IS NULL AND Status='Confirmed'")
    assert old[0]["n"] >= 3
    original = (demo.PRIVATE / "portal.json").read_bytes()
    config = json.loads(original)
    config["CustomerPortfolio"] = {"Restricted": True}
    config["Outbox"]["Enabled"] = False
    try:
        configure(demo, config)
        return exercise(demo, config, migration[0]["MigrationId"])
    finally:
        (demo.PRIVATE / "portal.json").write_bytes(original)
        demo.process(demo.COMPOSE + ["up", "-d", "--wait", "--no-deps", "--force-recreate", "portal"])


def exercise(demo, config, migration):
    root = demo.PRIVATE.parent
    people = {name: session(demo, name) for name in
              ("admin", "admin2", "vendedor", "suplente", "ajeno", "supervisor1", "supervisor2")}
    admin, sub = people["admin"], people["suplente"]
    ids = {r["ExternalCode"]: r["Id"] for r in demo.sql(
        "SELECT ExternalCode, Id FROM Customers WHERE ExternalCode BETWEEN 'TC0150' AND 'TC0153'")}
    customer, replacement, queue_customer = (ids["TC0150"], ids["TC0151"], ids["TC0152"])
    principal = assignment(demo, admin, customer, "vendedor")
    assignment(demo, admin, replacement, "suplente")
    queue_principal = assignment(demo, admin, queue_customer, "vendedor")
    today = datetime.now(timezone(timedelta(hours=-5))).date()
    friday = today + timedelta(days=(4 - today.weekday()) % 7)
    assert grant(demo, admin, customer, principal, "vendedor", today, friday) == 409
    assert grant(demo, admin, customer, principal, "ajeno", today, friday) == 409
    gate = Barrier(2)
    with ThreadPoolExecutor(max_workers=2) as pool:
        tasks = [pool.submit(grant, demo, people[name], customer, principal, "suplente", start, friday, gate)
                 for name, start in (("admin", today), ("admin2", today - timedelta(days=1)))]
        conflicts = sorted(task.result() for task in tasks)
    assert conflicts == [200, 409]
    coverage = demo.sql(f"SELECT Id FROM CustomerCoverages WHERE AssignmentId='{principal}'")
    assert len(coverage) == 1
    # El permiso depende de las cuentas y membresías actuales, incluso con cookies ya emitidas.
    for deactivate, restore in [
        ("UPDATE AspNetUsers SET IsActive=0 WHERE Email='vendedor@portal.test'",
         "UPDATE AspNetUsers SET IsActive=1 WHERE Email='vendedor@portal.test'"),
        ("UPDATE SalesTeamMembers SET LeftAtUtc=SYSUTCDATETIME() WHERE UserId=(SELECT Id FROM AspNetUsers WHERE Email='suplente@portal.test')",
         "UPDATE SalesTeamMembers SET LeftAtUtc=NULL WHERE UserId=(SELECT Id FROM AspNetUsers WHERE Email='suplente@portal.test')"),
        ("UPDATE SalesTeams SET IsActive=0 WHERE Code='supervisor1'", "UPDATE SalesTeams SET IsActive=1 WHERE Code='supervisor1'"),
    ]:
        demo.execute_sql(deactivate)
        try:
            assert sub.request("/MyPortfolio/Details/" + customer)[0] == 404
            assert not json.loads(sub.request("/Orders/SearchCustomers?term=TC0150")[2])["items"]
        finally:
            demo.execute_sql(restore)
    clock = demo.process(demo.COMPOSE + ["run", "--rm", "--no-deps", "-v", str(root / "clock-check") + ":/test:ro",
        "--entrypoint", "/bin/bash", "portal", "/entrypoint.sh", "/test/PortfolioClockCheck.dll"]).strip()
    assert clock.count("permitido") == 2 and clock.count("denegado") == 2

    for alias, path, expected in [
        ("supervisor2","/TeamPortfolio/Details/"+customer,404), ("ajeno","/MyPortfolio/Details/"+customer,404),
        ("supervisor1","/TeamPortfolio/Details/"+customer,200), ("vendedor","/TeamPortfolio",403),
        ("supervisor1","/Orders/Create?customerId="+customer,403), ("suplente","/CustomerPortfolio/Details/"+customer,403),
    ]:
        assert people[alias].request(path)[0] == expected, (alias, path)
    status, _, html = people["supervisor2"].request("/TeamPortfolio?filter=Expiring")
    assert status == 200 and "Clientes encontrados · 0" in html and "TC0150" not in html
    assert not json.loads(people["ajeno"].request("/Orders/SearchCustomers?term=TC0150")[2])["items"]
    assert "TC0150" not in people["ajeno"].request("/Orders/Create?customerId="+customer)[2]
    assert len(json.loads(admin.request("/Orders/SearchCustomers?term=TC0153")[2])["items"]) == 1
    errors = []
    with chromium(demo) as browser:
        supervisor = login(browser, demo, "supervisor1", errors)
        supervisor.goto("/TeamPortfolio?filter=Expiring")
        expect(supervisor.locator("tbody tr")).to_have_count(1)
        expect(supervisor.locator("tbody tr")).to_contain_text("Próxima a vencer")
        seller = login(browser, demo, "vendedor", errors)
        seller.goto("/MyPortfolio?search=TC02&filter=Holder")
        expect(seller.locator("tbody tr")).to_have_count(20)
        seller.get_by_role("link", name="Siguiente").click()
        expect(seller.locator("tbody tr")).to_have_count(3)
        holder_order, _ = order_from_card(seller, demo, customer, "TC0150", "vendedor")
        assert sub.request("/Orders/Details/"+holder_order)[0] == 404
        deputy = login(browser, demo, "suplente", errors)
        blocked, product = order_from_card(deputy, demo, customer, "TC0150", "suplente", second_line=True)
        stale_submit = submit_fields(demo, sub, blocked)
        original_lines = stored_lines(demo, blocked)
        # Retener otro formulario sin guardar durante la revocación.
        deputy.goto("/Orders/Create?customerId="+customer)
        choose(deputy, 0, "TP0150")
        depot_id = deputy.locator("#DraftId").input_value()
        manager = login(browser, demo, "admin", errors)
        manager.goto("/CustomerPortfolio/Details/" + customer)
        manager.get_by_label("Motivo de revocación").fill("Regresó el titular")
        manager.get_by_role("button", name="Revocar suplencia").click()
        expect(manager.locator("article")).to_contain_text("Regresó el titular")
        assert save(deputy) == 200
        expect(deputy.locator(".line-product")).to_have_value(product)
        assert not demo.order_state(depot_id)
        revocation = assert_denied_submit(demo, sub, blocked, stale_submit, original_lines)
        deputy.goto("/Orders/Edit/"+blocked)
        expect(deputy.locator(".line-product").first).to_have_value(product)
        expect(deputy.locator(".line-quantity").first).to_have_value("2")
        choose(deputy, None, "TC0151")
        assert save(deputy) == 302
        assert demo.sql(f"SELECT CustomerId FROM SalesOrders WHERE Id='{blocked}'")[0]["CustomerId"] == replacement
        edit = form(demo, sub, "/Orders/Edit/"+blocked)
        fields = {k:edit[k] for k in ("DraftId", "CatalogVersion", "RequestedDate", "RowVersion", TOKEN)}
        fields.update({"CustomerId":customer, "Lines[0].ProductId":product, "Lines[0].Quantity":"5",
                       "AcceptCurrentPrices":"true"})
        status, _, body = sub.request("/Orders/Edit/"+blocked, fields)
        assert status == 200 and "suplencia vigente" in unescape(body)
        assert demo.sql(f"SELECT CustomerId FROM SalesOrders WHERE Id='{blocked}'")[0]["CustomerId"] == replacement
        # Una revisión para copiar un rechazo tampoco conserva el permiso anterior.
        demo.execute_sql(f"UPDATE SalesOrders SET Status='Rejected' WHERE Id='{blocked}'")
        copy = form(demo, sub, "/OrderCopies/Index/"+blocked)
        assignment(demo, admin, replacement, "vendedor")
        fields = {k:copy[k] for k in ("SourceOrderId", "DraftId", "CatalogVersion", "RequestedDate", TOKEN)}
        fields["ConfirmCopy"] = "true"
        status, _, body = sub.request("/OrderCopies/Create/"+blocked, fields)
        assert status == 409 and "suplencia vigente" in unescape(body)
        assert not demo.order_state(fields["DraftId"])
        # Otro pedido de este suplente quedará en cola antes de finalizar su cobertura.
        manager.goto("/CustomerPortfolio/Details/" + queue_customer)
        manager.get_by_label("Correo del suplente").fill("suplente@portal.test")
        manager.get_by_label("Desde · Perú").fill(today.isoformat())
        manager.get_by_label("Hasta inclusive · Perú").fill(friday.isoformat())
        manager.get_by_label("Motivo de la suplencia").fill("Vacaciones del titular")
        manager.get_by_role("button", name="Registrar suplencia").click()
        expect(manager.locator("article")).to_contain_text("Vigente")
        expiring, _ = order_from_card(deputy, demo, queue_customer, "TC0152", "suplente", second_line=True)
        pending_submit = submit_fields(demo, sub, expiring)
        expiring_lines = stored_lines(demo, expiring)
        queued, _ = order_from_card(deputy, demo, queue_customer, "TC0152", "suplente")
        assert sub.request("/Orders/Submit/"+queued, submit_fields(demo, sub, queued))[0] == 200
        assert demo.order_state(queued)[0]["Status"] == "Queued"
        assert not errors

    # Acercar el fin del intervalo solo en la copia sintética para observar al worker real tras el vencimiento.
    demo.execute_sql(f"""UPDATE CustomerCoverages SET EndsAtUtc=DATEADD(second,1,SYSUTCDATETIME())
        WHERE AssignmentId='{queue_principal}'""")
    time.sleep(1.1)
    assert sub.request("/MyPortfolio/Details/"+queue_customer)[0] == 404
    expiry = assert_denied_submit(demo, sub, expiring, pending_submit, expiring_lines)
    before = demo.sql("SELECT Reserved FROM ErpSimulatorDockerDemo.dbo.ErpStocks WHERE ItemCode='TP0150' AND WarehouseCode='01'")[0]
    config["Outbox"]["Enabled"] = True
    configure(demo, config)
    for _ in range(45):
        state = demo.order_state(queued)[0]
        if state["Status"] == "Confirmed":
            break
        time.sleep(1)
    assert state["Status"] == "Confirmed"
    receipts = demo.sql(f"SELECT ExternalOrderId FROM ErpSimulatorDockerDemo.dbo.ErpOrderReceipts WHERE ExternalReference='{queued}'")
    assert receipts == [{"ExternalOrderId":state["ExternalOrderId"]}]
    after = demo.sql("SELECT Reserved FROM ErpSimulatorDockerDemo.dbo.ErpStocks WHERE ItemCode='TP0150' AND WarehouseCode='01'")[0]
    assert after["Reserved"] - before["Reserved"] == 2
    assignment(demo, admin, queue_customer, "ajeno")
    trace = demo.sql(f"""SELECT h.Email AS holder,u.Email AS creator,t.Code AS team,a.Id AS assignment
        FROM SalesOrders o JOIN AspNetUsers u ON u.Id=o.OwnerUserId JOIN SalesTeams t ON t.Id=o.SalesTeamId
        JOIN CustomerAssignments a ON a.Id=o.CustomerAssignmentId JOIN SalesTeamMembers m ON m.Id=a.SalesTeamMemberId
        JOIN AspNetUsers h ON h.Id=m.UserId WHERE o.Id='{queued}' AND a.EndedAtUtc IS NOT NULL""")[0]
    assert trace["holder"] == "vendedor@portal.test" and trace["creator"] == "suplente@portal.test"
    assert trace["assignment"] == queue_principal and trace["team"] == "supervisor1"
    audits = demo.sql(f"""SELECT Action FROM AuditEvents WHERE EntityId IN ('{customer}','{queue_customer}')
        AND Action LIKE 'CustomerPortfolio.%' ORDER BY OccurredAtUtc""")
    assert {"CustomerPortfolio.CoverageGranted","CustomerPortfolio.CoverageRevoked","CustomerPortfolio.Reassigned"} <= {r["Action"] for r in audits}
    return dict(migration=migration, overlap=conflicts, controlledClock=clock.splitlines(),
                holderDraft=holder_order, correctedDraft=blocked, revocation=revocation, expiry=expiry,
                queuedOrder=queued, receipt=receipts[0], reservation=2, trace=trace, audit=audits, pages=[20,3])
