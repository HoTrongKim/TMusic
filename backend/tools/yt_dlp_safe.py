#!/usr/bin/python3
"""Run the verified yt-dlp bundle with TMusic's public-only TikTok policy.

The existing downloader proxy remains responsible for destination validation,
public DNS pinning, redirect checks and request bounds. This adapter removes
TikTok's browser fingerprint randomization and rejects access challenges.
"""

from functools import wraps
from html.parser import HTMLParser
import inspect
from pathlib import Path
import sys


BUNDLE_PATH = Path("/usr/local/lib/tmusic/yt-dlp.pyz")
SUPPORTED_VERSION = "2026.10.26"
USER_AGENT = "TMusic/2.0"
ACCESS_CHECK_ERROR = "TMUSIC_SOURCE_ACCESS_CHECK_REQUIRED"
COMPATIBILITY_ERROR = "TMUSIC_YTDLP_COMPATIBILITY_ERROR"


class CompatibilityError(RuntimeError):
    """The pinned extractor no longer has the reviewed policy boundaries."""


class _AccessCheckMarkers(HTMLParser):
    """Recognize gate elements without decoding or executing their data."""

    def __init__(self):
        super().__init__()
        self.challenge_data = False
        self.challenge_cookie = False

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        if not (attributes.get("class") or "").strip():
            return
        if attributes.get("id") == "cs":
            self.challenge_data = True
        elif attributes.get("id") == "wci":
            self.challenge_cookie = True


def apply_tiktok_policy(version, tiktok_base, extractor_error):
    """Validate all reviewed hooks before changing the TikTok extractor only."""
    if version != SUPPORTED_VERSION:
        raise CompatibilityError(COMPATIBILITY_ERROR)

    expected_parameters = {
        "_generate_blockbuster_headers": ("min_headers", "max_headers"),
        "_download_webpage_handle": (
            "url_or_request", "video_id", "note", "errnote", "fatal", "encoding",
            "data", "headers", "query", "expected_status", "impersonate", "require_impersonation"),
        "_solve_challenge_and_set_cookies": ("webpage",),
        "_extract_web_data_and_status": ("url", "video_id", "fatal"),
        "_get_universal_data": ("webpage", "display_id"),
    }
    for name, expected in expected_parameters.items():
        method = getattr(tiktok_base, name, None)
        if not callable(method):
            raise CompatibilityError(COMPATIBILITY_ERROR)
        try:
            parameters = tuple(inspect.signature(method).parameters)
        except (TypeError, ValueError) as error:
            raise CompatibilityError(COMPATIBILITY_ERROR) from error
        if name != "_generate_blockbuster_headers":
            parameters = parameters[1:]  # The unbound instance parameter.
        if parameters != expected:
            raise CompatibilityError(COMPATIBILITY_ERROR)

    original_download = tiktok_base._download_webpage_handle
    download_signature = inspect.signature(original_download)

    def honest_headers(*, min_headers=2, max_headers=8):
        return {"User-Agent": USER_AGENT}

    @wraps(original_download)
    def public_webpage(self, *args, **kwargs):
        request = download_signature.bind(self, *args, **kwargs)
        request.arguments["impersonate"] = False
        request.arguments["require_impersonation"] = False
        headers = {
            name: value for name, value in (request.arguments.get("headers") or {}).items()
            if name.lower() != "user-agent"
        }
        headers["User-Agent"] = USER_AGENT
        request.arguments["headers"] = headers
        return original_download(*request.args, **request.kwargs)

    def reject_challenge(self, webpage):
        # Do not execute the original solver, obtain cookies, or retry the gate.
        markers = _AccessCheckMarkers()
        markers.feed(webpage)
        message = (ACCESS_CHECK_ERROR if markers.challenge_data and markers.challenge_cookie
                   else "Unexpected response from webpage request")
        raise extractor_error(message, expected=True)

    tiktok_base._generate_blockbuster_headers = staticmethod(honest_headers)
    tiktok_base._download_webpage_handle = public_webpage
    tiktok_base._solve_challenge_and_set_cookies = reject_challenge


def main(argv=None):
    try:
        if not BUNDLE_PATH.is_file():
            raise CompatibilityError(COMPATIBILITY_ERROR)
        sys.path.insert(0, str(BUNDLE_PATH))
        from yt_dlp import main as upstream_main
        from yt_dlp.extractor.tiktok import TikTokBaseIE
        from yt_dlp.utils import ExtractorError
        from yt_dlp.version import __version__

        apply_tiktok_policy(__version__, TikTokBaseIE, ExtractorError)
    except (ImportError, CompatibilityError):
        # Keep failure output independent of arguments, URLs and credentials.
        print(COMPATIBILITY_ERROR, file=sys.stderr)
        return 78

    return upstream_main(argv)


if __name__ == "__main__":
    sys.exit(main())
