"""Pruebas exclusivas de una copia temporal portalpedidos-capture-*."""
import argparse
import importlib.util
import http.client
import json
import re
import ssl
import statistics
import time
from contextlib import closing
from urllib.parse import urlsplit
from urllib.request import Request
from html.parser import HTMLParser
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("functional", HERE / "functional-test.py")
demo = importlib.util.module_from_spec(spec)
spec.loader.exec_module(demo)


def connect(root):
    compose = ["docker", "compose", "--env-file", str(root / ".env"), "-f", str(root / "compose.yaml")]
    config = json.loads(demo.process(compose + ["config", "--format", "json"]))
    project = config["name"]
    assert project.startswith("portalpedidos-capture-"), "Se necesita una copia Compose de captura."
    demo.COMPOSE = compose + ["-p", project]
    address = demo.process(demo.COMPOSE + ["port", "portal", "8443"]).strip()
    assert re.fullmatch(r"127\.0\.0\.1:\d+", address)
    demo.BASE = "https://localhost:" + address.split(":")[1]
    demo.PRIVATE = root / ".docker-local"


def sync():
    target = demo.sql("SELECT CurrentVersion AS v FROM ErpSimulatorDockerDemo.dbo.ErpCatalogStates WHERE Id=1")[0]["v"]
    try:
        demo.process(demo.COMPOSE + ["run", "--rm", "--no-deps", "portal", "--sync-catalog"])
    except RuntimeError:
        # El worker puede tener el bloqueo de sincronización y estar importando la misma versión.
        for _ in range(10):
            if demo.sql("SELECT CurrentVersion AS v FROM CatalogSyncStates WHERE Id=1")[0]["v"] >= target:
                return
            time.sleep(1)
        raise


class Options(HTMLParser):
    def __init__(self, html):
        super().__init__()
        self.ids = []
        self.feed(html)

    def handle_starttag(self, tag, attrs):
        if tag == "option" and (value := dict(attrs).get("value")):
            self.ids.append(value)


def sample_page(seller, path):
    # Reutilizar TLS como el navegador. La primera petición calienta la conexión y EF.
    host = urlsplit(demo.BASE)
    context = ssl.create_default_context(cafile=str(demo.PRIVATE / "ca.crt"))
    request = Request(demo.BASE + path)
    seller.jar.add_cookie_header(request)
    elapsed = []
    with closing(http.client.HTTPSConnection(host.hostname, host.port, context=context, timeout=20)) as conn:
        for _ in range(6):
            start = time.perf_counter()
            conn.request("GET", path, headers=dict(request.header_items()))
            response = conn.getresponse()
            body = response.read()
            elapsed.append((time.perf_counter() - start) * 1000)
            assert response.status == 200
    return json.loads(body), len(body), round(statistics.median(elapsed[1:]), 2)


def measure(baseline):
    seller = demo.Session()
    seller.login("vendedor@portal.test")
    volume = demo.sql("SELECT (SELECT COUNT(*) FROM Customers WHERE IsActive=1) AS customers, "
                      "(SELECT COUNT(*) FROM Products WHERE IsActive=1) AS products")[0]
    assert min(volume.values()) >= 500
    status, _, html = seller.request("/Orders/Create")
    assert status == 200
    ids = Options(html).ids
    report = dict(volume, htmlBytes=len(html.encode()), options=len(ids), distinctIds=len(set(ids)))
    if baseline:
        assert len(set(ids)) == sum(volume.values())
    else:
        assert not ids
        report["search"] = {}
        for action, term, tag in [("SearchCustomers", "TC", "clientes"), ("SearchProducts", "TP", "productos")]:
            page, size, median = sample_page(seller, f"/Orders/{action}?term={term}")
            codes = [item["code"] for item in page["items"]]
            assert page["hasMore"] and codes == [f"{term}{n:04d}" for n in range(1, 21)]
            timing = demo.sql(f"""SELECT TOP (1) q.last_elapsed_time AS microseconds
                FROM sys.dm_exec_query_stats q CROSS APPLY sys.dm_exec_sql_text(q.sql_handle) t
                WHERE t.text LIKE '%-- Captura: {tag}%' AND t.text NOT LIKE '%dm_exec_query_stats%'
                ORDER BY q.last_execution_time DESC""")
            assert timing
            report["search"][action] = dict(items=len(codes), bytes=size,
                medianHttpMs=median, lastSqlUs=timing[0]["microseconds"])
            status, _, next_page = seller.request(f"/Orders/{action}?term={term}&offset=20")
            assert status == 200
            assert [i["code"] for i in json.loads(next_page)["items"]] == [f"{term}{n:04d}" for n in range(21, 41)]
            _, _, exact = seller.request(f"/Orders/{action}?term={term}0500")
            assert [i["code"] for i in json.loads(exact)["items"]] == [term + "0500", term + "0001"]
            if action == "SearchCustomers":
                customer_id = json.loads(exact)["items"][0]["id"]
                status, _, prefilled = seller.request("/Orders/Create?customerId=" + customer_id)
                assert status == 200 and Options(prefilled).ids == [customer_id]
                assert demo.Form(prefilled).selected["CustomerId"] == customer_id
            name = ("Cliente" if term == "TC" else "Producto") + "%20captura%200500"
            _, _, named = seller.request(f"/Orders/{action}?term={name}")
            assert [i["code"] for i in json.loads(named)["items"]] == [term + "0500"]
            assert seller.request(f"/Orders/{action}?term=")[0] == 400
    return report


