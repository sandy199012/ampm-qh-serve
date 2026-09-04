"""
QuickHeal Endpoint Monitor - Flask Server v3.0
AMPM Fashions Pvt. Ltd. - IT Department
Render.com Deployment Version
"""

from flask import Flask, request, jsonify
from flask_cors import CORS
import datetime, sqlite3, os

app = Flask(__name__)
CORS(app)

# ── Render pe /tmp use karo (persistent disk nahi hai free tier mein)
# ── Agar Render paid hai toh /var/data use kar sakte ho
DB_PATH = os.environ.get("DB_PATH", "/tmp/endpoints.db")

# ─── DB ───────────────────────────────────────────────────────────────────────
def get_db():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn

def init_db():
    conn = get_db()
    conn.execute("""CREATE TABLE IF NOT EXISTS endpoints (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        hostname TEXT NOT NULL UNIQUE,
        ip_address TEXT, mac_address TEXT, os_name TEXT, os_version TEXT,
        os_build TEXT, os_arch TEXT, cpu TEXT, ram_gb REAL,
        disk_total_gb REAL, disk_free_gb REAL, logged_user TEXT, domain TEXT,
        qh_installed INTEGER DEFAULT 0, qh_version TEXT, qh_service_status TEXT,
        qh_service_name TEXT, qh_last_update TEXT, qh_license_key TEXT,
        qh_product_name TEXT, qh_def_date TEXT, qh_threats_found TEXT,
        qh_last_scan TEXT, qh_install_path TEXT, qh_install_date TEXT,
        last_seen TEXT, first_seen TEXT, agent_version TEXT, notes TEXT
    )""")
    conn.execute("""CREATE TABLE IF NOT EXISTS qh_licenses (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        license_key TEXT NOT NULL UNIQUE,
        product_name TEXT DEFAULT 'Quick Heal Total Security',
        expiry_date TEXT DEFAULT '',
        purchase_date TEXT DEFAULT '',
        assigned_to_hostname TEXT DEFAULT '',
        assigned_date TEXT DEFAULT '',
        status TEXT DEFAULT 'Available',
        notes TEXT DEFAULT ''
    )""")
    conn.commit()
    conn.close()

# ─── HELPERS ──────────────────────────────────────────────────────────────────
def now():
    return datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")

def sync_license_to_endpoint(conn, hostname, license_key):
    if hostname and license_key:
        conn.execute("UPDATE endpoints SET qh_license_key=? WHERE hostname=?",
                     (license_key, hostname.upper()))

def sync_all_licenses(conn):
    mapped = 0
    lics = conn.execute(
        "SELECT license_key, assigned_to_hostname FROM qh_licenses "
        "WHERE assigned_to_hostname IS NOT NULL AND assigned_to_hostname != ''"
    ).fetchall()
    for lic in lics:
        ep = conn.execute(
            "SELECT qh_license_key FROM endpoints WHERE hostname=?",
            (lic["assigned_to_hostname"],)
        ).fetchone()
        if ep is not None:
            conn.execute("UPDATE endpoints SET qh_license_key=? WHERE hostname=?",
                         (lic["license_key"], lic["assigned_to_hostname"]))
            mapped += 1

    eps = conn.execute(
        "SELECT hostname, qh_license_key FROM endpoints "
        "WHERE qh_license_key IS NOT NULL AND qh_license_key != ''"
    ).fetchall()
    for ep in eps:
        row = conn.execute(
            "SELECT id, assigned_to_hostname FROM qh_licenses WHERE license_key=?",
            (ep["qh_license_key"],)
        ).fetchone()
        if row and (not row["assigned_to_hostname"] or row["assigned_to_hostname"] == ep["hostname"]):
            conn.execute(
                "UPDATE qh_licenses SET assigned_to_hostname=?, assigned_date=?, status='Assigned' "
                "WHERE license_key=?",
                (ep["hostname"], now(), ep["qh_license_key"])
            )
            mapped += 1
    return mapped

# ─── ROUTES ───────────────────────────────────────────────────────────────────
@app.route("/", methods=["GET"])
def index():
    return jsonify({
        "status": "QuickHeal Monitor Server Running",
        "version": "3.0",
        "company": "AMPM Fashions Pvt. Ltd.",
        "server": "Render.com"
    })

@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok"})

