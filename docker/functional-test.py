"""Additional demo-only HTTP/backend checks; does not run migrations or delete data.

Python 3 standard library. Reuses the form parser from smoke-test.py. Stops only
the existing demo ERP briefly, and starts it in finally. Secrets stay in memory.
Results (including created IDs) go to .docker-local/functional-report-<UTC>.json.
"""
import importlib.util
import argparse
import http.cookiejar
from html import unescape
import json
from datetime import datetime, timezone
from decimal import Decimal
from pathlib import Path
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parent.parent
PRIVATE = ROOT / '.docker-local'
REPORT = PRIVATE / ('functional-report-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '.json')
BASE = 'https://localhost:8271'
COMPOSE = ['docker', 'compose', '-p', 'portalpedidos-demo', '-f', str(ROOT / 'compose.yaml')]
spec = importlib.util.spec_from_file_location('demo_smoke', ROOT / 'docker/smoke-test.py')
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


class Form(smoke.Form):
    def __init__(self, html):
        self.selected = {}
        self.prices = {}
        super().__init__(html)

    def handle_starttag(self, tag, attrs):
        super().handle_starttag(tag, attrs)
        attrs = dict(attrs)
        if tag == 'option':
            if 'selected' in attrs:
                self.selected[self.select] = attrs.get('value')
            if 'data-price' in attrs:
                self.prices[attrs['value']] = Decimal(attrs['data-price'])


def process(args, input_text=None):
    result = subprocess.run(args, input=input_text, capture_output=True, text=True,
                            encoding='utf-8', errors='replace', timeout=90)
    if result.returncode:
        # Never print captured logs, configuration or SQL request data.
        raise RuntimeError('Subprocess failed: ' + args[0] + ' (exit ' + str(result.returncode) + ')')
    return result.stdout


def execute_sql(batch):
    # Credentials are read inside the known demo container, never passed as arguments.
    script = 'SQLCMDPASSWORD=$(cat /run/secrets/sql-password); export SQLCMDPASSWORD; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d PortalPedidosDockerDemo -y 0 -w 65535'
    return process(COMPOSE + ['exec', '-T', 'sql', 'bash', '-c', script],
                   'SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;\n' + batch + '\n')


def sql(select):
    output = execute_sql(select + ' FOR JSON PATH;')
    # sqlcmd emits no text for FOR JSON PATH over an empty result set.
    return json.loads(output.strip(), parse_float=Decimal) if output.strip() else []


def order_state(order_id):
    oid = str(uuid.UUID(order_id))
    return sql(f"SELECT Id, PublicNumber, Status, Subtotal, ExternalOrderId FROM SalesOrders WHERE Id='{oid}'")


class Session:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({}),
            urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=str(PRIVATE / 'ca.crt'))),
            urllib.request.HTTPCookieProcessor(self.jar))

    def request(self, path, data=None):
        body = urllib.parse.urlencode(data).encode() if data is not None else None
        try:
            response = self.opener.open(BASE + path, data=body, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            return response.code, response.geturl(), response.read().decode('utf-8')

    def login(self, email):
        code, _, html = self.request('/Account/Login')
        assert code == 200
        data = {'Email': email, 'Password': (PRIVATE / 'seed-password').read_text().strip(),
                '__RequestVerificationToken': Form(html).inputs['__RequestVerificationToken']}
        code, url, _ = self.request('/Account/Login', data)
        assert code == 200 and '/Account/Login' not in url, 'Demo login failed.'

    def logout(self):
        _, _, html = self.request('/Orders')
        token = Form(html).inputs['__RequestVerificationToken']
        code, _, _ = self.request('/Account/Logout', {'__RequestVerificationToken': token})
        assert code == 200
        assert not any(cookie.name == 'PortalPedidos.Demo.Auth' for cookie in self.jar)
        code, url, _ = self.request('/Orders/Create')
        assert code == 200 and '/Account/Login' in url, 'Logged-out client retained access.'


def main():
    global BASE, COMPOSE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project-name', default='portalpedidos-demo')
    parser.add_argument('--port', type=int, default=8271)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('Invalid port')
    BASE = f'https://localhost:{args.port}'
    COMPOSE = ['docker', 'compose', '-p', args.project_name, '-f', str(ROOT / 'compose.yaml')]
    if REPORT.exists():
        raise RuntimeError('An existing functional-report.json is preserved; review it before another run.')
    report = {'label': 'DOCKER-FUNCTIONAL-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'),
              'method': 'HTTPS MVC forms + read-only SQL in ' + args.project_name, 'passed': [], 'failed': []}

    def save():
        REPORT.write_text(json.dumps(report, indent=2, default=str), encoding='utf-8')

    def passed(message):
        report['passed'].append(message)
        save()
        print('PASS:', message, flush=True)

    erp_interrupted = False
    try:
        for service in ['sql', 'erp', 'portal']:
            container = process(COMPOSE + ['ps', '-q', service]).strip()
            data = json.loads(process(['docker', 'inspect', container]))[0]
            assert data['Config']['Labels']['com.docker.compose.project'] == args.project_name
            assert data['State']['Running'] and data['State']['Health']['Status'] == 'healthy'
        for filename, key, database in [('portal.json', 'PortalDb', 'PortalPedidosDockerDemo'),
                                        ('erp.json', 'ErpSimulatorDb', 'ErpSimulatorDockerDemo')]:
            config = json.loads((PRIVATE / filename).read_text())
            parts = dict(item.split('=', 1) for item in config['ConnectionStrings'][key].split(';') if '=' in item)
            assert parts['Server'] == 'sql,1433' and parts['Database'] == database
        assert sql('SELECT DB_NAME() AS Name')[0]['Name'] == 'PortalPedidosDockerDemo'
        passed('Scope: healthy demo services and exact Docker database destinations verified.')

        admin, seller = Session(), Session()
        admin.login('admin@portal.test')
        seller.login('vendedor@portal.test')
        for session in [admin, seller]:
            for path in ['/Customers', '/Catalog', '/Orders/Create']:
                code, url, html = session.request(path)
                assert code == 200 and '/Account/' not in url
                if path == '/Customers':
                    assert 'C009' in html
                if path == '/Catalog':
                    assert 'P013' in html
        for path in ['/Users', '/Integration', '/Audit']:
            assert admin.request(path)[0] == 200
            assert seller.request(path)[0] == 403, 'Seller must be denied by backend: ' + path
        passed('Admin/seller login, clients and catalog; seller denied Users, Integration and Audit by backend (403).')

        _, _, html = seller.request('/Orders/Create')
        form = Form(html)
        oid = str(uuid.UUID(form.inputs['DraftId']))
        report['orderId'] = oid
        data = {key: form.inputs[key] for key in ['DraftId', 'CatalogVersion', 'RequestedDate', '__RequestVerificationToken']}
        def lookup(path):
            status, url, body = seller.request(path)
            assert status == 200
            return url, body

        options = [smoke.search_option(lookup, 'SearchProducts', code) for code in ['P013', 'P014']]
        products = [p['id'] for p in options]
        prices = {p['id']: Decimal(str(p['unitPrice'])) for p in options}
        data.update({'CustomerId': smoke.search_option(lookup, 'SearchCustomers', 'C009')['id'], 'Lines[0].ProductId': products[0],
                     'Lines[0].Quantity': '0', 'Lines[0].QuotedPrice': '0.01', 'Lines[1].ProductId': products[1], 'Lines[1].Quantity': '3'})
        code, url, html = seller.request('/Orders/Create', data)
        assert code == 200 and '/Orders/Create' in url
        assert 'entre 1 y 9999' in html and not order_state(oid), 'Invalid quantity was persisted.'
        passed('Quantity zero rejected by server validation; no order persisted.')
        data['Lines[0].Quantity'] = '2'
        # Use the form actually returned after validation, including the reviewed version.
        data['CatalogVersion'] = Form(html).inputs['CatalogVersion']
        code, url, html = seller.request('/Orders/Create', data)
        if code == 200 and '/Orders/Create' in url and 'catálogo cambió' in unescape(html):
            refreshed = Form(html)
            assert refreshed.selected['CustomerId'] == data['CustomerId']
            assert [refreshed.selected[f'Lines[{i}].ProductId'] for i in range(2)] == products
            assert refreshed.inputs['CatalogVersion'] != data['CatalogVersion']
            data['CatalogVersion'] = refreshed.inputs['CatalogVersion']
            code, url, _ = seller.request('/Orders/Create', data)
        assert code == 200 and '/Orders/Details/' in url, f'Draft creation returned {code} at {url}.'
        draft = order_state(oid)[0]
        report['publicNumber'] = draft['PublicNumber']
        report['customer'] = 'C009'
        report['lines'] = [{'code': code, 'quantity': qty, 'price': prices[product]}
                           for code, qty, product in zip(['P013', 'P014'], [2, 3], products)]
        subtotal = prices[products[0]] * 2 + prices[products[1]] * 3
        report['subtotal'] = subtotal
        assert draft['Status'] == 'Draft' and draft['Subtotal'] == subtotal
        lines = sql(f"SELECT ItemCode, Quantity, UnitPrice, LineAmount FROM SalesOrderLines WHERE OrderId='{oid}' ORDER BY LineNumber")
        for actual, expected in zip(lines, report['lines'], strict=True):
            assert actual['ItemCode'] == expected['code'] and actual['Quantity'] == expected['quantity']
            assert actual['UnitPrice'] == expected['price'] and actual['LineAmount'] == expected['price'] * expected['quantity']
        for _ in range(2):
            code, _, html = seller.request('/Orders/Edit/' + oid)
            edit = Form(html)
            assert code == 200 and edit.selected['CustomerId'] == data['CustomerId']
            for index, product in enumerate(products):
                assert edit.selected[f'Lines[{index}].ProductId'] == product
                assert edit.inputs[f'Lines[{index}].Quantity'] == str([2, 3][index])
                assert edit.prices[product] == prices[product]
            assert format(subtotal, '.2f') in html
        passed('Draft reopened twice: two lines, quantities 2/3, prices and subtotal preserved in HTML and SQL.')

        _, _, html = seller.request('/Orders/Submit/' + oid)
        form = Form(html)
        submission = {key: form.inputs[key] for key in ['Id', 'RowVersion', '__RequestVerificationToken']}
        submission['ConfirmSubmission'] = 'true'
        stock_before = sql("SELECT ItemCode, OnHand, Reserved FROM ErpSimulatorDockerDemo.dbo.ErpStocks WHERE ItemCode IN ('P013','P014') ORDER BY ItemCode")
        # Set the flag before stopping, so all failure paths attempt to restore the ERP.
        erp_interrupted = True
        process(COMPOSE + ['stop', 'erp'])
        code, url, _ = seller.request('/Orders/Submit/' + oid, submission)
        assert code == 200 and '/Orders/Details/' in url
        for _ in range(20):
            messages = sql(f"SELECT Id, Status, AttemptCount, LastErrorCode FROM OutboxMessages WHERE OrderId='{oid}'")
            if len(messages) == 1 and messages[0]['Status'] in ['RetryWaiting', 'Unknown']:
                break
            time.sleep(2)
        else:
            raise AssertionError('No scheduled recovery observed while ERP was stopped.')
        message_id = messages[0]['Id']
        report['messageId'] = message_id
        report['interruption'] = messages[0]
        assert order_state(oid)[0]['Status'] == 'Queued'
        assert not sql(f"SELECT Id FROM ErpSimulatorDockerDemo.dbo.ErpOrderReceipts WHERE ExternalReference='{oid}'")
        assert seller.request('/Orders/Submit/' + oid, submission)[0] == 200
        assert len(sql(f"SELECT Id FROM OutboxMessages WHERE OrderId='{oid}'")) == 1
        assert admin.request('/Integration/Details/' + message_id)[0] == 200
        passed('ERP interruption: order stays Queued, communication retry scheduled, no rejection/receipt; duplicate submit creates no second outbox message.')
        process(COMPOSE + ['start', 'erp'])
        erp_interrupted = False
        for _ in range(45):
            state = order_state(oid)[0]
            if state['Status'] == 'Confirmed':
                break
            assert state['Status'] != 'Rejected', 'Communication failure became a commercial rejection.'
            time.sleep(2)
        else:
            raise AssertionError('Automatic processing did not recover within 90 seconds.')
        receipts = sql(f"SELECT Id, Status, ExternalOrderId FROM ErpSimulatorDockerDemo.dbo.ErpOrderReceipts WHERE ExternalReference='{oid}'")
        assert len(receipts) == 1 and receipts[0]['Status'] == 'Accepted'
        assert receipts[0]['ExternalOrderId'] == state['ExternalOrderId']
        report['erpReference'] = state['ExternalOrderId']
        stock_after = sql("SELECT ItemCode, OnHand, Reserved FROM ErpSimulatorDockerDemo.dbo.ErpStocks WHERE ItemCode IN ('P013','P014') ORDER BY ItemCode")
        for before, after, quantity in zip(stock_before, stock_after, [2, 3], strict=True):
            assert after['OnHand'] == before['OnHand'] and after['Reserved'] - before['Reserved'] == quantity
        assert seller.request('/Orders/Submit/' + oid, submission)[0] == 200
        assert len(sql(f"SELECT Id FROM OutboxMessages WHERE OrderId='{oid}'")) == 1
        assert sql(f"SELECT Id, Status, ExternalOrderId FROM ErpSimulatorDockerDemo.dbo.ErpOrderReceipts WHERE ExternalReference='{oid}'") == receipts
        assert sql("SELECT ItemCode, OnHand, Reserved FROM ErpSimulatorDockerDemo.dbo.ErpStocks WHERE ItemCode IN ('P013','P014') ORDER BY ItemCode") == stock_after
        passed('ERP recovered automatically: exactly one Accepted receipt and stock reservation; repeated confirmed submission has no duplicate commercial effect.')

        history = sql(f"SELECT PreviousStatus, NewStatus, Reason FROM OrderStatusHistories WHERE OrderId='{oid}' ORDER BY Id")
        assert [item['NewStatus'] for item in history] == ['Draft', 'Queued', 'Confirmed']
        attempts = sql(f"SELECT AttemptNumber, Operation, HttpStatus, ResultCode FROM IntegrationAttempts WHERE MessageId='{message_id}' ORDER BY AttemptNumber")
        assert len(attempts) >= 2 and any(item.get('HttpStatus') == 200 for item in attempts)
        audits = sql(f"SELECT Id, Action FROM AuditEvents WHERE EntityId='{oid}' OR EntityId='{message_id}' ORDER BY Id")
        assert {'Order.DraftCreated', 'Order.SubmissionQueued', 'Order.ErpConfirmed'} <= {item['Action'] for item in audits}
        report.update({'history': history, 'attempts': attempts, 'audits': audits, 'stockBefore': stock_before, 'stockAfter': stock_after})
        for path in ['/Orders/Details/' + oid, '/Integration/Details/' + message_id,
                     '/Integration?orderNumber=' + str(draft['PublicNumber'])]:
            code, _, html = admin.request(path)
            assert code == 200 and str(draft['PublicNumber']) in html
            if path.startswith('/Orders/Details/'):
                assert state['ExternalOrderId'] in html and 'Confirmado' in html
        assert seller.request('/Integration/Details/' + message_id)[0] == 403
        code, _, html = admin.request('/Audit?Search=' + oid)
        assert code == 200
        for audit in audits:
            code, _, html = admin.request('/Audit/Details/' + str(audit['Id']))
            assert code == 200 and (oid in html.lower() or message_id.lower() in html.lower())
        passed('Order/ERP references, status history, failed/successful attempts, integration monitor and audit details verified.')
        admin.logout()
        seller.logout()
        passed('Both sessions logged out; auth cookies removed and protected requests redirect to Login.')
    except Exception as error:
        report['failed'].append(type(error).__name__ + ': ' + str(error))
        save()
        raise
    finally:
        if erp_interrupted:
            process(COMPOSE + ['start', 'erp'])
        save()


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print('FAIL:', type(error).__name__, str(error), flush=True)
        sys.exit(1)
