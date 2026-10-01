#!/bin/sh
set -eu

if [ -d /app/wwwroot ]; then
	find /app/wwwroot -type d -exec chmod 755 {} \;
	find /app/wwwroot -type f -exec chmod 644 {} \;
fi

# ffprobe is restored from a NuGet package (Openur.FFprobeStatic). Restore leaves it executable
# by its owner at most, and the app runs as a different user than the one that owns /app, so set
# the mode for everyone here. A RID-specific publish puts it at /app/ffprobe; a RID-agnostic one
# leaves it under runtimes/<rid>/native/.
find /app -maxdepth 1 -type f -name ffprobe -exec chmod 755 {} +
if [ -d /app/runtimes ]; then
	find /app/runtimes -type f -path '*/native/ffprobe' -exec chmod 755 {} +
fi

mkdir -p /app/config/database
