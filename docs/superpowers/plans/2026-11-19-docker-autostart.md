# TMusic Docker Autostart Implementation Plan

**Goal:** Run the existing V2 SQL Server, API and worker as restartable Docker services without modifying `.env` or existing database data.

**Architecture:** Keep the existing SQL volume; add a separate Compose override for API and worker with a shared persistent application volume. Supply the existing local Docker SQL credential at container creation without committing it. A stable Internet endpoint remains dependent on a user-provided Cloudflare account and domain.

**Tech stack:** Docker Compose, .NET 10, SQL Server 2022, Windows PowerShell.

## Steps

- [x] Configure writable shared storage and opt-in audio tooling in the API and worker images.
- [x] Add restartable API and worker services with isolated local port and explicit V2 SQL connection.
- [x] Provide a safe setup script that reads the protected local credential, preserves `.env` and never resets volumes.
- [x] Build and start services, verify database readiness and shared writable storage, and record results.
- [x] Document the stable public tunnel dependency and any unverified features.