@app.route("/api/report", methods=["POST"])
def receive_report():
    data = request.get_json()
    if not data or "hostname" not in data:
        return jsonify({"error": "Invalid data"}), 400

    hostname = data.get("hostname", "").upper().strip()
    mac      = (data.get("mac_address") or "").upper().strip()
    ts       = now()
    conn     = get_db()

    existing = None
    if mac and mac != "UNKNOWN":
        existing = conn.execute(
            "SELECT id, first_seen, hostname FROM endpoints WHERE mac_address=?", (mac,)
        ).fetchone()
        if existing and existing["hostname"] != hostname:
            conn.execute("UPDATE endpoints SET hostname=? WHERE mac_address=?", (hostname, mac))

    if not existing:
        existing = conn.execute(
            "SELECT id, first_seen, hostname FROM endpoints WHERE hostname=?", (hostname,)
        ).fetchone()

    fields = {
        "hostname": hostname, "ip_address": data.get("ip_address"),
        "mac_address": mac or data.get("mac_address"),
        "os_name": data.get("os_name"), "os_version": data.get("os_version"),
        "os_build": data.get("os_build"), "os_arch": data.get("os_arch"),
        "cpu": data.get("cpu"), "ram_gb": data.get("ram_gb"),
        "disk_total_gb": data.get("disk_total_gb"), "disk_free_gb": data.get("disk_free_gb"),
        "logged_user": data.get("logged_user"), "domain": data.get("domain"),
        "qh_installed": 1 if data.get("qh_installed") else 0,
        "qh_version": data.get("qh_version"), "qh_service_status": data.get("qh_service_status"),
        "qh_service_name": data.get("qh_service_name"),
        "qh_last_update": data.get("qh_last_update"),
        "qh_product_name": data.get("qh_product_name"),
        "qh_def_date": data.get("qh_def_date"),
        "qh_threats_found": data.get("qh_threats_found"),
        "qh_last_scan": data.get("qh_last_scan"),
        "qh_install_path": data.get("qh_install_path"),
        "qh_install_date": data.get("qh_install_date"),
        "agent_version": data.get("agent_version", "3.0"),
        "last_seen": ts,
    }

    agent_key = (data.get("qh_license_key") or "").strip()
    if agent_key:
        fields["qh_license_key"] = agent_key
    elif existing:
        old_key = conn.execute(
            "SELECT qh_license_key FROM endpoints WHERE hostname=?", (hostname,)
        ).fetchone()
        fields["qh_license_key"] = old_key["qh_license_key"] if old_key else ""
    else:
        fields["qh_license_key"] = ""

    if existing:
        fields["first_seen"] = existing["first_seen"]
        set_clause = ", ".join(f"{k}=?" for k in fields)
        if mac and mac != "UNKNOWN":
            conn.execute(f"UPDATE endpoints SET {set_clause} WHERE mac_address=?",
                         list(fields.values()) + [mac])
        else:
            conn.execute(f"UPDATE endpoints SET {set_clause} WHERE hostname=?",
                         list(fields.values()) + [hostname])
    else:
        fields["first_seen"] = ts
        cols = ", ".join(fields.keys())
        vals = ", ".join("?" for _ in fields)
        conn.execute(f"INSERT INTO endpoints ({cols}) VALUES ({vals})", list(fields.values()))

    if agent_key:
        row = conn.execute(
            "SELECT id, assigned_to_hostname FROM qh_licenses WHERE license_key=?", (agent_key,)
        ).fetchone()
        if row and (not row["assigned_to_hostname"] or row["assigned_to_hostname"] == hostname):
            conn.execute(
                "UPDATE qh_licenses SET assigned_to_hostname=?, assigned_date=?, status='Assigned' "
                "WHERE license_key=?", (hostname, ts, agent_key)
            )

    conn.commit()
    conn.close()
    return jsonify({"status": "ok", "hostname": hostname})

@app.route("/api/endpoints", methods=["GET"])
def get_endpoints():
    conn = get_db()
    rows = conn.execute("SELECT * FROM endpoints ORDER BY hostname").fetchall()
    conn.close()
    return jsonify([dict(r) for r in rows])

@app.route("/api/endpoints/<hostname>", methods=["GET"])
def get_endpoint(hostname):
    conn = get_db()
    row = conn.execute("SELECT * FROM endpoints WHERE hostname=?", (hostname.upper(),)).fetchone()
    conn.close()
    return jsonify(dict(row)) if row else (jsonify({"error": "Not found"}), 404)

