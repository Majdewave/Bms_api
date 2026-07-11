import json
import pathlib
import re
import sys
from typing import Dict, Tuple

import psycopg2

ROOT = pathlib.Path(__file__).resolve().parents[1]
APPSETTINGS = ROOT / "appsettings.json"
ARTIFACT = ROOT / "scripts" / "appointment_department_baseline.json"


def read_connection_string() -> str:
    text = APPSETTINGS.read_text(encoding="utf-8")
    match = re.search(r'"DefaultConnection"\s*:\s*"([^"]+)"', text)
    if not match:
        raise RuntimeError("DefaultConnection not found in appsettings.json")
    return match.group(1)


def to_psycopg_dsn(connection_string: str) -> str:
    parts = [segment.strip() for segment in connection_string.split(";") if segment.strip()]
    kv = {}
    for part in parts:
        if "=" not in part:
            continue
        key, value = part.split("=", 1)
        kv[key.strip().lower()] = value.strip()

    mapping = {
        "host": "host",
        "port": "port",
        "database": "dbname",
        "username": "user",
        "password": "password",
        "ssl mode": "sslmode",
    }

    dsn_parts = []
    for source_key, target_key in mapping.items():
        value = kv.get(source_key)
        if value:
            if target_key == "sslmode":
                value = value.lower()
            dsn_parts.append(f"{target_key}={value}")

    return " ".join(dsn_parts)


def fetch_stats(conn) -> Dict[str, int]:
    with conn.cursor() as cur:
        cur.execute(
            '''
            SELECT COUNT(*)
            FROM "Appointments" a
            WHERE a."DepartmentId" IS NULL
            '''
        )
        null_count = cur.fetchone()[0]

        cur.execute(
            '''
            SELECT COUNT(*)
            FROM "Appointments" a
            WHERE a."DepartmentId" IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM "Departments" d
                  WHERE d."Id" = a."DepartmentId"
                    AND d."TenantId" = a."TenantId"
              )
            '''
        )
        invalid_count = cur.fetchone()[0]

        cur.execute(
            '''
            SELECT COUNT(*)
            FROM "Appointments" a
            JOIN "Services" s
              ON s."Id" = a."ServiceId"
             AND s."TenantId" = a."TenantId"
            WHERE s."DepartmentId" IS NOT NULL
              AND (
                  a."DepartmentId" IS NULL
                  OR NOT EXISTS (
                      SELECT 1
                      FROM "Departments" d
                      WHERE d."Id" = a."DepartmentId"
                        AND d."TenantId" = a."TenantId"
                  )
              )
            '''
        )
        repairable_count = cur.fetchone()[0]

    return {
        "null_department_id": int(null_count),
        "invalid_department_id": int(invalid_count),
        "repairable": int(repairable_count),
    }


def fetch_valid_snapshot(conn) -> Dict[str, str]:
    with conn.cursor() as cur:
        cur.execute(
            '''
            SELECT a."Id", a."DepartmentId"
            FROM "Appointments" a
            WHERE a."DepartmentId" IS NOT NULL
              AND EXISTS (
                  SELECT 1
                  FROM "Departments" d
                  WHERE d."Id" = a."DepartmentId"
                    AND d."TenantId" = a."TenantId"
              )
            '''
        )
        rows = cur.fetchall()

    return {str(appointment_id): str(department_id) for appointment_id, department_id in rows}


def fetch_repaired_count(conn, baseline_valid: Dict[str, str]) -> int:
    with conn.cursor() as cur:
        cur.execute(
            '''
            SELECT a."Id", a."DepartmentId"
            FROM "Appointments" a
            WHERE a."DepartmentId" IS NOT NULL
            '''
        )
        rows = cur.fetchall()

    current = {str(appointment_id): str(department_id) for appointment_id, department_id in rows}
    # Repaired appointments are rows that were invalid/null before and now have a department set.
    # This is derived by total valid rows growth plus any corrected invalid rows that remained non-null.
    # We report repairs as rows that were in the pre "repairable" set and are now outside that set.
    # The caller computes this from baseline stats; this helper is not used for that metric.
    return len(current)


def compare_valid_unchanged(conn, baseline_valid: Dict[str, str]) -> Tuple[int, int]:
    changed = 0
    missing = 0

    with conn.cursor() as cur:
        cur.execute(
            '''
            SELECT a."Id", a."DepartmentId"
            FROM "Appointments" a
            ''',
        )
        current_rows = cur.fetchall()

    current_map = {str(appointment_id): (str(department_id) if department_id is not None else None) for appointment_id, department_id in current_rows}

    for appointment_id, expected_department_id in baseline_valid.items():
        if appointment_id not in current_map:
            missing += 1
            continue
        if current_map[appointment_id] != expected_department_id:
            changed += 1

    return changed, missing


def cmd_pre(conn):
    stats = fetch_stats(conn)
    baseline_valid = fetch_valid_snapshot(conn)

    ARTIFACT.write_text(
        json.dumps(
            {
                "stats": stats,
                "valid_snapshot": baseline_valid,
            },
            indent=2,
        ),
        encoding="utf-8",
    )

    print("PRE_NULL_DEPARTMENT_ID=", stats["null_department_id"], sep="")
    print("PRE_INVALID_DEPARTMENT_ID=", stats["invalid_department_id"], sep="")
    print("PRE_REPAIRABLE=", stats["repairable"], sep="")
    print("BASELINE_VALID_APPOINTMENTS=", len(baseline_valid), sep="")


def cmd_post(conn):
    if not ARTIFACT.exists():
        raise RuntimeError("Baseline artifact not found. Run with 'pre' first.")

    baseline = json.loads(ARTIFACT.read_text(encoding="utf-8"))
    pre_stats = baseline["stats"]
    baseline_valid = baseline["valid_snapshot"]

    post_stats = fetch_stats(conn)

    repaired = int(pre_stats["repairable"]) - int(post_stats["repairable"])
    if repaired < 0:
        repaired = 0

    valid_changed, baseline_missing = compare_valid_unchanged(conn, baseline_valid)

    print("POST_NULL_DEPARTMENT_ID=", post_stats["null_department_id"], sep="")
    print("POST_INVALID_DEPARTMENT_ID=", post_stats["invalid_department_id"], sep="")
    print("POST_REPAIRABLE=", post_stats["repairable"], sep="")
    print("REPAIRED_APPOINTMENTS=", repaired, sep="")
    print("VALID_APPOINTMENTS_CHANGED=", valid_changed, sep="")
    print("BASELINE_VALID_APPOINTMENTS_MISSING=", baseline_missing, sep="")


def main() -> int:
    if len(sys.argv) != 2 or sys.argv[1] not in {"pre", "post"}:
        print("Usage: python verify_department_snapshot_migration.py [pre|post]")
        return 1

    conn_str = to_psycopg_dsn(read_connection_string())

    with psycopg2.connect(conn_str) as conn:
        if sys.argv[1] == "pre":
            cmd_pre(conn)
        else:
            cmd_post(conn)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
