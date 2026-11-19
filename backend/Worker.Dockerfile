FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet restore backend/src/TMusic.Worker/TMusic.Worker.csproj
RUN dotnet publish backend/src/TMusic.Worker/TMusic.Worker.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
# Keep the optional audio runtime identical to the API image.
ARG INSTALL_AUDIO_TOOLS=false
ARG YT_DLP_VERSION=2026.10.26
ARG YT_DLP_SHA256=e51e69e89489f199530f13d32a4342da3ddb2cf34842f364603a066db05afbf3
ARG DENO_VERSION=2.9.8
ARG DENO_AMD64_SHA256=e890975bc2549807bbcc0b584ffafda72cc7c93349dd99c80483dd784f6c4609
ARG DENO_ARM64_SHA256=d49596c20b9788cf8f5d8ddf644103d4bde56ed239753f0bdb92ba9361d51905
RUN set -eu; \
    case "$INSTALL_AUDIO_TOOLS" in \
      true) \
        apt-get update; \
        apt-get install -y --no-install-recommends ca-certificates curl ffmpeg python3 unzip; \
        curl --fail --location --proto '=https' --tlsv1.2 \
          "https://github.com/yt-dlp/yt-dlp/releases/download/${YT_DLP_VERSION}/yt-dlp" \
          --output /usr/local/bin/yt-dlp; \
        printf '%s  %s\n' "$YT_DLP_SHA256" /usr/local/bin/yt-dlp | sha256sum --check --strict -; \
        chmod 0755 /usr/local/bin/yt-dlp; \
        test "$(yt-dlp --version)" = "$YT_DLP_VERSION"; \
        arch="$(dpkg --print-architecture)"; \
        case "$arch" in \
          amd64) deno_target="x86_64-unknown-linux-gnu"; deno_sha256="$DENO_AMD64_SHA256" ;; \
          arm64) deno_target="aarch64-unknown-linux-gnu"; deno_sha256="$DENO_ARM64_SHA256" ;; \
          *) echo "Unsupported architecture for deno: $arch" >&2; exit 1 ;; \
        esac; \
        curl --fail --location --proto '=https' --tlsv1.2 \
          "https://github.com/denoland/deno/releases/download/v${DENO_VERSION}/deno-${deno_target}.zip" \
          --output /tmp/deno.zip; \
        printf '%s  %s\n' "$deno_sha256" /tmp/deno.zip | sha256sum --check --strict -; \
        unzip -q -o /tmp/deno.zip deno -d /usr/local/bin; \
        rm -f /tmp/deno.zip; \
        chmod 0755 /usr/local/bin/deno; \
        deno --version > /dev/null; \
        ffmpeg -version > /dev/null; \
        rm -rf /var/lib/apt/lists/* ;; \
      false) ;; \
      *) echo 'INSTALL_AUDIO_TOOLS must be true or false.' >&2; exit 1 ;; \
    esac
# Keep the verified upstream bundle intact; the launcher disables TikTok access-check solving.
COPY backend/tools/yt_dlp_safe.py /opt/tmusic/yt_dlp_safe.py
RUN if [ "$INSTALL_AUDIO_TOOLS" = true ]; then \
      mkdir -p /usr/local/lib/tmusic; \
      mv /usr/local/bin/yt-dlp /usr/local/lib/tmusic/yt-dlp.pyz; \
      install -m 0755 /opt/tmusic/yt_dlp_safe.py /usr/local/bin/yt-dlp; \
      test "$(yt-dlp --version)" = "$YT_DLP_VERSION"; \
    fi
WORKDIR /app
COPY --from=build /out/ ./
RUN mkdir -p /data/audio /data/covers /data/keys && chown -R "$APP_UID":0 /data
USER $APP_UID
ENTRYPOINT ["dotnet", "TMusic.Worker.dll"]
