#!/bin/sh
set -eu

data=/var/opt/mssql
mkdir -p "$data"

# Clean up any diagnostic core dumps left by earlier failed boots.
if [ -d "$data/log" ]; then
    rm -rf "$data"/log/core.sqlservr.*
fi

# If a prior crashed first-boot left half-initialized system DBs (still referencing Microsoft's
# template path mkmastr.nativeproj) and no user database exists yet, clear the broken system files
# so SQL Server performs a clean initial setup with traceflag 1800 already enabled.
if [ -f "$data/log/errorlog" ] && [ ! -f "$data/data/TMusicV2Dev.mdf" ] && \
   tr -d '\000\r' <"$data/log/errorlog" | grep -q 'mkmastr\.nativeproj'; then
    echo "[tmusic-sql] Detected incomplete first-boot system DBs; resetting uninitialized volume state."
    rm -rf "$data/data" "$data/.system" "$data/secrets" "$data/log"
fi

# Railway volumes are ZFS zvols with 16 KB physical sectors; SQL Server supports up to 4 KB.
# Trace flag 1800 aligns transaction log writes to 4 KB, and mini dumps keep crashes small.
/opt/mssql/bin/mssql-conf unset control.writethrough >/dev/null 2>&1 || true
/opt/mssql/bin/mssql-conf unset control.alternatewritethrough >/dev/null 2>&1 || true
/opt/mssql/bin/mssql-conf traceflag 1800 on >/dev/null
/opt/mssql/bin/mssql-conf set coredump.coredumptype mini >/dev/null

# Railway mounts the volume owned by root; ensure mssql owns the volume and mssql.conf.
chown -R mssql:"$(id -g mssql)" "$data"
chmod 0770 "$data"

export HOME="$data"
setpriv --reuid="$(id -u mssql)" --regid="$(id -g mssql)" --init-groups \
    /opt/mssql/bin/launch_sqlservr.sh "$@" &
server=$!

trap 'pkill -TERM -x sqlservr 2>/dev/null || kill -TERM "$server" 2>/dev/null || true' TERM INT
rc=0
wait "$server" || rc=$?
wait "$server" 2>/dev/null || true

if [ -f "$data/log/errorlog" ]; then
    echo "[tmusic-sql] SQL Server exited ($rc). Tail of errorlog:"
    tr -d '\000\r' <"$data/log/errorlog" | tail -30 | sed 's/^/[errorlog] /'
fi
exit "$rc"
