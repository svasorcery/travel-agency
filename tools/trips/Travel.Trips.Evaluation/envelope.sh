#!/bin/sh
set -eu
docker-entrypoint.sh postgres -c listen_addresses=127.0.0.1 -c shared_buffers=32MB -c min_wal_size=32MB -c max_wal_size=32MB -c max_connections=8 &
database_pid=$!
stop() { kill -TERM "$database_pid" 2>/dev/null || true; wait "$database_pid" || true; }
trap stop EXIT
trap 'exit 143' TERM INT
wait "$database_pid"
