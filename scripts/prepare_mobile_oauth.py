#!/usr/bin/env python3
"""Prepare public Google OAuth build settings without copying environment secrets.

Client IDs are public identifiers, not client secrets. Both native applications
send real Google ID tokens to the existing backend for independent verification.
"""

import argparse
import json
import os
from pathlib import Path
import plistlib
import re
import sys
from urllib.parse import urlsplit


CLIENT_ID = re.compile(r"[0-9]+-[A-Za-z0-9_-]+\.apps\.googleusercontent\.com", re.ASCII)
MANAGED_URL_NAME = "com.tmusic.google"


class ConfigurationError(ValueError):
    """A public build setting is absent or malformed; never include its value."""


def normalize_client_id(raw):
    value = raw.strip()
    while value.endswith(("/", "\\")):
        value = value[:-1].strip()
    while value.lower().endswith(".apps.googleusercontent.comcom"):
        value = value[:-3]
    if not CLIENT_ID.fullmatch(value):
        raise ConfigurationError("GOOGLE_CLIENT_ID_MISSING_OR_INVALID")
    return value


def first_value(environment, *names):
    return next((environment[name] for name in names
                 if environment.get(name, "").strip()), "")


def api_origin(raw):
    if any(ord(character) <= 32 or ord(character) == 127 for character in raw):
        raise ConfigurationError("API_HTTPS_ORIGIN_REQUIRED")
    try:
        parsed = urlsplit(raw)
        valid = (parsed.scheme == "https" and parsed.hostname and
                 not parsed.username and not parsed.password and
                 parsed.path in ("", "/") and not parsed.query and not parsed.fragment and
                 parsed.port != 0)
    except ValueError:
        valid = False
    if not valid:
        raise ConfigurationError("API_HTTPS_ORIGIN_REQUIRED")
    return raw.rstrip("/")


def prepare(platform, output, info_plist, environment):
    if output.suffix.lower() != ".json":
        raise ConfigurationError("DART_DEFINES_JSON_OUTPUT_REQUIRED")
    api = api_origin(first_value(environment, "TMUSIC_BUILD_API_URL", "TMUSIC_API_URL"))
    server_id = first_value(environment, "TMUSIC_GOOGLE_SERVER_CLIENT_ID", "TMUSIC_GOOGLE_WEB_CLIENT_ID")
    if not server_id:
        audiences = first_value(environment, "Auth__Google__Audiences", "Auth__google__Audiences",
                                "Auth:Google:Audiences", "Auth:google:Audiences")
        server_id = next((part for part in audiences.split(",") if part.strip()), "")
    defines = {
        "TMUSIC_API_URL": api,
        "TMUSIC_GOOGLE_SERVER_CLIENT_ID": normalize_client_id(server_id),
    }
    configured_plist = None
    if platform == "ios":
        if info_plist is None or info_plist.suffix.lower() != ".plist":
            raise ConfigurationError("IOS_INFO_PLIST_REQUIRED")
        ios_id = normalize_client_id(environment.get("TMUSIC_GOOGLE_IOS_CLIENT_ID", ""))
        defines["TMUSIC_GOOGLE_IOS_CLIENT_ID"] = ios_id
        try:
            configured_plist = plistlib.loads(info_plist.read_bytes())
        except (OSError, plistlib.InvalidFileException, ValueError):
            raise ConfigurationError("IOS_INFO_PLIST_INVALID") from None
        if not isinstance(configured_plist, dict):
            raise ConfigurationError("IOS_INFO_PLIST_INVALID")
        url_types = configured_plist.get("CFBundleURLTypes", [])
        if not isinstance(url_types, list) or any(not isinstance(record, dict) for record in url_types):
            raise ConfigurationError("IOS_INFO_PLIST_INVALID")
        url_types = [record for record in url_types if record.get("CFBundleURLName") != MANAGED_URL_NAME]
        reversed_id = ".".join(reversed(ios_id.split(".")))
        url_types.append({"CFBundleURLName": MANAGED_URL_NAME,
                          "CFBundleURLSchemes": [reversed_id]})
        configured_plist["CFBundleURLTypes"] = url_types
    else:
        if info_plist is not None:
            raise ConfigurationError("IOS_PLIST_NOT_USED_ON_ANDROID")
        android_id = environment.get("TMUSIC_GOOGLE_ANDROID_CLIENT_ID", "")
        if android_id.strip():
            defines["TMUSIC_GOOGLE_ANDROID_CLIENT_ID"] = normalize_client_id(android_id)

    # Emit a fixed allowlist. Never serialize environment variables wholesale.
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(defines, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
    if configured_plist is not None:
        info_plist.write_bytes(plistlib.dumps(configured_plist, sort_keys=False))


def main(argv=None):
    parser = argparse.ArgumentParser(description="Prepare public Google OAuth mobile build settings.")
    parser.add_argument("--platform", required=True, choices=("android", "ios"))
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--info-plist", type=Path)
    arguments = parser.parse_args(argv)
    try:
        prepare(arguments.platform, arguments.output, arguments.info_plist, os.environ)
    except ConfigurationError as error:
        print(str(error), file=sys.stderr)
        return 2
    except OSError:
        print("MOBILE_BUILD_CONFIGURATION_WRITE_FAILED", file=sys.stderr)
        return 2
    print("Public mobile OAuth configuration prepared.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
