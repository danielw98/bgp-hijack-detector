"""Runs a test module's test_* functions under plain python; pytest does not use it."""

from __future__ import annotations

import sys


def run(namespace: dict) -> None:
    tests = [v for k, v in sorted(namespace.items()) if k.startswith("test_") and callable(v)]
    failed = 0
    for t in tests:
        try:
            t()
            print(f"PASS {t.__name__}")
        except AssertionError as exc:
            failed += 1
            print(f"FAIL {t.__name__}: {exc}")
    print(f"\n{len(tests) - failed}/{len(tests)} passed")
    sys.exit(1 if failed else 0)
