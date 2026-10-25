#!/usr/bin/env python3
"""Tests for scripts/package-release.

Run with: python3 -m unittest discover -s scripts/tests
"""
from __future__ import annotations

import importlib.machinery
import importlib.util
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

_loader = importlib.machinery.SourceFileLoader("package_release", str(ROOT / "scripts/package-release"))
_spec = importlib.util.spec_from_loader("package_release", _loader)
package_release = importlib.util.module_from_spec(_spec)
_loader.exec_module(package_release)


class LeftOut(unittest.TestCase):
    def test_the_live_config_ships(self) -> None:
        self.assertFalse(package_release.is_junk("lms-tournament.yaml"))

    def test_config_backups_do_not(self) -> None:
        for name in ("lms-tournament.yaml.bak", "lms-tournament.yaml.bak-old"):
            self.assertTrue(package_release.is_junk(name), name)

    def test_in_game_survey_output_does_not(self) -> None:
        self.assertTrue(package_release.is_junk("lms-biome-sightings.tsv"))

    def test_the_cell_timing_switch_does_not(self) -> None:
        self.assertTrue(package_release.is_junk("lms-cell-timing"))

    def test_the_mod_itself_does(self) -> None:
        self.assertFalse(package_release.is_junk("Lms.Game.dll"))


class Stamp(unittest.TestCase):
    def test_names_the_version_the_commit_and_where_the_source_is(self) -> None:
        with tempfile.TemporaryDirectory() as made:
            stage = Path(made)
            package_release.stamp(stage, "v1.2.3", "abc123def456")
            text = (stage / "BUILD.txt").read_text(encoding="utf-8")

        self.assertIn("v1.2.3", text)
        self.assertIn("abc123def456", text)
        self.assertIn(f"{package_release.REPOSITORY}/tree/abc123def456", text)


if __name__ == "__main__":
    unittest.main()
