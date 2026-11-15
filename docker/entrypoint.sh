#!/bin/bash
set -euo pipefail
# Keep platform roots, and trust only the public demo CA in addition.
cat /etc/ssl/certs/ca-certificates.crt /https/ca.crt > /tmp/demo-ca-bundle.crt
export SSL_CERT_FILE=/tmp/demo-ca-bundle.crt
exec dotnet "$@"
