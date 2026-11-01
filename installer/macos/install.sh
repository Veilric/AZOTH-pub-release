#!/bin/sh
set -eu

source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
install_dir="$HOME/Applications/AZOTH.app"
bin_dir="$HOME/.local/bin"
if [ -e "$install_dir" ] || [ -L "$install_dir" ]; then
    printf '%s\n' "An installation already exists at $install_dir. Run uninstall.sh first."
    exit 1
fi
# A azoth link left by an earlier AZOTH whose app is gone (moved to the Trash instead of uninstall.sh) is replaced.
# Anything else at that path isn't ours and stays.
stale_link=false
if [ -L "$bin_dir/azoth" ] && [ ! -e "$bin_dir/azoth" ]; then
    case $(readlink "$bin_dir/azoth") in
        */AZOTH.app/Contents/MacOS/azoth-cli) stale_link=true ;;
    esac
fi
if [ "$stale_link" = false ] && { [ -e "$bin_dir/azoth" ] || [ -L "$bin_dir/azoth" ]; }; then
    printf '%s\n' "$bin_dir/azoth already exists. Remove or rename it before installing AZOTH."
    exit 1
fi
test -x "$source_dir/AZOTH.app/Contents/MacOS/AZOTH"
mkdir -p "$HOME/Applications" "$bin_dir"
/usr/bin/ditto "$source_dir/AZOTH.app" "$install_dir"
# ditto keeps the download's quarantine flag: macOS would then start AZOTH from a translocated copy and could stop
# the azoth command without a word (#316).
/usr/bin/xattr -dr com.apple.quarantine "$install_dir" 2>/dev/null || true
if [ "$stale_link" = true ]; then
    rm -f -- "$bin_dir/azoth"
    printf '%s\n' "Replaced the azoth link left by an earlier installation."
fi
ln -s "$install_dir/Contents/MacOS/azoth-cli" "$bin_dir/azoth"
printf '%s\n' "Installed $install_dir" "CLI: $bin_dir/azoth (add $bin_dir to PATH if needed)."
printf '%s\n' 'Open AZOTH from Applications. Grant Accessibility and Finder Automation access when requested.'
printf '%s\n' 'If AZOTH is already listed and switched on under Accessibility, remove it with the - button and add it again with +.'
