"""Real HTTPS form test against the isolated demo. Python 3, standard library only.

Creates one order (C001 / P001 / quantity 1), submits it and waits for confirmation.
--verify-existing checks the saved order and session after restarting services.
Credentials and cookies stay inside .docker-local; never printed.
"""
import http.cookiejar
import argparse
import json
from html.parser import HTMLParser
from pathlib import Path
import re
import ssl
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
PRIVATE = ROOT / '.docker-local'
BASE = 'https://localhost:8271'


class Form(HTMLParser):
    def __init__(self, html):
        super().__init__()
        self.inputs = {}
        self.options = {}
        self.select = None
        self.option = None
        self.feed(html)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'input' and attrs.get('name'):
            self.inputs.setdefault(attrs['name'], attrs.get('value', ''))
        if tag == 'select':
            self.select = attrs.get('name')
        if tag == 'option' and self.select:
            self.option = [attrs.get('value', ''), '']

    def handle_data(self, data):
        if self.option is not None:
            self.option[1] += data

    def handle_endtag(self, tag):
        if tag == 'option' and self.option is not None:
            self.options.setdefault(self.select, []).append(self.option)
            self.option = None
        if tag == 'select':
            self.select = None

def search_option(request, action, code):
    _, body = request('/Orders/' + action + '?' + urllib.parse.urlencode({'term': code}))
    first = json.loads(body)['items'][0]
    assert first['code'] == code
    return first


def main():
    global BASE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=8271)
    parser.add_argument('--verify-existing', action='store_true')
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('Invalid port')
    BASE = f'https://localhost:{args.port}'
    context = ssl.create_default_context(cafile=str(PRIVATE / 'ca.crt'))
    jar = http.cookiejar.MozillaCookieJar(str(PRIVATE / 'test-cookies.txt'))
    opener = urllib.request.build_opener(
        urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context),
        urllib.request.HTTPCookieProcessor(jar))

    def request(path, data=None):
        payload = urllib.parse.urlencode(data).encode() if data is not None else None
        with opener.open(BASE + path, data=payload, timeout=20) as response:
            return response.geturl(), response.read().decode()

    if args.verify_existing:
        jar.load(ignore_discard=True)
        state = json.loads((PRIVATE / 'test-order.json').read_text())
        url, html = request('/Orders/Details/' + state['id'])
        assert '/Orders/Details/' in url, 'Session did not survive restart.'
        assert re.search(r'badge bg-success[^>]*>\s*Confirmado\s*</span>', html), 'Order did not remain confirmed.'
        print('PASS: saved authenticated session and confirmed order survived restart.')
        return

    for path in ['/health/live', '/health/ready']:
        request(path)
    print('PASS: HTTPS hostname/CA validation and portal health.')
    _, html = request('/Account/Login')
    token = Form(html).inputs['__RequestVerificationToken']
    # A POST without antiforgery must fail, even with valid credentials.
    try:
        request('/Account/Login', {'Email': 'vendedor@portal.test', 'Password': 'invalid'})
        raise AssertionError('Missing antiforgery token was accepted.')
    except urllib.error.HTTPError as error:
        assert error.code == 400
    print('PASS: antiforgery rejects an unprotected POST.')
    url, _ = request('/Account/Login', {
        'Email': 'vendedor@portal.test', 'Password': (PRIVATE / 'seed-password').read_text().strip(),
        'RememberMe': 'false', '__RequestVerificationToken': token})
    assert '/Account/Login' not in url, 'Login failed.'
    auth = next(cookie for cookie in jar if cookie.name == 'PortalPedidos.Demo.Auth')
    assert auth.secure and any(key.lower() == 'httponly' for key in auth._rest)
    assert any(k.lower() == 'samesite' and str(v).lower() == 'lax' for k, v in auth._rest.items())
    print('PASS: seller login and Secure/HttpOnly/SameSite=Lax demo cookie.')

    _, html = request('/Orders/Create')
    form = Form(html)
    order_id = form.inputs['DraftId']
    data = {key: form.inputs[key] for key in ['DraftId', 'CatalogVersion', 'RequestedDate', '__RequestVerificationToken']}
    data.update({'CustomerId': search_option(request, 'SearchCustomers', 'C001')['id'],
                 'Lines[0].ProductId': search_option(request, 'SearchProducts', 'P001')['id'],
                 'Lines[0].Quantity': '1'})
    url, _ = request('/Orders/Create', data)
    assert '/Orders/Details/' in url and order_id in url, 'Draft was not created.'
    _, html = request('/Orders/Submit/' + order_id)
    form = Form(html)
    data = {key: form.inputs[key] for key in ['Id', 'RowVersion', '__RequestVerificationToken']}
    data['ConfirmSubmission'] = 'true'
    url, _ = request('/Orders/Submit/' + order_id, data)
    assert '/Orders/Details/' in url, 'Submission did not redirect to the order.'
    for _ in range(20):
        _, html = request('/Orders/Details/' + order_id)
        if re.search(r'badge bg-success[^>]*>\s*Confirmado\s*</span>', html):
            break
        time.sleep(2)
    else:
        raise AssertionError('Order did not reach Confirmado within 40 seconds.')
    jar.save(ignore_discard=True)
    (PRIVATE / 'test-order.json').write_text(json.dumps({'id': order_id}))
    print('PASS: created draft, submitted it, outbox delivered it and ERP confirmed it.')


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Never dump HTTP response bodies, request data or credentials on failures.
        print('FAIL:', type(error).__name__, str(error))
        sys.exit(1)