def migration_check(after):
    # Comparar claves, relaciones y valores comerciales en orden estable.
    projections = {
        "Customers": "Id, ExternalCode, Name, IsActive",
        "CustomerAssignments": "Id, CustomerId, SalesTeamMemberId, AssignedByUserId, AssignedAtUtc, AssignmentReason, EndedAtUtc",
        "SalesTeamMembers": "Id, UserId, SalesTeamId, JoinedAtUtc, LeftAtUtc",
        "SalesOrders": "Id, PublicNumber, CustomerId, OwnerUserId, SalesTeamId, Status, Subtotal, ExternalOrderId",
        "SalesOrderLines": "Id, OrderId, LineNumber, ProductId, ItemCode, Quantity, UnitPrice, LineAmount",
    }
    tables = {}
    for table, columns in projections.items():
        row = demo.sql(f"""SELECT COUNT(*) AS rows,
            CONVERT(varchar(64), HASHBYTES('SHA2_256', (SELECT {columns} FROM {table}
                ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES)), 2) AS digest FROM {table}""")[0]
        assert row["rows"] > 0, table
        tables[table] = row

    migrations = [row["MigrationId"] for row in demo.sql("SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId")]
    coverage = "20261125234634_AddCustomerCoverages"
    if not after:
        assert coverage not in migrations
        return dict(tables=tables, migrations=migrations)
    previous = json.loads((demo.PRIVATE / "capture-upgrade-before.json").read_text(encoding="utf-8"))
    assert tables == previous["tables"] and migrations == [*previous["migrations"], coverage]
    assert not demo.sql("SELECT Id FROM SalesOrders WHERE CustomerAssignmentId IS NOT NULL")
    assert not demo.sql("SELECT Id FROM CustomerCoverages")
    assert not demo.sql("""SELECT a.Id FROM CustomerAssignments a JOIN Customers c ON a.CustomerId=c.Id
                           WHERE c.ExternalCode='C001'""")
    seller = demo.Session()
    seller.login("vendedor@portal.test")
    status, _, result = seller.request("/Orders/SearchCustomers?term=C001")
    assert status == 200 and json.loads(result)["items"][0]["code"] == "C001"
    return dict(tables=tables, migrations=migrations, unassignedCustomerSelectable=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("case", choices=["seed", "before", "after", "browser", "lost-response", "verify",
                                        "sync", "operational", "portfolio", "seed-portfolio", "upgrade-before", "upgrade-after"])
    parser.add_argument("--demo", type=Path, default=HERE.parent)
    args = parser.parse_args()
    connect(args.demo.resolve())
    if args.case == "verify":
        print(demo.execute_sql((HERE / "verify-data.sql").read_text()))
        return
    if args.case == "sync":
        sync()
        return
    if args.case == "seed":
        demo.execute_sql((HERE / "capture-fixture.sql").read_text())
        sync()
        return
    if args.case == "seed-portfolio":
        demo.execute_sql((HERE / "portfolio-fixture.sql").read_text(encoding="utf-8"))
        return
    if args.case in ("upgrade-before", "upgrade-after"):
        report = migration_check(args.case == "upgrade-after")
    elif args.case in ("before", "after"):
        report = measure(args.case == "before")
    elif args.case == "operational":
        from operational_test import operational_case
        report = operational_case(demo)
    elif args.case == "portfolio":
        from portfolio_test import portfolio_case
        report = portfolio_case(demo)
    else:
        from capture_browser import browser_case, lost_response
        report = browser_case(demo, sync) if args.case == "browser" else lost_response(demo)
    output = json.dumps(report, indent=2, ensure_ascii=False, default=str)
    (demo.PRIVATE / f"capture-{args.case}.json").write_text(output, encoding="utf-8")
    print(output)


if __name__ == "__main__":
    main()
