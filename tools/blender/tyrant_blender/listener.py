"""
Tyrant's door into this Blender: a listener on 127.0.0.1 that accepts exactly one kind of request, one line
{"open": "<absolute path>/tyrant-blender.json"} naming a file that exists. It never runs code. Accepted paths wait in
`pending` until Blender's main thread opens them (bpy must not be used from the listener's thread).
"""
import json
import os
import queue
import socketserver
import threading

from . import project

PORT = 8897

pending = queue.Queue()


def parse(line):
    """(answer, path to open or None) for one request line."""
    try:
        request = json.loads(line)
    except ValueError:
        return {"ok": False, "error": "not JSON"}, None
    if not isinstance(request, dict) or set(request) != {"open"} or not isinstance(request["open"], str):
        return {"ok": False, "error": 'only {"open": "<path to tyrant-blender.json>"} is accepted'}, None
    path = request["open"]
    if not os.path.isabs(path) or not project.is_local(path) or os.path.basename(path) != project.FILE_NAME:
        return {"ok": False, "error": f"the path must be an absolute path to a {project.FILE_NAME}"}, None
    if not os.path.isfile(path):
        return {"ok": False, "error": f"{path} does not exist"}, None
    return {"ok": True}, path


class _Handler(socketserver.StreamRequestHandler):
    timeout = 5

    def handle(self):
        try:
            line = self.rfile.readline(65536).decode("utf-8", errors="replace")
        except OSError:
            return
        answer, path = parse(line)
        if path is not None:
            pending.put(path)
        try:
            self.wfile.write((json.dumps(answer) + "\n").encode("utf-8"))
        except OSError:
            pass


class _TcpServer(socketserver.ThreadingTCPServer):
    daemon_threads = True
    allow_reuse_address = False  # a second Blender must not share the port: Tyrant then starts or uses only one


class Server:
    def __init__(self, port=PORT):
        self._server = _TcpServer(("127.0.0.1", port), _Handler)
        self._thread = None

    @property
    def port(self):
        return self._server.server_address[1]

    def start(self):
        self._thread = threading.Thread(target=self._server.serve_forever, name="tyrant-listener", daemon=True)
        self._thread.start()

    def stop(self):
        self._server.shutdown()
        self._server.server_close()
