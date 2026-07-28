#!/usr/bin/env bash
# Builds a self-contained Windows deployment of TAT on this Mac: the Angular
# client is compiled to static files and dropped into server/wwwroot, then
# the server is published as a single win-x64 executable that serves both
# the UI and the API from one port. The result needs nothing installed on
# the Windows machine — no Node, no .NET runtime — just unzip and run.
set -euo pipefail
cd "$(dirname "$0")"

echo "==> Building Angular client (production)"
(cd client && npx ng build)

echo "==> Copying client build into server/wwwroot"
rm -rf server/wwwroot
mkdir -p server/wwwroot
cp -R client/dist/client/browser/. server/wwwroot/

echo "==> Publishing self-contained win-x64 server"
rm -rf publish
dotnet publish server \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish

echo "==> Snapshotting current database into publish"
# sqlite3's .backup does an online, WAL-safe snapshot (unlike a plain cp,
# which could grab tat.db without the not-yet-checkpointed tat.db-wal
# writes and produce a copy that's missing recent changes).
sqlite3 server/tat.db ".backup 'publish/tat.db'"

echo "==> Done. Copy the 'publish' folder to the Windows machine and run Server.exe"