@app.route("/api/endpoints/<hostname>/notes", methods=["POST"])
def update_notes(hostname):
    note = (request.get_json() or {}).get("notes", "")
    conn = get_db()
    conn.execute("UPDATE endpoints SET notes=? WHERE hostname=?", (note, hostname.upper()))
    conn.commit(); conn.close()
    return jsonify({"status": "ok"})

@app.route("/api/delete/<hostname>", methods=["DELETE"])
def delete_endpoint(hostname):
    conn = get_db()
    conn.execute("DELETE FROM endpoints WHERE hostname=?", (hostname.upper(),))
    conn.commit(); conn.close()
    return jsonify({"status": "deleted"})

@app.route("/api/summary", methods=["GET"])
def get_summary():
    conn   = get_db()
    total  = conn.execute("SELECT COUNT(*) FROM endpoints").fetchone()[0]
    qh_ok  = conn.execute("SELECT COUNT(*) FROM endpoints WHERE qh_installed=1").fetchone()[0]
    cutoff = (datetime.datetime.now() - datetime.timedelta(hours=24)).strftime("%Y-%m-%d %H:%M:%S")
    online = conn.execute("SELECT COUNT(*) FROM endpoints WHERE last_seen >= ?", (cutoff,)).fetchone()[0]
    svc_run= conn.execute("SELECT COUNT(*) FROM endpoints WHERE qh_service_status='Running'").fetchone()[0]
    lt     = conn.execute("SELECT COUNT(*) FROM qh_licenses").fetchone()[0]
    la     = conn.execute("SELECT COUNT(*) FROM qh_licenses WHERE status='Assigned'").fetchone()[0]
    le     = conn.execute("SELECT COUNT(*) FROM qh_licenses WHERE status='Expired'").fetchone()[0]
    conn.close()
    return jsonify({
        "total_endpoints": total, "qh_installed": qh_ok, "qh_missing": total - qh_ok,
        "online_24h": online, "svc_running": svc_run,
        "total_licenses": lt, "licenses_used": la,
        "licenses_free": lt - la - le, "licenses_expired": le,
    })

@app.route("/api/licenses", methods=["GET"])
def get_licenses():
    conn = get_db()
    rows = conn.execute("SELECT * FROM qh_licenses ORDER BY id").fetchall()
    result = []
    for r in rows:
        d = dict(r)
        if d.get("assigned_to_hostname"):
            ep = conn.execute(
                "SELECT ip_address, logged_user, os_name, last_seen FROM endpoints WHERE hostname=?",
                (d["assigned_to_hostname"],)
            ).fetchone()
            if ep:
                d["ep_ip"]       = ep["ip_address"]
                d["ep_user"]     = ep["logged_user"]
                d["ep_os"]       = ep["os_name"]
                d["ep_lastseen"] = ep["last_seen"]
        result.append(d)
    conn.close()
    return jsonify(result)

@app.route("/api/licenses", methods=["POST"])
def add_license():
    data     = request.get_json() or {}
    key      = (data.get("license_key") or "").strip()
    hostname = (data.get("assigned_to_hostname") or "").upper().strip()
    if not key:
        return jsonify({"error": "license_key required"}), 400
    conn = get_db()
    try:
        status = "Assigned" if hostname else data.get("status", "Available")
        conn.execute("""INSERT INTO qh_licenses
            (license_key, product_name, expiry_date, purchase_date,
             assigned_to_hostname, assigned_date, status, notes)
            VALUES (?,?,?,?,?,?,?,?)""",
            (key, data.get("product_name", "Quick Heal Total Security"),
             data.get("expiry_date", ""), data.get("purchase_date", ""),
             hostname, now() if hostname else "", status, data.get("notes", "")))
        conn.commit()
        if hostname:
            sync_license_to_endpoint(conn, hostname, key)
            conn.commit()
        conn.close()
        return jsonify({"status": "added", "license_key": key})
    except Exception as e:
        conn.close()
        return jsonify({"error": str(e)}), 409

