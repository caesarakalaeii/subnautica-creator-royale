#!/usr/bin/env python3
"""Tests for the cell-timing marker that ./scripts/install-mod drops.

usage: python3 -m unittest discover -s scripts/tests
"""
from __future__ import annotations

import contextlib
import importlib.machinery
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

# install-mod has no .py suffix, so it is loaded by path.
_loader = importlib.machinery.SourceFileLoader("install_mod", str(ROOT / "scripts/install-mod"))
_spec = importlib.util.spec_from_loader("install_mod", _loader)
install_mod = importlib.util.module_from_spec(_spec)
_loader.exec_module(install_mod)


class Quietly(unittest.TestCase):
    """Swallows the script's progress output."""

    def setUp(self) -> None:
        said = contextlib.redirect_stdout(io.StringIO())
        said.__enter__()
        self.addCleanup(said.__exit__, None, None, None)


class TheMarkerThatTurnsCellTimingOn(Quietly):
    def setUp(self) -> None:
        super().setUp()
        # Stands in for the launcher's lib/net472, where NitroxClient is loaded from.
        self.client_lib = Path(tempfile.mkdtemp())

    def test_it_is_written_where_the_client_will_find_it(self) -> None:
        install_mod.drop_marker(self.client_lib)

        self.assertTrue((self.client_lib / "lms-cell-timing").is_file())

    def test_installing_twice_leaves_one_file_and_no_error(self) -> None:
        install_mod.drop_marker(self.client_lib)
        install_mod.drop_marker(self.client_lib)

        self.assertEqual([path.name for path in self.client_lib.iterdir()], ["lms-cell-timing"])

    def test_a_build_without_it_is_a_working_mod_with_the_instrument_off(self) -> None:
        names = [name for _, files in install_mod.REQUIRED for name in files]

        self.assertNotIn("lms-cell-timing", names)


if __name__ == "__main__":
    unittest.main()