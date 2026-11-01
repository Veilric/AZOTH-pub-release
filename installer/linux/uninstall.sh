#!/bin/sh
set -eu

data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
install_dir="$data_dir/azoth/application"
desktop_file="$data_dir/applications/io.github.Veilric.AZOTH.desktop"
gnome_extension_id="azoth-donut@veilric"
gnome_extension_dir="$data_dir/gnome-shell/extensions/$gnome_extension_id"
python3 - "$data_dir" "$install_dir" <<'PY'
import pathlib
import sys

root, target = map(pathlib.Path, sys.argv[1:])
expected = root.resolve() / 'azoth' / 'application'
if target.is_symlink() or target.resolve() != expected:
    raise SystemExit('Refusing to remove an installation outside its expected directory.')
if (target / '.azoth-package').read_text(encoding='utf-8').strip() != 'io.github.Veilric.AZOTH':
    raise SystemExit('The directory is not an installation created by AZOTH.')
PY
"$install_dir/AZOTH" --uninstall-cleanup
if [ -f "$gnome_extension_dir/.azoth-package" ] && [ "$(cat "$gnome_extension_dir/.azoth-package")" = 'io.github.Veilric.AZOTH' ]; then
    if command -v gnome-extensions >/dev/null 2>&1; then
        gnome-extensions disable "$gnome_extension_id" >/dev/null 2>&1 || true
    fi
    rm -rf -- "$gnome_extension_dir"
fi
if [ -L "$HOME/.local/bin/azoth" ] && [ "$(readlink "$HOME/.local/bin/azoth")" = "$install_dir/azoth-cli" ]; then
    rm -- "$HOME/.local/bin/azoth"
fi
if [ -f "$desktop_file" ] && grep -q '^X-AZOTH-Managed=true$' "$desktop_file"; then
    rm -- "$desktop_file"
fi
rm -rf -- "$install_dir"
printf '%s\n' 'AZOTH was removed. Your settings and conversion results were kept.'
