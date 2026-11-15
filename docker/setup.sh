#!/bin/bash
set -euo pipefail
umask 077
cd /output
if [ -f prepared ]; then
    for file in ca.crt portal.pfx erp.pfx sql.crt sql.key portal.json erp.json sql-password seed-password; do
        test -s "$file" || { echo "Incomplete demo configuration; refusing to overwrite." >&2; exit 1; }
    done
    echo "Existing demo credentials preserved."
    exit 0
fi
if [ -n "$(ls -A /output)" ]; then
    echo "Output is not empty; refusing to overwrite existing files." >&2
    exit 1
fi

sql_password="Aa1!$(openssl rand -hex 24)"
seed_password="Aa1!$(openssl rand -hex 24)"
api_key="$(openssl rand -hex 32)"
pfx_password="$(openssl rand -hex 24)"
printf '%s' "$sql_password" > sql-password
printf '%s' "$seed_password" > seed-password
openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 365 -keyout ca.key -out ca.crt \
    -subj '/CN=PortalPedidos Docker Demo CA' -addext 'basicConstraints=critical,CA:TRUE' \
    -addext 'keyUsage=critical,keyCertSign,cRLSign' 2>/dev/null
for service in portal erp sql; do
    openssl req -new -newkey rsa:3072 -nodes -keyout "$service.key" -out "$service.csr" \
        -subj "/CN=$service" 2>/dev/null
    printf 'subjectAltName=DNS:%s,DNS:localhost\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n' "$service" > "$service.ext"
    openssl x509 -req -in "$service.csr" -CA ca.crt -CAkey ca.key -CAcreateserial \
        -out "$service.crt" -days 180 -sha256 -extfile "$service.ext" 2>/dev/null
    printf '%s' "$pfx_password" | openssl pkcs12 -export -out "$service.pfx" \
        -inkey "$service.key" -in "$service.crt" -certfile ca.crt -passout stdin
done
cat > portal.json <<EOF
{
  "ConnectionStrings": {"PortalDb": "Server=sql,1433;Database=PortalPedidosDockerDemo;User ID=sa;Password=$sql_password;Encrypt=True;TrustServerCertificate=False"},
  "Erp": {"BaseUrl": "https://erp:8443/", "ApiKey": "$api_key"},
  "Kestrel": {"Certificates": {"Default": {"Password": "$pfx_password"}}},
  "CatalogSync": {"Enabled": true}, "Outbox": {"Enabled": true}
}
EOF
cat > erp.json <<EOF
{
  "ConnectionStrings": {"ErpSimulatorDb": "Server=sql,1433;Database=ErpSimulatorDockerDemo;User ID=sa;Password=$sql_password;Encrypt=True;TrustServerCertificate=False"},
  "Simulator": {"ApiKey": "$api_key", "Faults": {"LoseOrderResponseOnce": false}},
  "Kestrel": {"Certificates": {"Default": {"Password": "$pfx_password"}}}
}
EOF
chown 1654:1654 portal.pfx erp.pfx portal.json erp.json seed-password
chmod 644 ca.crt sql-password sql.crt sql.key
chown 1654:1654 /keys
chmod 700 /keys
touch prepared
echo "Demo credentials and certificates prepared; no secret values printed."
