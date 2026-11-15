#!/usr/bin/env bash
# Installs QuickCommands for the current user (no root needed):
#   ~/.local/share/quickcommands/QuickCommands, ~/.local/bin/quickcommands, app menu entry and icon.
# ./install.sh --uninstall removes it again (your commands in ~/.quickcommands are kept).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
bin="$HOME/.local/bin"
app_dir="$data/quickcommands"
desktop="$data/applications/quickcommands.desktop"
icon="$data/icons/hicolor/256x256/apps/quickcommands.png"

if [ "${1:-}" = "--uninstall" ]; then
  if [ -x "$app_dir/QuickCommands" ]; then
    "$app_dir/QuickCommands" --remove-integration || true
  fi
  rm -rf "$app_dir" "$bin/quickcommands" "$desktop" "$icon"
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" || true
  echo "QuickCommands removed. Your commands in ~/.quickcommands are kept."
  exit 0
fi

mkdir -p "$app_dir" "$bin" "$data/applications" "$(dirname "$icon")"
install -m 755 "$here/QuickCommands" "$app_dir/QuickCommands"
ln -sf "$app_dir/QuickCommands" "$bin/quickcommands"
install -m 644 "$here/quickcommands.png" "$icon"
sed "s|@EXEC@|$app_dir/QuickCommands|" "$here/quickcommands.desktop" > "$desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" || true

echo "QuickCommands installed. Start it from the app menu or run: quickcommands"
case ":$PATH:" in
  *":$bin:"*) ;;
  *) echo "Note: $bin is not in your PATH." ;;
esac
