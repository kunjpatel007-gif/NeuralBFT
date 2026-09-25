import http.server
import socketserver
import os

PORT = 8000

class Handler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        # Add headers for Unity WebGL Brotli/Gzip compression
        if self.path.endswith('.br'):
            self.send_header('Content-Encoding', 'br')
            self.send_header('Content-Type', 'application/wasm' if 'wasm' in self.path else 'application/javascript')
        elif self.path.endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
            self.send_header('Content-Type', 'application/wasm' if 'wasm' in self.path else 'application/javascript')
        
        if self.path.endswith('.wasm'):
            self.send_header('Content-Type', 'application/wasm')
            
        super().end_headers()

with socketserver.TCPServer(("", PORT), Handler) as httpd:
    print(f"Serving at http://localhost:{PORT}")
    httpd.serve_forever()
