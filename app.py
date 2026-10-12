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
    root.title("HABITAT")
    root.geometry("820x680")
    root.minsize(560, 460)

    canvas = "#eef3f9"
    surface = "#fbfdff"
    surface_alt = "#f5f8fc"
    border = "#dce5f0"
    ink = "#1b2436"
    muted = "#728097"
    accent = "#6674f4"
    accent_hover = "#5665e8"
    success = "#36a078"
    warning = "#cf7a45"
    danger = "#cf5a6b"

    root.configure(bg=canvas)
    try:
        root.attributes("-alpha", 0.985)
    except tk.TclError:
        pass
    root.option_add("*tearOff", False)
    root.grid_columnconfigure(0, weight=1)
    root.grid_rowconfigure(1, weight=1)

    style = ttk.Style(root)
    style.theme_use("clam")
    style.configure(
        "Accent.TButton",
        background=accent,
        foreground="white",
        borderwidth=0,
        focusthickness=0,
        font=("Segoe UI", 10, "bold"),
        padding=(22, 10),
    )
    style.map(
        "Accent.TButton",
        background=[("disabled", "#afb7e5"), ("active", accent_hover)],
        foreground=[("disabled", "#f5f6ff")],
    )
    style.configure(
        "Quiet.TButton",
        background=surface_alt,
        foreground=ink,
        bordercolor=border,
        lightcolor=border,
        darkcolor=border,
        font=("Segoe UI", 10),
        padding=(16, 8),
    )
    style.map("Quiet.TButton", background=[("active", "#eaf0f8")])
    style.configure(
        "Pair.TEntry",
        fieldbackground=surface_alt,
        foreground=ink,
        bordercolor=border,
        lightcolor=border,
        darkcolor=border,
        padding=8,
    )

    header = tk.Frame(root, bg=canvas)
    header.grid(row=0, column=0, sticky="ew", padx=28, pady=(22, 14))
    header.grid_columnconfigure(0, weight=1)

    brand = tk.Frame(header, bg=canvas)
    brand.grid(row=0, column=0, sticky="w")
    tk.Label(
        brand,
        text="HABITAT",
        fg=ink,
        bg=canvas,
        font=("Segoe UI Variable Display", 20, "bold"),
    ).pack(anchor="w")
    tk.Label(
        brand,
        text="Tu conversación de ChatGPT, en una ventana más tranquila",
        fg=muted,
        bg=canvas,
        font=("Segoe UI", 9),
    ).pack(anchor="w", pady=(2, 0))

    status = tk.StringVar(value="Reconectando automáticamente con ChatGPT…")
    status_frame = tk.Frame(header, bg="#e7edf7", padx=11, pady=7)
    status_frame.grid(row=0, column=1, sticky="e", padx=(12, 8))
    status_dot = tk.Label(
        status_frame, text="●", fg=muted, bg="#e7edf7", font=("Segoe UI", 9)
    )
    status_dot.pack(side="left", padx=(0, 6))
    status_label = tk.Label(
        status_frame,
        textvariable=status,
        fg=muted,
        bg="#e7edf7",
        font=("Segoe UI", 9),
    )
    status_label.pack(side="left")

    status_colors = {
        "neutral": (muted, "#e7edf7"),
        "success": (success, "#e4f4ee"),
        "warning": (warning, "#fff0e5"),
        "danger": (danger, "#fdebef"),
        "active": (accent, "#e9ebff"),
    }

    def set_status(text, tone="neutral"):
        foreground, background = status_colors.get(tone, status_colors["neutral"])
        status.set(text)
        status_frame.configure(bg=background)
        status_dot.configure(fg=foreground, bg=background)
        status_label.configure(fg=foreground, bg=background)

    output_card = tk.Frame(
        root,
        bg=surface,
        highlightthickness=1,
        highlightbackground=border,
    )
    output_card.grid(row=1, column=0, sticky="nsew", padx=28, pady=(0, 14))
    output_card.grid_rowconfigure(0, weight=1)
    output_card.grid_columnconfigure(0, weight=1)

    output = scrolledtext.ScrolledText(
        output_card,
        bg=surface,
        fg=ink,
        insertbackground=ink,
        selectbackground="#dfe4ff",
        selectforeground=ink,
        wrap="word",
        font=("Segoe UI", 11),
        relief="flat",
        borderwidth=0,
        padx=22,
        pady=20,
        state="disabled",
    )
    output.grid(row=0, column=0, sticky="nsew")
    output.tag_configure("you", foreground=accent, font=("Segoe UI", 9, "bold"))
    output.tag_configure("assistant", foreground=success, font=("Segoe UI", 9, "bold"))
    output.tag_configure("muted", foreground=muted, font=("Segoe UI", 9, "bold"))

    def append(title, body, tag):
        output.configure(state="normal")
        output.insert("end", title + "\n", tag)
        output.insert("end", body + "\n\n")
        output.configure(state="disabled")
        output.see("end")

    def copy_key():
        root.clipboard_clear()
        root.clipboard_append(pair_key)
        set_status("Código copiado", "active")

    def show_pairing():
        dialog = tk.Toplevel(root)
        dialog.title("Vinculación · HABITAT")
        dialog.configure(bg=canvas)
        dialog.resizable(False, False)
        dialog.transient(root)
        dialog.bind("<Escape>", lambda _: dialog.destroy())
        try:
            dialog.attributes("-alpha", 0.98)
        except tk.TclError:
            pass

        panel = tk.Frame(
            dialog,
            bg=surface,
            highlightthickness=1,
            highlightbackground=border,
            padx=24,
            pady=22,
        )
        panel.pack(fill="both", expand=True, padx=16, pady=16)
        tk.Label(
            panel,
            text="Código de vinculación",
            bg=surface,
            fg=ink,
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w")
        tk.Label(
            panel,
            text=("Solo se necesita como recuperación manual. La extensión "
                  "se reconecta automáticamente al abrir HABITAT."),
            bg=surface,
            fg=muted,
            wraplength=430,
            justify="left",
            font=("Segoe UI", 9),
        ).pack(anchor="w", pady=(6, 14))
        key_field = ttk.Entry(panel, style="Pair.TEntry", width=48)
        key_field.insert(0, pair_key)
        key_field.configure(state="readonly")
        key_field.pack(fill="x")
        actions = tk.Frame(panel, bg=surface)
        actions.pack(fill="x", pady=(16, 0))
        ttk.Button(actions, text="Cerrar", style="Quiet.TButton",
                   command=dialog.destroy).pack(side="right")
        ttk.Button(actions, text="Copiar código", style="Accent.TButton",
                   command=copy_key).pack(side="right", padx=(0, 8))

        dialog.update_idletasks()
        width, height = 520, 255
        x = root.winfo_rootx() + max(0, (root.winfo_width() - width) // 2)
        y = root.winfo_rooty() + max(0, (root.winfo_height() - height) // 2)
        dialog.geometry(f"{width}x{height}+{x}+{y}")

    menu = tk.Menu(
        root,
        bg=surface,
        fg=ink,
        activebackground="#e9edff",
        activeforeground=ink,
        borderwidth=1,
        relief="solid",
        font=("Segoe UI", 10),
    )
    menu.add_command(label="Código de vinculación…", command=show_pairing)
    menu.add_separator()
    menu.add_command(label="Copiar código", command=copy_key)

    def show_options_menu():
        menu.update_idletasks()
        menu_width = max(180, menu.winfo_reqwidth())
        x = overflow.winfo_rootx() + overflow.winfo_width() - menu_width
        y = overflow.winfo_rooty() + overflow.winfo_height()
        try:
            menu.tk_popup(x, y)
        finally:
            menu.grab_release()

    overflow = tk.Button(
        header,
        text="•••",
        command=show_options_menu,
        bg=canvas,
        fg=muted,
        activebackground="#e2e9f3",
        activeforeground=ink,
        relief="flat",
        borderwidth=0,
        cursor="hand2",
        font=("Segoe UI", 13, "bold"),
        padx=8,
        pady=5,
    )
    overflow.grid(row=0, column=2, sticky="e")

    composer = tk.Frame(
        root,
        bg=surface,
        highlightthickness=1,
        highlightbackground=border,
        padx=16,
        pady=14,
    )
    composer.grid(row=2, column=0, sticky="ew", padx=28, pady=(0, 24))
    composer.grid_columnconfigure(0, weight=1)
    entry = tk.Text(
        composer,
        height=3,
        bg=surface,
        fg=ink,
        insertbackground=accent,
        selectbackground="#dfe4ff",
        selectforeground=ink,
        font=("Segoe UI", 11),
        wrap="word",
        relief="flat",
        borderwidth=0,
        padx=3,
        pady=3,
        undo=True,
    )
    entry.grid(row=0, column=0, columnspan=2, sticky="ew")
    tk.Label(
        composer,
        text="Enter para enviar  ·  Shift+Enter para salto de línea",
        bg=surface,
        fg=muted,
        font=("Segoe UI", 8),
    ).grid(row=1, column=0, sticky="w", pady=(10, 0))
    send = ttk.Button(composer, text="Enviar", style="Accent.TButton")
    send.grid(row=1, column=1, sticky="e", pady=(8, 0))

    def submit(_=None):
        message = entry.get("1.0", "end-1c").strip()
        if not message:
            return "break"
        with lock:
            if time.monotonic() - state["last_poll"] >= 4.0:
                set_status("Reconectando con ChatGPT…", "warning")
                return "break"
            if state["inflight"] is not None:
                set_status("Espera a que termine la respuesta", "warning")
                return "break"
            message_id = secrets.token_hex(8)
            state["inflight"] = message_id
            state["inflight_since"] = time.monotonic()
        pending.put({"id": message_id, "text": message})
        append("TÚ", message, "you")
        entry.delete("1.0", "end")
        set_status("Enviando a Chrome…", "active")
        return "break"

    send.configure(command=submit)
    entry.bind("<Return>", submit)
    entry.bind("<Control-Return>", submit)

    def insert_newline(_):
        entry.insert("insert", "\n")
        return "break"

    entry.bind("<Shift-Return>", insert_newline)
    entry.bind("<KP_Enter>", submit)

    def pump():
        with lock:
            last_poll = state["last_poll"]
            inflight = state["inflight"]
            inflight_since = state["inflight_since"]
        connected = time.monotonic() - last_poll < 4.0
        if inflight is None:
            send.state(["!disabled"])
        else:
            send.state(["disabled"])
        if connected and inflight is None:
            set_status("Extensión conectada", "success")
        elif not connected and inflight is None:
            set_status("Reconectando con ChatGPT…", "warning")
        if inflight is not None and inflight_since is not None \
                and time.monotonic() - inflight_since > 240:
            with lock:
                if state["inflight"] == inflight:
                    state["inflight"] = None
                    state["inflight_since"] = None
            append("HABITAT", "Se agotó el tiempo de espera. Revisa ChatGPT antes de volver a enviar.", "muted")
            set_status("Tiempo agotado; revisa Chrome", "danger")
        while True:
            try:
                event = events.get_nowait()
            except queue.Empty:
                break
            kind = event.get("type")
            ident = event.get("id")
            if kind == "status":
                set_status(str(event.get("text", ""))[:100], "active")
                continue
            with lock:
                if ident != state["inflight"]:
                    continue
                if kind in ("response", "error"):
                    state["inflight"] = None
                    state["inflight_since"] = None
            if kind == "started":
                set_status("ChatGPT está respondiendo…", "active")
            elif kind == "response":
                append("CHATGPT", str(event.get("text", "")), "assistant")
                set_status("Respuesta recibida", "success")
            elif kind == "error":
                append("HABITAT", str(event.get("text", "Error desconocido")), "muted")
                set_status("Error en el puente", "danger")
        root.after(200, pump)

    try:
        server = ThreadingHTTPServer((HOST, PORT), Bridge)
    except OSError as exc:
        append("HABITAT", f"No se puede abrir el puerto {PORT}: {exc}", "muted")
        set_status("No se pudo iniciar el puente", "danger")
        root.mainloop()
        return
    threading.Thread(target=server.serve_forever, daemon=True).start()
    append(
        "HABITAT",
        "Conexión automática habilitada. Mantén abierta en Chrome la conversación "
        "deseada y espera a que aparezca ‘Extensión conectada’.\n\n"
        "Enter envía · Shift+Enter crea una nueva línea.",
        "muted",
    )
    root.after(200, pump)
    root.after(250, entry.focus_set)
    def close():
        server.shutdown()
        root.destroy()
    root.protocol("WM_DELETE_WINDOW", close)
    root.mainloop()


if __name__ == "__main__":
    main()
