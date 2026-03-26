#!/bin/bash
set -e

PUID=${PUID:-0}
PGID=${PGID:-${PUID}}
UMASK=${UMASK:-022}

umask "$UMASK"

# If running as root and PUID/PGID are set to non-root, create user and switch
if [ "$(id -u)" = "0" ] && { [ "$PUID" != "0" ] || [ "$PGID" != "0" ]; }; then
    echo "Starting Listenarr with UID=$PUID GID=$PGID UMASK=$UMASK"

    if [ "$PGID" != "0" ]; then
        groupmod -o -g "$PGID" listenarr 2>/dev/null || addgroup --gid "$PGID" listenarr
    fi
    usermod -o -u "$PUID" -g "$PGID" listenarr 2>/dev/null || adduser --uid "$PUID" --gid "$PGID" --disabled-password --gecos "" --no-create-home listenarr

    chown -R "$PUID:$PGID" /app/config

    exec gosu "$PUID:$PGID" dotnet Listenarr.Api.dll "$@"
fi

echo "Starting Listenarr as $(id)"
exec dotnet Listenarr.Api.dll "$@"
