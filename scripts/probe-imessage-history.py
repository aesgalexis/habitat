"""Offline research probe. Reads incoming service metadata; never sends or imports.

Input must be a closed, decrypted Messages SQLite database, not a live database.
Output describes historical observations, never current recipient availability.
"""

import argparse
import contextlib
import datetime as dt
import json
from pathlib import Path
import re
import sqlite3
import sys


def address_key(value):
    if not isinstance(value, str):
        return None
    value = value.strip()
    if re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", value):
        return value.casefold()
    phone = re.sub(r"[ ().-]", "", value)
    if phone.startswith("00"):
        phone = "+" + phone[2:]
    # Do not guess the country or join a local number to a different person.
    return phone if re.fullmatch(r"\+[1-9][0-9]{7,14}", phone) else None


def apple_date(value):
    if not isinstance(value, int) or value <= 0:
        return None
    # Apple stores seconds since 2001; modern databases store nanoseconds.
    seconds = value / 1_000_000_000 if value >= 1_000_000_000_000 else value
    try:
        return (dt.datetime(2001, 1, 1, tzinfo=dt.timezone.utc)
                + dt.timedelta(seconds=seconds)).isoformat()
    except (OverflowError, ValueError):
        return None


def probe(database, addresses):
    path = Path(database).resolve(strict=True)
    if not path.is_file():
        raise ValueError("Select an offline Messages SQLite database.")
    for suffix in ("-wal", "-journal"):
        sidecar = Path(str(path) + suffix)
        if sidecar.exists() and sidecar.stat().st_size:
            raise ValueError("Database has pending changes. Use a consistent offline export.")

    results = {}
    for value in addresses:
        key = address_key(value)
        if key:
            results[key] = {
                "address": key, "currentCapability": "unknown",
                "imessageEvidence": "unknown", "incomingCounts": {},
                "latestObservedService": None, "lastIncomingAt": None,
            }
    # Immutable prevents journal/locking writes. Safe only for this offline input.
    with contextlib.closing(sqlite3.connect(path.as_uri() + "?mode=ro&immutable=1", uri=True)) as db:
        db.execute("PRAGMA query_only = ON")
        required = {"handle": {"id"}, "message": {"handle_id", "service", "date", "is_from_me"}}
        for table, columns in required.items():
            actual = {row[1] for row in db.execute(f'PRAGMA table_info("{table}")')}
            if not columns <= actual:
                raise ValueError("Unsupported Messages schema: required metadata is missing.")
        # handle_id identifies the sender for received messages. Do not infer
        # outgoing/group participants from it, or use handle.service as proof.
        rows = db.execute("""
            SELECT h.id, m.service, m.date FROM message AS m
            JOIN handle AS h ON h.ROWID = m.handle_id
            WHERE m.is_from_me = 0
        """)
        for raw_address, raw_service, raw_date in rows:
            record = results.get(address_key(raw_address))
            if record is None:
                continue
            service = {"imessage": "iMessage", "sms": "SMS", "mms": "MMS", "rcs": "RCS"}.get(
                str(raw_service).strip().casefold(), "unknown")
            counts = record["incomingCounts"]
            counts[service] = counts.get(service, 0) + 1
            if service == "iMessage":
                record["imessageEvidence"] = "observed-incoming"
            date = apple_date(raw_date)
            if date and (record["lastIncomingAt"] is None or date > record["lastIncomingAt"]):
                record["lastIncomingAt"] = date
                record["latestObservedService"] = service
            elif date and date == record["lastIncomingAt"] and record["latestObservedService"] != service:
                record["latestObservedService"] = "ambiguous"
    return {
        "schemaVersion": 1, "source": "offline-messages-metadata",
        "historicalOnly": True,
        "contacts": [results[key] for key in sorted(results)],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--addresses", type=Path, required=True,
                        help="JSON array of selected phone numbers/email addresses.")
    parser.add_argument("--output", type=Path, required=True,
                        help="New report file; existing files are never overwritten.")
    args = parser.parse_args()
    try:
        addresses = json.loads(args.addresses.read_text(encoding="utf-8-sig"))
        if not isinstance(addresses, list) or any(not isinstance(value, str) for value in addresses):
            raise ValueError("Addresses must be a JSON array of strings.")
        report = probe(args.database, addresses)
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(report, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        print(f"Historical metadata report saved: {len(report['contacts'])} addresses.")
        return 0
    except (OSError, ValueError, sqlite3.Error):
        # Avoid emitting private addresses, database contents or paths in errors.
        print("Cannot create report. Check input format, offline database schema and unused output path.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
