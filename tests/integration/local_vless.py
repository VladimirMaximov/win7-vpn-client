#!/usr/bin/env python3
"""Local HTTP -> mixed proxy -> VLESS -> origin test, with synthetic credentials."""
import argparse
import http.client
import http.server
import json
from pathlib import Path
import socket
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[2]


def free_port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


class Origin(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = b'LOCAL_VLESS_OK'
        self.send_response(200)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Connection', 'close')
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


def run(engine):
    engine = Path(engine).resolve()
    if not engine.is_file():
        raise RuntimeError('Pass --engine with a Mihomo binary for the current OS')
    parent = ROOT / 'artifacts/integration'
    parent.mkdir(parents=True, exist_ok=True)
    # Bind the origin first so its ephemeral port cannot reuse a proposed proxy port.
    origin = http.server.HTTPServer(('127.0.0.1', 0), Origin)
    port, proxy_port = free_port(), free_port()
    while port == proxy_port:
        proxy_port = free_port()
    user = '11111111-1111-4111-8111-111111111111'
    server = {'mixed-port': 0, 'log-level': 'info', 'listeners': [
        {'name': 'local-test', 'type': 'vless', 'listen': '127.0.0.1', 'port': port,
         'allow-insecure': True, 'users': [{'username': 'test', 'uuid': user}]}], 'rules': ['MATCH,DIRECT']}
    client = {'mixed-port': proxy_port, 'allow-lan': False, 'bind-address': '127.0.0.1',
              'proxies': [{'name': 'VPN', 'type': 'vless', 'server': '127.0.0.1',
                           'port': port, 'uuid': user, 'tls': False, 'network': 'tcp'}], 'rules': ['MATCH,VPN']}
    thread = threading.Thread(target=origin.serve_forever, daemon=True)
    thread.start()
    processes, logs = [], []
    with tempfile.TemporaryDirectory(dir=parent) as temporary:
        folder = Path(temporary)
        try:
            for name, config in [('server', server), ('client', client)]:
                home = folder / name
                home.mkdir()
                path = home / 'config.json'
                path.write_text(json.dumps(config))
                log = (home / 'core.log').open('w')
                logs.append(log)
                processes.append(subprocess.Popen([str(engine), '-d', str(home), '-f', str(path)], stdout=log, stderr=log))
            for endpoint in [port, proxy_port]:
                for _ in range(80):
                    if any(p.poll() is not None for p in processes):
                        raise RuntimeError('A local test engine exited')
                    try:
                        with socket.create_connection(('127.0.0.1', endpoint), timeout=.2):
                            break
                    except OSError:
                        time.sleep(.1)
                else:
                    raise RuntimeError('Test listener did not become ready')
            connection = http.client.HTTPConnection('127.0.0.1', proxy_port, timeout=10)
            try:
                connection.request('GET', 'http://127.0.0.1:%d/proof' % origin.server_port)
                response = connection.getresponse()
                body = response.read()
                if response.status != 200 or body != b'LOCAL_VLESS_OK':
                    for log in logs:
                        log.flush()
                    for path in folder.rglob('core.log'):
                        print(path.parent.name + ': ' + path.read_text()[-2000:])
                    raise AssertionError('Unexpected local proxy response: %s %r' % (response.status, body[:160]))
            finally:
                connection.close()
            print('PASS local VLESS transport; no subscription or Internet required')
        finally:
            origin.shutdown()
            origin.server_close()
            for process in processes:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            for log in logs:
                log.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--engine', required=True, help='Mihomo executable for the current OS')
    run(parser.parse_args().engine)