@app.route("/api/licenses/bulk", methods=["POST"])
def bulk_add_licenses():
    data  = request.get_json() or {}
    keys  = data.get("keys", [])
    added = skipped = 0
    conn  = get_db()
    for k in keys:
        k = str(k).strip()
        if not k: continue
        try:
            conn.execute("""INSERT INTO qh_licenses
                (license_key, product_name, expiry_date, purchase_date, status, notes)
                VALUES (?,?,?,?,'Available','')""",
                (k, data.get("product_name","Quick Heal Total Security"),
                 data.get("expiry_date",""), data.get("purchase_date","")))
            added += 1
        except: skipped += 1
    conn.commit(); conn.close()
    return jsonify({"status": "ok", "added": added, "skipped_duplicates": skipped})

@app.route("/api/licenses/<int:lid>", methods=["PUT"])
def update_license(lid):
    data = request.get_json() or {}
    conn = get_db()
    row  = conn.execute("SELECT * FROM qh_licenses WHERE id=?", (lid,)).fetchone()
    if not row:
        conn.close()
        return jsonify({"error": "Not found"}), 404
    new_host = (data.get("assigned_to_hostname") or "").upper().strip()
    old_host = (row["assigned_to_hostname"] or "").upper().strip()
    new_key  = data.get("license_key", row["license_key"])
    status   = "Assigned" if new_host else "Available"
    conn.execute("""UPDATE qh_licenses SET
        license_key=?, product_name=?, expiry_date=?, purchase_date=?,
        assigned_to_hostname=?, assigned_date=?, status=?, notes=?
        WHERE id=?""",
        (new_key, data.get("product_name", row["product_name"]),
         data.get("expiry_date", row["expiry_date"]),
         data.get("purchase_date", row["purchase_date"]),
         new_host, now() if new_host else "",
         status, data.get("notes", row["notes"]), lid))
    if new_host:
        sync_license_to_endpoint(conn, new_host, new_key)
    if old_host and old_host != new_host:
        conn.execute("UPDATE endpoints SET qh_license_key='' WHERE hostname=? AND qh_license_key=?",
                     (old_host, row["license_key"]))
    conn.commit(); conn.close()
    return jsonify({"status": "updated"})

@app.route("/api/licenses/<int:lid>", methods=["DELETE"])
def delete_license(lid):
    conn = get_db()
    conn.execute("DELETE FROM qh_licenses WHERE id=?", (lid,))
    conn.commit(); conn.close()
    return jsonify({"status": "deleted"})

@app.route("/api/licenses/assign", methods=["POST"])
def assign_license():
    data     = request.get_json() or {}
    lid      = data.get("id")
    hostname = (data.get("hostname") or "").upper().strip()
    if not lid or not hostname:
        return jsonify({"error": "id and hostname required"}), 400
    conn = get_db()
    row  = conn.execute("SELECT license_key FROM qh_licenses WHERE id=?", (lid,)).fetchone()
    if not row:
        conn.close()
        return jsonify({"error": "License not found"}), 404
    conn.execute("""UPDATE qh_licenses SET
        assigned_to_hostname=?, assigned_date=?, status='Assigned' WHERE id=?""",
        (hostname, now(), lid))
    sync_license_to_endpoint(conn, hostname, row["license_key"])
    conn.commit(); conn.close()
    return jsonify({"status": "assigned", "hostname": hostname})

@app.route("/api/licenses/unassign/<int:lid>", methods=["POST"])
def unassign_license(lid):
    conn = get_db()
    row  = conn.execute("SELECT license_key, assigned_to_hostname FROM qh_licenses WHERE id=?", (lid,)).fetchone()
    if row and row["assigned_to_hostname"]:
        conn.execute("UPDATE endpoints SET qh_license_key='' WHERE hostname=? AND qh_license_key=?",
                     (row["assigned_to_hostname"], row["license_key"]))
    conn.execute("UPDATE qh_licenses SET assigned_to_hostname='', assigned_date='', status='Available' WHERE id=?", (lid,))
    conn.commit(); conn.close()
    return jsonify({"status": "unassigned"})

@app.route("/api/licenses/automap", methods=["POST"])
def automap_all():
    conn   = get_db()
    mapped = sync_all_licenses(conn)
    conn.commit(); conn.close()
    return jsonify({"status": "ok", "mapped": mapped})

if __name__ == "__main__":
    init_db()
    port = int(os.environ.get("PORT", 8080))
    print("=" * 55)
    print("  QuickHeal Monitor Server v3.0 - AMPM Fashions IT")
    print(f"  URL: http://0.0.0.0:{port}")
    print("=" * 55)
    app.run(host="0.0.0.0", port=port, debug=False)
