#!/bin/bash
set -euo pipefail
export MSSQL_SA_PASSWORD="$(cat /run/secrets/sql-password)"
mkdir -p /var/opt/mssql/certs
cp /run/secrets/sql.crt /var/opt/mssql/certs/sql.crt
cp /run/secrets/sql.key /var/opt/mssql/certs/sql.key
chmod 600 /var/opt/mssql/certs/sql.key
exec /opt/mssql/bin/sqlservr
