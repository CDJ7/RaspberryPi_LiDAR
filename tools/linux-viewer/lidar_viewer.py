#!/usr/bin/env python3
"""LiDAR AGV live viewer for Linux - a DEVELOPMENT TOOL, not part of the graded project.

WHY this exists: the course GUI (LidarPC) is C# WinForms, which only runs on Windows.
This small Tkinter window lets you watch the Pi from a Linux laptop while developing.
It talks to exactly the same REST API as the WinForms GUI (GET /api/scan etc.), so it
adds nothing the C# programs depend on, and it is left out of the submission zip.

Standard library only (tkinter, urllib, json, threading) - no pip install needed.
On Debian/Ubuntu, tkinter comes from:  sudo apt install python3-tk

Usage:
    python3 tools/linux-viewer/lidar_viewer.py                      # defaults to the Pi at 192.168.1.100
    python3 tools/linux-viewer/lidar_viewer.py --host localhost      # the simulator on this laptop
"""

from __future__ import annotations

import argparse
import base64
import json
import math
import threading
import time
import tkinter as tk
import urllib.error
import urllib.request
from tkinter import messagebox, ttk

# Zone colours, identical to LidarPC/Controls/ZoneColors.cs so both GUIs look the same.
ZONE_COLORS = {"Clear": "#2ea043", "Caution": "#e6b400", "Stop": "#d72626"}


class ApiError(Exception):
    """A failed REST call. ``kind`` mirrors ApiErrorKind in the C# client."""

    def __init__(self, message: str, kind: str, status: int | None = None):
        super().__init__(message)
        self.kind = kind          # "unreachable", "timeout", "unauthorized", "rejected", "server"
        self.status = status


class PiClient:
    """Minimal Basic-auth JSON client for the LidarPi REST API (Python twin of RestClientBase/LidarPiClient)."""

    def __init__(self, host: str, port: int, user: str, password: str, timeout: float = 2.5):
        self.base = f"http://{host}:{port}/"
        token = base64.b64encode(f"{user}:{password}".encode()).decode()
        self.headers = {"Authorization": f"Basic {token}", "Accept": "application/json"}
        self.timeout = timeout
        # Never go through an HTTP proxy from the environment: the Pi is on the local network,
        # and a corporate/university proxy would refuse or break the connection.
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def request(self, method: str, path: str, body: dict | None = None) -> dict:
        """Sends one request and returns the decoded JSON, or raises ApiError with a readable message."""
        data = None if body is None else json.dumps(body).encode()
        headers = dict(self.headers)
        if data is not None:
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(self.base + path.lstrip("/"), data=data, headers=headers, method=method)
        try:
            with self.opener.open(req, timeout=self.timeout) as resp:
                return json.loads(resp.read() or b"{}")
        except urllib.error.HTTPError as e:
            try:
                message = json.loads(e.read()).get("error") or f"HTTP {e.code}"
            except (ValueError, AttributeError):
                message = f"HTTP {e.code} {e.reason}"
            kind = {401: "unauthorized", 400: "rejected", 409: "rejected"}.get(e.code, "server")
            raise ApiError(message, kind, e.code) from e
        except TimeoutError as e:
            raise ApiError(f"The Pi did not answer within {self.timeout} s.", "timeout") from e
        except urllib.error.URLError as e:
            if isinstance(e.reason, TimeoutError):
                raise ApiError(f"The Pi did not answer within {self.timeout} s.", "timeout") from e
            raise ApiError(f"Cannot reach {self.base}: {e.reason}", "unreachable") from e

    def get(self, path: str) -> dict:
        """GET a JSON resource."""
        return self.request("GET", path)

    def post(self, path: str, body: dict | None = None) -> dict:
        """POST a JSON body."""
        return self.request("POST", path, body if body is not None else {})


