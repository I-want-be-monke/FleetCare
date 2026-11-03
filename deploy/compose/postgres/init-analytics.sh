#!/bin/sh
set -eu
: "${FLEETCARE_ANALYTICS_PASSWORD:?FLEETCARE_ANALYTICS_PASSWORD is required}"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v analytics_password="$FLEETCARE_ANALYTICS_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE fleetcare_analytics_worker WITH LOGIN PASSWORD %L',
              :'analytics_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fleetcare_analytics_worker')
\gexec
ALTER ROLE fleetcare_analytics_worker SET default_transaction_read_only = on;
SQL
echo "Analytics role initialized."
