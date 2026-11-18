FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet restore backend/src/TMusic.Api/TMusic.Api.csproj
RUN dotnet publish backend/src/TMusic.Api/TMusic.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
# Opt in at build time; runtime Features__EnableAudioDownloads remains a separate gate.
# Noble supplies Python 3.12 and FFmpeg; the platform-independent yt-dlp needs Python >=3.11.
ARG INSTALL_AUDIO_TOOLS=false
ARG YT_DLP_VERSION=2026.10.26
ARG YT_DLP_SHA256=e51e69e89489f199530f13d32a4342da3ddb2cf34842f364603a066db05afbf3
RUN set -eu; \
    case "$INSTALL_AUDIO_TOOLS" in \
      true) \
        apt-get update; \
        apt-get install -y --no-install-recommends ca-certificates curl ffmpeg python3; \
        curl --fail --location --proto '=https' --tlsv1.2 \
          "https://github.com/yt-dlp/yt-dlp/releases/download/${YT_DLP_VERSION}/yt-dlp" \
          --output /usr/local/bin/yt-dlp; \
        printf '%s  %s\n' "$YT_DLP_SHA256" /usr/local/bin/yt-dlp | sha256sum --check --strict -; \
        chmod 0755 /usr/local/bin/yt-dlp; \
        test "$(yt-dlp --version)" = "$YT_DLP_VERSION"; \
        ffmpeg -version > /dev/null; \
        rm -rf /var/lib/apt/lists/* ;; \
      false) ;; \
      *) echo 'INSTALL_AUDIO_TOOLS must be true or false.' >&2; exit 1 ;; \
    esac
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "TMusic.Api.dll"]