class Viewer(tk.Tk):
    """Main window: login bar, zone banner, live plot, object list and a few commands."""

    POLL_PERIOD_S = 0.1     # ~10 Hz like the WinForms GUI
    DRAW_PERIOD_MS = 100

    def __init__(self, args: argparse.Namespace):
        super().__init__()
        self.title("LiDAR AGV viewer (Linux dev tool)")
        self.geometry("1100x760")
        self.client: PiClient | None = None
        self.latest: dict | None = None        # newest /api/scan result, written by the poll thread
        self.latest_error: str | None = None
        self.lock = threading.Lock()
        self.stop_event = threading.Event()
        self.view_range = 3.0                   # metres from the centre to the nearest edge (zoom)
        self.simulate = False
        self._build_ui(args)
        self.after(self.DRAW_PERIOD_MS, self._redraw)
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    # ------------------------------------------------------------------ layout
    def _build_ui(self, args: argparse.Namespace) -> None:
        bar = ttk.Frame(self, padding=4)
        bar.pack(side=tk.TOP, fill=tk.X)
        self.host = tk.StringVar(value=args.host)
        self.port = tk.StringVar(value=str(args.port))
        self.user = tk.StringVar(value=args.user)
        self.password = tk.StringVar(value=args.password)
        for label, var, width, show in (("Pi host / IP:", self.host, 16, ""), ("Port:", self.port, 6, ""),
                                        ("User:", self.user, 10, ""), ("Password:", self.password, 12, "*")):
            ttk.Label(bar, text=label).pack(side=tk.LEFT, padx=(6, 2))
            entry = ttk.Entry(bar, textvariable=var, width=width, show=show)
            entry.pack(side=tk.LEFT)
        self.connect_btn = ttk.Button(bar, text="Connect", command=self._toggle_connect)
        self.connect_btn.pack(side=tk.LEFT, padx=8)
        self.conn_label = tk.Label(bar, text="● Not connected", fg="gray")
        self.conn_label.pack(side=tk.LEFT)

        self.banner = tk.Frame(self, bg="dimgray")
        self.banner.pack(side=tk.TOP, fill=tk.X)
        self.reset_btn = tk.Button(self.banner, text="Reset E-Stop", font=("TkDefaultFont", 12, "bold"),
                                   state=tk.DISABLED, command=self._reset_estop)
        self.reset_btn.pack(side=tk.RIGHT, padx=8, pady=6, ipadx=10, ipady=8)
        self.zone_label = tk.Label(self.banner, text="NOT CONNECTED", bg="dimgray", fg="white",
                                   font=("TkDefaultFont", 22, "bold"), anchor="w")
        self.zone_label.pack(side=tk.TOP, fill=tk.X, padx=10)
        self.detail_label = tk.Label(self.banner, text="", bg="dimgray", fg="white", anchor="w")
        self.detail_label.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(0, 4))

        self.status = tk.Label(self, text="Ready.", anchor="w", relief=tk.SUNKEN)
        self.status.pack(side=tk.BOTTOM, fill=tk.X)

        side = ttk.Frame(self, padding=4)
        side.pack(side=tk.RIGHT, fill=tk.Y)
        ttk.Label(side, text="Objects (closest first)").pack(anchor="w")
        self.objects = tk.Listbox(side, width=38, font=("TkFixedFont", 10))
        self.objects.pack(fill=tk.BOTH, expand=True)
        self.sim_frame = ttk.LabelFrame(side, text="Simulator only", padding=4)
        self.sim_estop = tk.BooleanVar()
        self.sim_unplug = tk.BooleanVar()
        ttk.Checkbutton(self.sim_frame, text="E-stop button pressed", variable=self.sim_estop,
                        command=self._send_sim).pack(anchor="w")
        ttk.Checkbutton(self.sim_frame, text="LiDAR unplugged", variable=self.sim_unplug,
                        command=self._send_sim).pack(anchor="w")

        self.canvas = tk.Canvas(self, bg="#16181c", highlightthickness=0)
        self.canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        # Mouse wheel zoom: <MouseWheel> on Windows/macOS, Button-4/5 on X11 (Linux).
        self.canvas.bind("<MouseWheel>", lambda e: self._zoom(e.delta > 0))
        self.canvas.bind("<Button-4>", lambda e: self._zoom(True))
        self.canvas.bind("<Button-5>", lambda e: self._zoom(False))

    # ------------------------------------------------------------------ connection
    def _toggle_connect(self) -> None:
        if self.client is not None:
            self._disconnect("Disconnected.")
            return
        try:
            client = PiClient(self.host.get().strip(), int(self.port.get()), self.user.get().strip(), self.password.get())
            system = client.get("api/system")   # verifies the login once before polling
        except (ApiError, ValueError) as e:
            self._set_status(f"Connect failed: {e}", error=True)
            messagebox.showwarning("Cannot connect", str(e))
            return
        self.client = client
        self.simulate = bool(system.get("simulate"))
        if self.simulate:
            self.sim_frame.pack(fill=tk.X, pady=6)
        lidar = system.get("lidar", {})
        self._set_status(f"Connected to {system['system']['hostname']}: {lidar.get('model')} fw {lidar.get('firmware')}")
        self.conn_label.config(text=f"● Connected{' (SIMULATOR)' if self.simulate else ''}", fg="forestgreen")
        self.connect_btn.config(text="Disconnect")
        self.stop_event.clear()
        threading.Thread(target=self._poll_loop, args=(client,), daemon=True).start()

    def _disconnect(self, message: str) -> None:
        self.stop_event.set()
        self.client = None
        with self.lock:
            self.latest = None
        self.sim_frame.pack_forget()
        self.connect_btn.config(text="Connect")
        self.conn_label.config(text="● Not connected", fg="gray")
        self._set_banner("NOT CONNECTED", "", "dimgray", "white")
        self.reset_btn.config(state=tk.DISABLED)
        self._set_status(message)

    def _poll_loop(self, client: PiClient) -> None:
        """Background thread: fetch /api/scan ~10x per second; Tk widgets are only touched in _redraw."""
        while not self.stop_event.is_set():
            t0 = time.monotonic()
            try:
                scan = client.get("api/scan")
                with self.lock:
                    self.latest, self.latest_error = scan, None
            except ApiError as e:
                with self.lock:
                    self.latest, self.latest_error = None, str(e)
                if e.kind == "unauthorized":
                    break
            self.stop_event.wait(max(0.0, self.POLL_PERIOD_S - (time.monotonic() - t0)))

    # ------------------------------------------------------------------ commands
    def _reset_estop(self) -> None:
        if self.client is None:
            return
        try:
            self._set_status(self.client.post("api/estop/reset").get("message", "OK"))
        except ApiError as e:
            self._set_status(str(e), error=True)
            messagebox.showwarning("Reset E-Stop", str(e))

    def _send_sim(self) -> None:
        if self.client is None:
            return
        try:
            self.client.post("api/sim", {"estopPressed": self.sim_estop.get(), "lidarUnplugged": self.sim_unplug.get()})
        except ApiError as e:
            self._set_status(f"Simulation: {e}", error=True)

    def _zoom(self, zoom_in: bool) -> None:
        self.view_range = min(12.0, max(0.5, self.view_range * (1 / 1.15 if zoom_in else 1.15)))

    # ------------------------------------------------------------------ drawing
    def _redraw(self) -> None:
        with self.lock:
            scan, error = self.latest, self.latest_error
        if self.client is not None:
            if scan is not None:
                self._update_banner(scan["status"])
                self._update_objects(scan["objects"])
            elif error is not None:
                self._set_banner("NO DATA FROM PI", error, "dimgray", "white")
                self.reset_btn.config(state=tk.DISABLED)
                if "password" in error.lower() or "unauthorized" in error.lower():
                    self._disconnect("The Pi rejected the password.")
        self._draw_plot(scan)
        self.after(self.DRAW_PERIOD_MS, self._redraw)

    def _update_banner(self, s: dict) -> None:
        where = ""
        if s.get("closestM") is not None and s.get("bearingDeg") is not None:
            where = f"   {s['closestM']:.2f} m @ {round(s['bearingDeg']) % 360:03d}°"
        if s.get("shuttingDown"):
            title, back = "PI SHUTTING DOWN", "dimgray"
        elif s["eStopLatched"]:
            title, back = "E-STOP" + where, ZONE_COLORS["Stop"]
        elif s["lidarFault"]:
            title, back = "LIDAR FAULT", ZONE_COLORS["Stop"]
        else:
            title = {"Clear": "CLEAR", "Caution": "CAUTION - CLOSE"}.get(s["zone"], "STOP - TOO CLOSE") + where
            back = ZONE_COLORS.get(s["zone"], ZONE_COLORS["Stop"])
        details = (f"Motion: {'ENABLED' if s['motionEnabled'] else 'DISABLED'}   |   "
                   f"E-stop: {'LATCHED (' + str(s.get('eStopReason')) + ')' if s['eStopLatched'] else 'OK'}   |   "
                   f"LiDAR: {'FAULT (' + str(s.get('faultMessage')) + ')' if s['lidarFault'] else 'OK'}   |   "
                   f"LCD: {s.get('lcdLine1', '').strip()} / {s.get('lcdLine2', '').strip()}")
        if s.get("hardwareFaults"):
            details += "   |   HW: " + "; ".join(s["hardwareFaults"])
        fore = "black" if back == ZONE_COLORS["Caution"] else "white"
        self._set_banner(title, details, back, fore)
        self.reset_btn.config(state=tk.NORMAL if s["eStopLatched"] else tk.DISABLED)

    def _update_objects(self, objects: list) -> None:
        rows = [f"#{o['id']:<3} {o['distanceM']:5.2f} m  {round(o['bearingDeg']) % 360:3d}°  w {o['widthM']:4.2f} m"
                + ("" if o["inSector"] else "  (out)") for o in objects]
        if list(self.objects.get(0, tk.END)) != rows:
            self.objects.delete(0, tk.END)
            for i, (row, o) in enumerate(zip(rows, objects)):
                self.objects.insert(tk.END, row)
                self.objects.itemconfig(i, bg={"Clear": "#d6f0da", "Caution": "#fff1c2"}.get(o["zone"], "#ffd2d2"))

    def _draw_plot(self, scan: dict | None) -> None:
        """Top-down view: robot at the centre, forward UP, left on the LEFT (same as LidarPlotControl)."""
        c = self.canvas
        c.delete("all")
        w, h = max(c.winfo_width(), 50), max(c.winfo_height(), 50)
        cx, cy = w / 2, h / 2
        scale = (min(w, h) / 2 - 12) / self.view_range

        def to_screen(x: float, y: float) -> tuple[float, float]:
            return cx - y * scale, cy - x * scale

        spacing = 0.5
        while self.view_range / spacing > 8:
            spacing *= 2
        r = spacing
        while r * scale < math.hypot(w, h) / 2:
            c.create_oval(cx - r * scale, cy - r * scale, cx + r * scale, cy + r * scale, outline="#3c424a", dash=(1, 3))
            c.create_text(cx + r * scale * 0.707 + 4, cy - r * scale * 0.707 - 8, text=f"{r:g} m", fill="#828c96", anchor="w")
            r += spacing
        c.create_line(cx, 0, cx, h, fill="#464e58")
        c.create_line(0, cy, w, cy, fill="#464e58")
        c.create_polygon(cx, 4, cx - 7, 18, cx + 7, 18, fill="deepskyblue")
        c.create_text(cx + 10, 10, text="0° FWD", fill="deepskyblue", anchor="w")
        c.create_text(6, cy + 10, text="90° L", fill="#828c96", anchor="w")
        c.create_text(w - 6, cy + 10, text="270° R", fill="#828c96", anchor="e")

        stop = scan["stopDistanceM"] if scan else 0.35
        caution = scan["cautionDistanceM"] if scan else 0.80
        for radius, colour in ((caution, ZONE_COLORS["Caution"]), (stop, ZONE_COLORS["Stop"])):
            c.create_oval(cx - radius * scale, cy - radius * scale, cx + radius * scale, cy + radius * scale,
                          outline=colour, width=2, dash=(6, 4))
        s = max(8, 0.15 * scale)
        c.create_rectangle(cx - s, cy - s, cx + s, cy + s, outline="deepskyblue", width=2)

        if scan is None:
            c.create_text(cx, h - 14, text="Not connected" if self.client is None else "Waiting for data...", fill="gray")
            return
        for x, y in scan["points"]:
            d = math.hypot(x, y)
            colour = ZONE_COLORS["Stop"] if d < stop else ZONE_COLORS["Caution"] if d < caution else ZONE_COLORS["Clear"]
            px, py = to_screen(x, y)
            c.create_rectangle(px - 1.5, py - 1.5, px + 1.5, py + 1.5, fill=colour, outline="")
        st = scan["status"]
        if st.get("closestM") is not None and st.get("bearingDeg") is not None:
            a = math.radians(st["bearingDeg"])
            px, py = to_screen(st["closestM"] * math.cos(a), st["closestM"] * math.sin(a))
            colour = ZONE_COLORS.get(st["zone"], ZONE_COLORS["Stop"])
            c.create_line(cx, cy, px, py, fill=colour, dash=(4, 4))
            c.create_oval(px - 7, py - 7, px + 7, py + 7, outline=colour, width=2)
            c.create_text(px + 10, py - 12, text=f"{st['closestM']:.2f} m @ {round(st['bearingDeg']) % 360:03d}°",
                          fill="white", anchor="w")
        c.create_text(6, 26, anchor="nw", fill="silver",
                      text=f"{len(scan['points'])} points   {len(scan['objects'])} objects   {scan['scanRateHz']:.1f} Hz\n"
                           f"sector: {scan['sectorMode']}   view ±{self.view_range:.1f} m (mouse wheel = zoom)")

    # ------------------------------------------------------------------ helpers
    def _set_banner(self, title: str, details: str, back: str, fore: str) -> None:
        self.banner.config(bg=back)
        self.zone_label.config(text=title, bg=back, fg=fore)
        self.detail_label.config(text=details, bg=back, fg=fore)

    def _set_status(self, message: str, error: bool = False) -> None:
        self.status.config(text=f"{time.strftime('%H:%M:%S')}  {message}", fg="firebrick" if error else "black")

    def _on_close(self) -> None:
        self.stop_event.set()
        self.destroy()


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", default="192.168.1.100", help="Pi IP or host name (localhost for the simulator)")
    ap.add_argument("--port", type=int, default=5000)
    ap.add_argument("--user", default="admin")
    ap.add_argument("--password", default="", help="optional; you can also type it in the window")
    Viewer(ap.parse_args()).mainloop()


if __name__ == "__main__":
    main()
