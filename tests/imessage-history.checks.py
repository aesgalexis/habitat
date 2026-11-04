"""Synthetic-only checks for the optional offline metadata research probe."""
import hashlib
import contextlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "probe-imessage-history.py"
spec = importlib.util.spec_from_file_location("imessage_history", SCRIPT)
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


@contextlib.contextmanager
def open_database(path):
    with contextlib.closing(sqlite3.connect(path)) as db:
        with db:
            yield db


class HistoryChecks(unittest.TestCase):
    def setUp(self):
        base = ROOT / "artifacts" / "checks" / "imessage-history"
        base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=base)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.database = self.root / "sms.db"
        self.addresses = [f"+34600{i:06d}" for i in range(400)]
        with open_database(self.database) as db:
            db.executescript("""
                CREATE TABLE handle (id TEXT, service TEXT);
                CREATE TABLE message (handle_id INTEGER, service TEXT, date INTEGER,
                                      is_from_me INTEGER, text TEXT);
            """)
            for i, address in enumerate(self.addresses):
                db.execute("INSERT INTO handle VALUES (?, 'iMessage')", (address,))
                if i < 160:
                    db.execute("INSERT INTO message VALUES (?, 'iMessage', 800000000000000000, 0, 'PRIVATE BODY')", (i + 1,))
                elif i < 260:
                    db.execute("INSERT INTO message VALUES (?, 'SMS', 800000000000000000, 0, 'PRIVATE BODY')", (i + 1,))
                elif i < 300:
                    db.execute("INSERT INTO message VALUES (?, 'iMessage', 800000000000000000, 1, 'PRIVATE BODY')", (i + 1,))

    def test_bulk_400_read_only_no_bodies_or_false_negatives(self):
        before = hashlib.sha256(self.database.read_bytes()).digest()
        contacts = probe.probe(self.database, self.addresses)["contacts"]
        self.assertEqual(len(contacts), 400)
        self.assertEqual(sum(c["imessageEvidence"] == "observed-incoming" for c in contacts), 160)
        self.assertTrue(all(c["currentCapability"] == "unknown" for c in contacts))
        self.assertEqual(contacts[160]["incomingCounts"], {"SMS": 1})
        self.assertEqual(contacts[260]["incomingCounts"], {})  # Sent/group evidence ignored.
        self.assertEqual(contacts[300]["imessageEvidence"], "unknown")  # Handle label alone insufficient.
        self.assertNotIn("PRIVATE BODY", json.dumps(contacts))
        self.assertEqual(hashlib.sha256(self.database.read_bytes()).digest(), before)
        self.assertEqual({p.name for p in self.root.iterdir()}, {"sms.db"})

    def test_mixed_services_and_dates_keep_historical_evidence(self):
        with open_database(self.database) as db:
            db.execute("INSERT INTO message VALUES (1, 'SMS', 800000001000000000, 0, 'secret')")
        contact = probe.probe(self.database, self.addresses)["contacts"][0]
        self.assertEqual(contact["imessageEvidence"], "observed-incoming")
        self.assertEqual(contact["latestObservedService"], "SMS")
        self.assertEqual(contact["incomingCounts"], {"iMessage": 1, "SMS": 1})
        self.assertEqual(probe.apple_date(800000001), probe.apple_date(800000001000000000))
        self.assertIsNone(probe.apple_date(0))

    def test_exact_addresses_and_email(self):
        with open_database(self.database) as db:
            db.execute("INSERT INTO handle VALUES ('Someone@Example.com', NULL)")
            db.execute("INSERT INTO message VALUES (401, 'iMessage', 800000000, 0, 'secret')")
        contacts = probe.probe(self.database, ["SOMEONE@example.com", "0034 600 000 000", "600000000"])["contacts"]
        self.assertEqual(len(contacts), 2)
        self.assertTrue(all(c["imessageEvidence"] == "observed-incoming" for c in contacts))
        self.assertIsNone(probe.address_key("+34600000000 ext 5"))

    def test_pending_database_and_unsupported_schema_rejected(self):
        wal = Path(str(self.database) + "-wal")
        wal.write_bytes(b"pending")
        with self.assertRaises(ValueError):
            probe.probe(self.database, self.addresses)
        wal.unlink()
        with open_database(self.database) as db:
            db.execute("DROP TABLE message")
        with self.assertRaises(ValueError):
            probe.probe(self.database, self.addresses)

    def test_cli_does_not_overwrite(self):
        addresses = self.root / "addresses.json"
        addresses.write_text(json.dumps(self.addresses), encoding="utf-8")
        output = self.root / "report.json"
        command = [sys.executable, str(SCRIPT), "--database", str(self.database), "--addresses", str(addresses), "--output", str(output)]
        first = subprocess.run(command, capture_output=True)
        self.assertEqual(first.returncode, 0, first.stderr)
        before = output.read_bytes()
        self.assertNotEqual(subprocess.run(command, capture_output=True).returncode, 0)
        self.assertEqual(output.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
