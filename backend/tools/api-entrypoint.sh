#!/bin/sh
set -eu

if [ "$(id -u)" -eq 0 ]; then
    install -d -o "$APP_UID" -g 0 -m 0750 /data/audio /data/covers /data/keys
    exec setpriv --reuid="$APP_UID" --regid=0 --clear-groups "$@"
fi
exec "$@"
