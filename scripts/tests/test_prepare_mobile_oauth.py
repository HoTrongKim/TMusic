"""Build configuration tests; all client IDs here are public test fixtures."""

import json
import os
from pathlib import Path
import plistlib
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "prepare_mobile_oauth.py"
SERVER_ID = "123456789-webfixture.apps.googleusercontent.com"
IOS_ID = "123456789-iosfixture.apps.googleusercontent.com"
ANDROID_ID = "123456789-androidfixture.apps.googleusercontent.com"


class MobileOAuthConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.output = self.root / "dart-defines.json"
        self.plist = self.root / "Info.plist"
        self.original = {
            "CFBundleIdentifier": "com.tmusic.tmusic",
            "UIBackgroundModes": ["audio"],
            "CFBundleURLTypes": [{"CFBundleURLName": "existing",
                                  "CFBundleURLSchemes": ["tmusic-share"]}],
        }
        self.plist.write_bytes(plistlib.dumps(self.original))
        self.environment = {
            key: value for key, value in os.environ.items()
            if not (key.startswith("TMUSIC_") or key.lower().startswith("auth"))
        }
        self.environment.update({
            "TMUSIC_BUILD_API_URL": "https://api.example.com",
            "TMUSIC_GOOGLE_SERVER_CLIENT_ID": SERVER_ID,
            "TMUSIC_GOOGLE_IOS_CLIENT_ID": IOS_ID,
        })

    def run_helper(self, platform, info_plist=True):
        arguments = [sys.executable, str(SCRIPT), "--platform", platform,
                     "--output", str(self.output)]
        if info_plist and platform == "ios":
            arguments += ["--info-plist", str(self.plist)]
        return subprocess.run(arguments, env=self.environment,
                              capture_output=True, text=True, check=False)

    def test_android_emits_only_public_configuration_and_does_not_touch_plist(self):
        self.environment["TMUSIC_GOOGLE_ANDROID_CLIENT_ID"] = ANDROID_ID
        self.environment["TMUSIC_GOOGLE_CLIENT_SECRET"] = "never-bundle-this-fixture"
        original_bytes = self.plist.read_bytes()
        result = self.run_helper("android")
        self.assertEqual(result.returncode, 0, "Android public configuration was not generated")
        self.assertEqual(json.loads(self.output.read_text()), {
            "TMUSIC_API_URL": "https://api.example.com",
            "TMUSIC_GOOGLE_SERVER_CLIENT_ID": SERVER_ID,
            "TMUSIC_GOOGLE_ANDROID_CLIENT_ID": ANDROID_ID,
        })
        self.assertEqual(self.plist.read_bytes(), original_bytes)
        self.assertNotIn("never-bundle", self.output.read_text())
        self.assertNotIn(SERVER_ID, result.stdout + result.stderr)

    def test_ios_installs_callback_scheme_preserving_unrelated_settings(self):
        result = self.run_helper("ios")
        self.assertEqual(result.returncode, 0, "iOS callback configuration was not generated")
        configured = plistlib.loads(self.plist.read_bytes())
        self.assertEqual(configured["CFBundleIdentifier"], "com.tmusic.tmusic")
        self.assertEqual(configured["UIBackgroundModes"], ["audio"])
        self.assertIn(self.original["CFBundleURLTypes"][0], configured["CFBundleURLTypes"])
        google = next(record for record in configured["CFBundleURLTypes"]
                      if record.get("CFBundleURLName") == "com.tmusic.google")
        self.assertEqual(google["CFBundleURLSchemes"],
                         ["com.googleusercontent.apps.123456789-iosfixture"])
        self.assertEqual(json.loads(self.output.read_text()), {
            "TMUSIC_API_URL": "https://api.example.com",
            "TMUSIC_GOOGLE_SERVER_CLIENT_ID": SERVER_ID,
            "TMUSIC_GOOGLE_IOS_CLIENT_ID": IOS_ID,
        })
        self.assertEqual(self.run_helper("ios").returncode, 0)
        self.assertEqual(plistlib.loads(self.plist.read_bytes()), configured)

    def test_android_does_not_require_an_android_client_id(self):
        self.environment.pop("TMUSIC_GOOGLE_IOS_CLIENT_ID")
        result = self.run_helper("android")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(set(json.loads(self.output.read_text())),
                         {"TMUSIC_API_URL", "TMUSIC_GOOGLE_SERVER_CLIENT_ID"})

    def test_existing_server_aliases_and_legacy_suffixes_are_supported(self):
        for alias in ("TMUSIC_GOOGLE_WEB_CLIENT_ID", "Auth__Google__Audiences"):
            with self.subTest(alias=alias):
                self.environment.pop("TMUSIC_GOOGLE_SERVER_CLIENT_ID", None)
                self.environment[alias] = "  " + SERVER_ID + "com/\\  "
                if alias == "Auth__Google__Audiences":
                    self.environment[alias] += "," + IOS_ID
                result = self.run_helper("android")
                self.assertEqual(result.returncode, 0)
                self.assertEqual(json.loads(self.output.read_text())["TMUSIC_GOOGLE_SERVER_CLIENT_ID"], SERVER_ID)
                self.environment.pop(alias)

    def test_invalid_or_missing_google_configuration_fails_without_mutating_inputs(self):
        original_bytes = self.plist.read_bytes()
        for key, value in (("TMUSIC_GOOGLE_SERVER_CLIENT_ID", ""),
                           ("TMUSIC_GOOGLE_SERVER_CLIENT_ID", "not-an-oauth-client"),
                           ("TMUSIC_GOOGLE_IOS_CLIENT_ID", ""),
                           ("TMUSIC_GOOGLE_IOS_CLIENT_ID", "secret\nvalue")):
            with self.subTest(key=key, value=value):
                old = self.environment[key]
                self.environment[key] = value
                result = self.run_helper("ios")
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(self.output.exists())
                self.assertEqual(self.plist.read_bytes(), original_bytes)
                self.assertNotIn(value or "not-an-oauth-client", result.stdout + result.stderr)
                self.environment[key] = old

    def test_non_origin_or_unsafe_api_urls_are_rejected(self):
        for api in ("http://api.example.com", "https://user:pass@api.example.com",
                    "https://api.example.com/path", "https://api.example.com?token=secret",
                    "https://api.example.com#fragment", "https://api.example.com:bad",
                    "https://api.example.com\n", "https://api.example.com:0"):
            with self.subTest(api=api):
                self.environment["TMUSIC_BUILD_API_URL"] = api
                result = self.run_helper("android")
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(self.output.exists())
                self.assertNotIn(api, result.stdout + result.stderr)

    def test_ios_requires_a_plist_target(self):
        result = self.run_helper("ios", info_plist=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.output.exists())

    def test_wrong_or_invalid_plist_is_not_overwritten(self):
        for data in (b"invalid-plist", plistlib.dumps(["not-a-dictionary"]),
                     plistlib.dumps({"CFBundleURLTypes": "invalid"})):
            with self.subTest(data=data):
                self.plist.write_bytes(data)
                result = self.run_helper("ios")
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(self.plist.read_bytes(), data)
                self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
