#!/usr/bin/env python3
"""Loopback stub for an indexer (Torznab) and a qBittorrent client.

Logs every request as one line (port, method, path, body length) to argv[1] so a run can
prove whether a search or a download submission was attempted. Answers with an empty
Torznab feed or minimal qBittorrent responses so callers do not error out.
"""
from __future__ import annotations

import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

EMPTY_FEED = (b'<?xml version="1.0" encoding="UTF-8"?><rss version="2.0" '
              b'xmlns:torznab="http://torznab.com/schemas/2015/feed"><channel><title>stub</title></channel></rss>')
CAPS = (b'<?xml version="1.0" encoding="UTF-8"?><caps><server title="stub"/><searching>'
        b'<search available="yes" supportedParams="q"/><book-search available="yes" supportedParams="q,author,title"/>'
        b'</searching><categories><category id="3030" name="Audio/Audiobook"/></categories></caps>')


def make_handler(log_path: str):
    """Build a request handler bound to one log file."""
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def _record(self) -> int:
            length = int(self.headers.get("Content-Length") or 0)
            if length:
                self.rfile.read(length)
            with lock, open(log_path, "a", encoding="utf-8") as log:
                log.write(f"{time.strftime('%H:%M:%S')} port={self.server.server_port} "
                          f"{self.command} {self.path} body={length}\n")
            return length

        def _reply(self, body: bytes, content_type: str) -> None:
            self.send_response(200)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:  # noqa: N802
            self._record()
            if "t=caps" in self.path:
                self._reply(CAPS, "application/xml")
            elif "/api/v2/torrents/info" in self.path:
                self._reply(b"[]", "application/json")
            elif "/api/v2/app/version" in self.path:
                self._reply(b"v4.6.0", "text/plain")
            elif "/api/v2/app/webapiVersion" in self.path:
                self._reply(b"2.9.3", "text/plain")
            elif "/api/v2/app/preferences" in self.path:
                self._reply(b'{"save_path":"/downloads"}', "application/json")
            elif "/api/v2/torrents/categories" in self.path:
                self._reply(b"{}", "application/json")
            else:
                self._reply(EMPTY_FEED, "application/xml")

        def do_POST(self) -> None:  # noqa: N802
            self._record()
            self._reply(b"Ok.", "text/plain")

        def log_message(self, *args) -> None:
            return

    return Handler


def main() -> int:
    """Serve on every port given after the log path until killed."""
    if len(sys.argv) < 3:
        print("usage: stub_server.py <log> <port>...", file=sys.stderr)
        return 2
    handler = make_handler(sys.argv[1])
    servers = [ThreadingHTTPServer(("127.0.0.1", int(port)), handler) for port in sys.argv[2:]]
    for server in servers[1:]:
        threading.Thread(target=server.serve_forever, daemon=True).start()
    servers[0].serve_forever()
    return 0


if __name__ == "__main__":
    sys.exit(main())
