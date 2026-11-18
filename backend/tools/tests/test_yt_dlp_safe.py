"""Offline policy tests against the real, pinned yt-dlp distribution.

Run with Python's unittest discovery. TMUSIC_YT_DLP_TEST_BUNDLE may point to
the verified upstream zip when testing an image before installing its wrapper.
"""

import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import unittest
from unittest import mock


BUNDLE_PATH = Path(os.environ.get(
    "TMUSIC_YT_DLP_TEST_BUNDLE", "/usr/local/lib/tmusic/yt-dlp.pyz"))
sys.path.insert(0, str(BUNDLE_PATH))

from yt_dlp import YoutubeDL
from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.extractor.tiktok import TikTokBaseIE, TikTokIE
from yt_dlp.extractor.youtube import YoutubeIE
from yt_dlp.networking.common import Response
from yt_dlp.networking.exceptions import HTTPError
from yt_dlp.utils import ExtractorError
from yt_dlp.version import __version__


LAUNCHER_PATH = Path(__file__).resolve().parents[1] / "yt_dlp_safe.py"
if LAUNCHER_PATH.is_file():
    spec = importlib.util.spec_from_file_location("yt_dlp_safe", LAUNCHER_PATH)
    launcher = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(launcher)
else:
    launcher = None


class TikTokPolicyTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(launcher, "The production safety launcher is missing")
        self.assertEqual(__version__, "2026.10.26", "Tests require the pinned upstream bundle")
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        for name in (
            "_generate_blockbuster_headers",
            "_download_webpage_handle",
            "_solve_challenge_and_set_cookies",
        ):
            # Restore inherited attributes correctly after each policy installation.
            self.stack.enter_context(mock.patch.object(TikTokBaseIE, name, getattr(TikTokBaseIE, name)))
        self.downloader = YoutubeDL({"quiet": True, "no_warnings": True})
        self.addCleanup(self.downloader.close)
        self.extractor = TikTokIE(self.downloader)

    def install_policy(self, version=__version__):
        launcher.apply_tiktok_policy(version, TikTokBaseIE, ExtractorError)

    def respond_with(self, body, url="https://www.tiktok.com/@creator/video/123"):
        response = Response(
            io.BytesIO(body.encode()), url,
            {"Content-Type": "text/html; charset=utf-8"}, extensions={})
        return self.stack.enter_context(mock.patch.object(
            self.downloader, "urlopen", return_value=response))

    def forbid_cookie_operations(self):
        for name in ("_set_cookie", "_get_cookies"):
            self.stack.enter_context(mock.patch.object(
                self.extractor, name,
                side_effect=AssertionError("Cookie access is forbidden in this flow")))
        for name in ("clear", "set_cookie", "extract_cookies", "add_cookie_header", "load", "save"):
            self.stack.enter_context(mock.patch.object(
                self.downloader.cookiejar, name,
                side_effect=AssertionError("Cookie access is forbidden in this flow")))

    def test_public_page_uses_original_handler_without_impersonation(self):
        original_handler = InfoExtractor._download_webpage_handle
        self.install_policy()
        self.respond_with("public page")
        parse_targets = self.stack.enter_context(mock.patch.object(
            self.downloader, "_parse_impersonate_targets",
            wraps=self.downloader._parse_impersonate_targets))

        body, response = self.extractor._download_webpage_handle(
            "https://www.tiktok.com/@creator/video/123#fragment", "123",
            headers={"User-Agent": "TMusic/2.0"}, impersonate=True)

        self.assertEqual(body, "public page")
        self.assertEqual(response.url, "https://www.tiktok.com/@creator/video/123")
        parse_targets.assert_called_once_with(False)
        self.assertIs(InfoExtractor._download_webpage_handle, original_handler)

    def test_public_video_data_uses_honest_headers_and_no_cookies(self):
        self.install_policy()
        self.forbid_cookie_operations()
        video = {"id": "123", "video": {"duration": 1}}
        data = {"__DEFAULT_SCOPE__": {"webapp.video-detail": {
            "statusCode": 0, "itemInfo": {"itemStruct": video}}}}
        request = self.respond_with(
            '<script id="__UNIVERSAL_DATA_FOR_REHYDRATION__" type="application/json">'
            + json.dumps(data) + "</script>")

        result, status = self.extractor._extract_web_data_and_status(
            "https://www.tiktok.com/@creator/video/123", "123")

        self.assertEqual((result, status), (video, 0))
        request.assert_called_once()
        sent = request.call_args.args[0]
        self.assertEqual(dict(sent.headers), {"User-Agent": "TMusic/2.0"})
        self.assertNotIn("impersonate", sent.extensions)

    def test_positional_impersonation_flags_cannot_enable_browser_identity(self):
        self.install_policy()
        request = self.respond_with("public page")
        parse_targets = self.stack.enter_context(mock.patch.object(
            self.downloader, "_parse_impersonate_targets",
            wraps=self.downloader._parse_impersonate_targets))

        body, _ = self.extractor._download_webpage_handle(
            "https://www.tiktok.com/@creator/video/123", "123",
            None, None, True, None, None,
            {"user-agent": "unwanted browser identity", "Accept": "text/html"},
            {}, None, True, True)

        self.assertEqual(body, "public page")
        parse_targets.assert_called_once_with(False)
        self.assertEqual(dict(request.call_args.args[0].headers), {
            "User-Agent": "TMusic/2.0", "Accept": "text/html"})

    def test_access_check_stops_before_original_solver_or_cookie_changes(self):
        original_solver_calls = []

        def forbidden_solver(_self, webpage):
            original_solver_calls.append(webpage)
            raise AssertionError("Upstream challenge solver must never run")

        self.stack.enter_context(mock.patch.object(
            TikTokBaseIE, "_solve_challenge_and_set_cookies", forbidden_solver))
        self.install_policy()
        self.forbid_cookie_operations()
        request = self.respond_with(
            "<html><div id='cs' class='inert-challenge-data'></div>"
            "<div id='wci' class='_wafchallengeid'></div></html>")

        with self.assertRaises(ExtractorError) as caught:
            self.extractor._extract_web_data_and_status(
                "https://www.tiktok.com/@creator/video/123", "123")

        self.assertTrue(caught.exception.expected)
        self.assertEqual(caught.exception.orig_msg, "TMUSIC_SOURCE_ACCESS_CHECK_REQUIRED")
        self.assertEqual(original_solver_calls, [])
        request.assert_called_once()

    def test_unknown_page_is_not_mislabeled_as_an_access_check(self):
        self.install_policy()
        self.forbid_cookie_operations()
        for body in (
            "<html><h1>Temporarily unavailable</h1></html>",
            "<html>Please wait...</html>",
            "<html><div id='cs'>Unrelated content</div></html>",
            '<script id="__UNIVERSAL_DATA_FOR_REHYDRATION__">{broken-json}</script>',
            "<script>const sample = \"<div id='cs' class='data'></div>"
            "<div id='wci' class='_wafchallengeid'></div>\";</script>",
        ):
            with self.subTest(body=body):
                request = self.respond_with(body)
                with self.assertRaises(ExtractorError) as caught:
                    self.extractor._extract_web_data_and_status(
                        "https://www.tiktok.com/@creator/video/123", "123")
                self.assertTrue(caught.exception.expected)
                self.assertEqual(caught.exception.orig_msg, "Unexpected response from webpage request")
                request.assert_called_once()

    def test_login_wall_is_not_retried(self):
        self.install_policy()
        self.forbid_cookie_operations()
        request = self.respond_with("login required", "https://www.tiktok.com/login")

        with self.assertRaises(ExtractorError) as caught:
            self.extractor._extract_web_data_and_status(
                "https://www.tiktok.com/@creator/video/123", "123")

        self.assertTrue(caught.exception.expected)
        self.assertIn("requiring login", caught.exception.orig_msg)
        request.assert_called_once()

    def test_http_access_denial_propagates_without_another_request(self):
        self.install_policy()
        self.forbid_cookie_operations()
        response = Response(io.BytesIO(b"denied"),
            "https://www.tiktok.com/@creator/video/123", {}, status=403)
        request = self.stack.enter_context(mock.patch.object(
            self.downloader, "urlopen", side_effect=HTTPError(response)))

        with self.assertRaises(ExtractorError) as caught:
            self.extractor._extract_web_data_and_status(
                "https://www.tiktok.com/@creator/video/123", "123")

        self.assertIsInstance(caught.exception.cause, HTTPError)
        self.assertEqual(caught.exception.cause.status, 403)
        request.assert_called_once()

    def test_other_provider_keeps_its_original_request_behavior(self):
        original_handler = YoutubeIE._download_webpage_handle
        original_headers = YoutubeIE._generate_blockbuster_headers
        self.install_policy()
        self.respond_with("youtube page", "https://www.youtube.com/watch?v=offline-test")
        parse_targets = self.stack.enter_context(mock.patch.object(
            self.downloader, "_parse_impersonate_targets", return_value=(None, [])))

        body, _ = YoutubeIE(self.downloader)._download_webpage_handle(
            "https://www.youtube.com/watch?v=offline-test", "offline-test", impersonate=True)

        self.assertEqual(body, "youtube page")
        self.assertIs(YoutubeIE._download_webpage_handle, original_handler)
        self.assertIs(YoutubeIE._generate_blockbuster_headers, original_headers)
        parse_targets.assert_called_once_with(True)

    def test_version_mismatch_fails_before_policy_mutation(self):
        original_handler = TikTokBaseIE._download_webpage_handle
        with self.assertRaises(launcher.CompatibilityError):
            self.install_policy("2026.10.27")
        self.assertIs(TikTokBaseIE._download_webpage_handle, original_handler)

    def test_missing_expected_internals_fail_closed(self):
        for name in (
            "_generate_blockbuster_headers", "_download_webpage_handle",
            "_solve_challenge_and_set_cookies", "_extract_web_data_and_status",
            "_get_universal_data",
        ):
            with self.subTest(member=name), mock.patch.object(TikTokBaseIE, name, None):
                with self.assertRaises(launcher.CompatibilityError):
                    self.install_policy()

    def test_changed_expected_signature_fails_before_policy_mutation(self):
        original_handler = TikTokBaseIE._download_webpage_handle
        with mock.patch.object(TikTokBaseIE, "_solve_challenge_and_set_cookies", lambda self: None):
            with self.assertRaises(launcher.CompatibilityError):
                self.install_policy()
        self.assertIs(TikTokBaseIE._download_webpage_handle, original_handler)

    def test_missing_bundle_returns_safe_error_without_starting_upstream(self):
        error = io.StringIO()
        with mock.patch.object(launcher, "BUNDLE_PATH", Path("/missing/tmusic/yt-dlp.pyz")):
            with contextlib.redirect_stderr(error):
                result = launcher.main(["--version"])
        self.assertEqual(result, 78)
        self.assertEqual(error.getvalue(), "TMUSIC_YTDLP_COMPATIBILITY_ERROR\n")

    def test_main_rejects_incompatible_bundle_before_upstream_cli(self):
        error = io.StringIO()
        with mock.patch.object(launcher, "BUNDLE_PATH", BUNDLE_PATH):
            with mock.patch.object(launcher, "SUPPORTED_VERSION", "incompatible"):
                with mock.patch("yt_dlp.main", side_effect=AssertionError("CLI must not start")):
                    with mock.patch.object(sys, "path", list(sys.path)):
                        with contextlib.redirect_stderr(error):
                            result = launcher.main(["https://www.tiktok.com/@creator/video/123"])
        self.assertEqual(result, 78)
        self.assertEqual(error.getvalue(), "TMUSIC_YTDLP_COMPATIBILITY_ERROR\n")


if __name__ == "__main__":
    unittest.main()
