#!/bin/sh
set -eu

install_dir="$HOME/Applications/AZOTH.app"
if [ -L "$install_dir" ] || [ ! -d "$install_dir" ]; then
    printf '%s\n' "No ordinary AZOTH app bundle was found at $install_dir."
    exit 1
fi
resolved_parent=$(CDPATH= cd -- "$HOME/Applications" && pwd -P)
resolved_app=$(CDPATH= cd -- "$install_dir" && pwd -P)
if [ "$resolved_app" != "$resolved_parent/AZOTH.app" ]; then
    printf '%s\n' 'Refusing to remove an app outside the expected Applications directory.'
    exit 1
fi
bundle_id=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$install_dir/Contents/Info.plist")
if [ "$bundle_id" != 'io.github.Veilric.AZOTH' ]; then
    printf '%s\n' 'The bundle is not AZOTH.'
    exit 1
fi
"$install_dir/Contents/MacOS/AZOTH" --uninstall-cleanup
if [ -L "$HOME/.local/bin/azoth" ] && [ "$(readlink "$HOME/.local/bin/azoth")" = "$install_dir/Contents/MacOS/azoth-cli" ]; then
    rm -- "$HOME/.local/bin/azoth"
fi
rm -rf -- "$install_dir"
printf '%s\n' 'AZOTH was removed. Your settings and conversion results were kept.'
