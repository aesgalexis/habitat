"""HABITAT: minimal Windows desktop chat surface for an existing ChatGPT tab."""
import json
import queue
import secrets
import threading
import time
import tkinter as tk
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from tkinter import scrolledtext, ttk

HOST, PORT = "127.0.0.1", 8765
pending = queue.Queue()
events = queue.Queue()
pair_key = secrets.token_urlsafe(24)
state = {"last_poll": 0.0, "inflight": None, "inflight_since": None}
lock = threading.Lock()


class Bridge(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def reply(self, code, obj):
        payload = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def read_data(self):
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size < 200000:
                return None
            return json.loads(self.rfile.read(size))
        except (ValueError, json.JSONDecodeError):
            return None

    def authorized(self):
        return secrets.compare_digest(self.headers.get("X-Habitat-Key", ""), pair_key)

    def do_GET(self):
        if self.path == "/pair":
            # A normal web page cannot add this header without a successful
            # CORS preflight (which this server deliberately does not allow).
            # The installed extension has explicit loopback host permission.
            if self.headers.get("X-Habitat-Pair") != "1":
                return self.reply(403, {"error": "Pairing denied"})
            return self.reply(200, {"key": pair_key})
        if not self.authorized():
            return self.reply(403, {"error": "Unauthorized"})
        if self.path != "/next":
            return self.reply(404, {"error": "Not found"})
        with lock:
            state["last_poll"] = time.monotonic()
        try:
            item = pending.get_nowait()
        except queue.Empty:
            item = None
        return self.reply(200, {"message": item})

    def do_POST(self):
        if not self.authorized():
            return self.reply(403, {"error": "Unauthorized"})
        data = self.read_data()
        if not isinstance(data, dict):
            return self.reply(400, {"error": "Invalid JSON"})
        if self.path != "/event":
            return self.reply(404, {"error": "Not found"})
        if data.get("type") not in ("started", "response", "error", "status"):
            return self.reply(400, {"error": "Invalid event"})
        events.put(data)
        return self.reply(200, {"ok": True})


def main():
    root = tk.Tk()
    root.title("HABITAT · ChatGPT bridge")
    root.geometry("760x650")
    root.minsize(500, 420)
    root.configure(bg="#10131a")
    root.grid_columnconfigure(0, weight=1)
    root.grid_rowconfigure(1, weight=1)

    header = tk.Frame(root, bg="#10131a")
    header.grid(row=0, column=0, sticky="ew", padx=22, pady=(20, 12))
    tk.Label(header, text="HABITAT", fg="#e8edff", bg="#10131a",
             font=("Segoe UI", 18, "bold")).pack(side="left")
    status = tk.StringVar(value="Abre ChatGPT y conecta la extensión")
    tk.Label(header, textvariable=status, fg="#a1b2d6", bg="#10131a",
             font=("Segoe UI", 10)).pack(side="right")

    output = scrolledtext.ScrolledText(root, bg="#191f2b", fg="#e9ecf5",
                                       insertbackground="white", wrap="word",
                                       font=("Segoe UI", 11), relief="flat",
                                       padx=18, pady=16, state="disabled")
    output.grid(row=1, column=0, sticky="nsew", padx=22, pady=(0, 12))
    output.tag_configure("you", foreground="#91c5ff")
    output.tag_configure("assistant", foreground="#d8e6ff")
    output.tag_configure("muted", foreground="#9aa8bf")

    def append(title, body, tag):
        output.configure(state="normal")
        output.insert("end", title + "\n", tag)
        output.insert("end", body + "\n\n")
        output.configure(state="disabled")
        output.see("end")

    keyrow = tk.Frame(root, bg="#10131a")
    keyrow.grid(row=2, column=0, sticky="ew", padx=22, pady=(0, 8))
    tk.Label(keyrow, text="Código de vinculación:", bg="#10131a",
             fg="#9aa8bf").pack(side="left")
    key_field = ttk.Entry(keyrow, width=36)
    key_field.insert(0, pair_key)
    key_field.configure(state="readonly")
    key_field.pack(side="left", padx=8)
    def copy_key():
        root.clipboard_clear()
        root.clipboard_append(pair_key)
    ttk.Button(keyrow, text="Copiar", command=copy_key).pack(side="left")

    bottom = tk.Frame(root, bg="#10131a")
    bottom.grid(row=3, column=0, sticky="ew", padx=22, pady=(0, 22))
    entry = tk.Text(bottom, height=3, bg="#252d3d", fg="white",
                    insertbackground="white", font=("Segoe UI", 11),
                    wrap="word", relief="flat", padx=10, pady=9)
    entry.pack(side="left", fill="x", expand=True)
    send = ttk.Button(bottom, text="Enviar")
    send.pack(side="left", padx=(10, 0))

    def submit(_=None):
        message = entry.get("1.0", "end-1c").strip()
        if not message:
            return "break"
        with lock:
            if time.monotonic() - state["last_poll"] >= 4.0:
                status.set("Conecta primero la extensión en una pestaña de ChatGPT")
                return "break"
            if state["inflight"] is not None:
                status.set("Espera a que termine la respuesta")
                return "break"
            message_id = secrets.token_hex(8)
            state["inflight"] = message_id
            state["inflight_since"] = time.monotonic()
        pending.put({"id": message_id, "text": message})
        append("TÚ", message, "you")
        entry.delete("1.0", "end")
        status.set("Enviando a Chrome…")
        return "break"

    send.configure(command=submit)
    entry.bind("<Control-Return>", submit)

    def pump():
        with lock:
            last_poll = state["last_poll"]
            inflight = state["inflight"]
            inflight_since = state["inflight_since"]
        connected = time.monotonic() - last_poll < 4.0
        if connected and inflight is None:
            status.set("Extensión conectada")
        elif not connected and inflight is None:
            status.set("Abre ChatGPT y conecta la extensión")
        if inflight is not None and inflight_since is not None \
                and time.monotonic() - inflight_since > 240:
            with lock:
                if state["inflight"] == inflight:
                    state["inflight"] = None
                    state["inflight_since"] = None
            append("HABITAT", "Se agotó el tiempo de espera. Revisa ChatGPT antes de volver a enviar.", "muted")
            status.set("Tiempo agotado; revisa Chrome")
        while True:
            try:
                event = events.get_nowait()
            except queue.Empty:
                break
            kind = event.get("type")
            ident = event.get("id")
            if kind == "status":
                status.set(str(event.get("text", ""))[:100])
                continue
            with lock:
                if ident != state["inflight"]:
                    continue
                if kind in ("response", "error"):
                    state["inflight"] = None
                    state["inflight_since"] = None
            if kind == "started":
                status.set("ChatGPT está respondiendo…")
            elif kind == "response":
                append("CHATGPT", str(event.get("text", "")), "assistant")
                status.set("Respuesta recibida")
            elif kind == "error":
                append("HABITAT", str(event.get("text", "Error desconocido")), "muted")
                status.set("Error en el puente")
        root.after(200, pump)

    try:
        server = ThreadingHTTPServer((HOST, PORT), Bridge)
    except OSError as exc:
        append("HABITAT", f"No se puede abrir el puerto {PORT}: {exc}", "muted")
        root.mainloop()
        return
    threading.Thread(target=server.serve_forever, daemon=True).start()
    append("HABITAT", "1. Instala la extensión desde la carpeta extension.\n"
           "2. Abre la conversación deseada en Chrome y déjala activa.\n"
           "3. La extensión se vinculará automáticamente.\n"
           "4. Cuando indique Extensión conectada, escribe aquí. Ctrl+Enter para enviar.", "muted")
    root.after(200, pump)
    def close():
        server.shutdown()
        root.destroy()
    root.protocol("WM_DELETE_WINDOW", close)
    root.mainloop()


if __name__ == "__main__":
    main()
