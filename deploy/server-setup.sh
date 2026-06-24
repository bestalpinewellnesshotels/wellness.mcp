#!/bin/bash
# Einmaliges Server-Setup als User "mcp" ausfuehren.
# Aufruf auf dem Server: bash server-setup.sh

set -euo pipefail

APP_DIR="${APP_DIR:-/data/web/mcp/home/app}"
MCP_DIR="${MCP_DIR:-/data/web/mcp/home/mcp}"
SYSTEMD_DIR="${HOME}/.config/systemd/user"

echo "==> Verzeichnisse anlegen"
mkdir -p "$APP_DIR" "$MCP_DIR" "$SYSTEMD_DIR" "${HOME}/log"

echo "==> Node.js pruefen"
if ! command -v node >/dev/null 2>&1; then
  echo "FEHLER: Node.js fehlt. Bitte Mynet um Node.js 20+ bitten."
  exit 1
fi
node --version

echo "==> User-Services duerfen ohne Login laufen (linger)"
if command -v loginctl >/dev/null 2>&1; then
  loginctl enable-linger "$(whoami)" || true
fi

echo "==> systemd User-Daemon neu laden"
systemctl --user daemon-reload

echo
echo "Setup abgeschlossen."
echo "Als Naechstes vom Windows-PC: .\\scripts\\deploy-bestalpine.ps1 -FirstDeploy"
