#!/bin/bash
# Installs Open World on the game's native Linux build (Steam's default on Linux, no Proton): the Linux build of BepInEx, HalfGripTools
# and the newest Open World release, the same three things the Windows launcher installs. Run it again to update. Nothing of the game's
# own is changed; the mods only run when the game is started through run_bepinex.sh (see the launch option it prints at the end).
#   tools/install-linux.sh [--multiplayer] [game folder]
# --multiplayer also installs the Multiplayer mod from HalfGripMods (main, as the Windows launcher does). Without a game folder it looks
# for "Half Grip Demo" in the Steam libraries. Needs bash, curl, unzip, tar and sha256sum.
set -euo pipefail

# Pinned, with the hash GitHub gives for the release asset: the loader runs before anything else in the game.
bepinex_version="5.4.23.5"
bepinex_sha256="e538560be65739f562519ab518a75f9c65b3f57f87457403ae7cde683c12dab7"
tools_repo="4l3x4nd3r0/HalfGripTools"
mods_repo="4l3x4nd3r0/HalfGripMods"
openworld_repo="htubdawg/HalfGripOpenWorld"
exe="HalfGrip.x86_64"

multiplayer=0
game=""
for a in "$@"; do
	case "$a" in
		--multiplayer) multiplayer=1 ;;
		-h|--help) sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		-*) echo "Unknown option $a" >&2; exit 2 ;;
		*) game="$a" ;;
	esac
done

for cmd in curl unzip tar sha256sum; do
	command -v "$cmd" > /dev/null || { echo "Needs $cmd: install it with your package manager." >&2; exit 1; }
done

# Every Steam library listed in libraryfolders.vdf, for the native, Steam-runtime and Flatpak installs of Steam.
find_game() {
	local steam vdf lib
	for steam in "$HOME/.local/share/Steam" "$HOME/.steam/steam" "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
		vdf="$steam/steamapps/libraryfolders.vdf"
		[ -f "$vdf" ] || continue
		while IFS= read -r lib; do
			if [ -f "$lib/steamapps/common/Half Grip Demo/$exe" ]; then
				echo "$lib/steamapps/common/Half Grip Demo"
				return 0
			fi
		done < <(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf"; echo "$steam")
	done
	return 1
}

if [ -z "$game" ]; then
	game="$(find_game)" || { echo "Half Grip Demo not found in the Steam libraries. Give the game folder: $0 <game folder>" >&2; exit 1; }
fi
game="$(cd "$game" && pwd)"
if [ ! -f "$game/$exe" ]; then
	if [ -f "$game/HalfGrip.exe" ]; then
		echo "$game is the Windows build (Proton). Switch Proton off in the game's Properties > Compatibility, let Steam download the" >&2
		echo "Linux build, then run this again; or use the Windows launcher inside Proton." >&2
	else
		echo "$game has no $exe: not the Half Grip Demo folder." >&2
	fi
	exit 1
fi
if pgrep -f "$game/$exe" > /dev/null 2>&1; then
	echo "Close the game first." >&2
	exit 1
fi
echo "Game: $game"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The newest release's tag, from where GitHub's "latest" page redirects to.
latest_tag() {
	curl -fsSI "https://github.com/$1/releases/latest" | sed -n 's|^[Ll]ocation:.*/tag/\([^[:space:]]*\).*|\1|p'
}

check_sha() {
	echo "$2  $1" | sha256sum -c --quiet - || { echo "Checksum of $(basename "$1") does not match: not installed." >&2; exit 1; }
}

echo "BepInEx $bepinex_version (Linux x64)"
curl -fsSL -o "$work/bepinex.zip" "https://github.com/BepInEx/BepInEx/releases/download/v$bepinex_version/BepInEx_linux_x64_$bepinex_version.zip"
check_sha "$work/bepinex.zip" "$bepinex_sha256"
unzip -o -q "$work/bepinex.zip" -d "$game"
# Steam passes the game through its own wrappers, so the script is told the executable instead of guessing it from the arguments.
sed -i "s/^executable_name=.*/executable_name=\"$exe\"/" "$game/run_bepinex.sh"
chmod +x "$game/run_bepinex.sh"

tag="$(latest_tag "$tools_repo")"
[ -n "$tag" ] || { echo "Could not find the newest HalfGripTools release." >&2; exit 1; }
echo "HalfGripTools $tag"
zip="HalfGripTools-$tag.zip"
curl -fsSL -o "$work/$zip" "https://github.com/$tools_repo/releases/download/$tag/$zip"
curl -fsSL -o "$work/tools.sums" "https://github.com/$tools_repo/releases/download/$tag/SHA256SUMS.txt"
# SHA256SUMS.txt is written on Windows: CRLF line ends.
check_sha "$work/$zip" "$(tr -d '\r' < "$work/tools.sums" | awk -v f="$zip" '$2 == f || $2 == "*" f { print $1 }')"
unzip -o -q "$work/$zip" -d "$game"

mods="$game/BepInEx/HalfGripMods"
mkdir -p "$mods"

tag="$(latest_tag "$openworld_repo")"
[ -n "$tag" ] || { echo "Could not find the newest Open World release." >&2; exit 1; }
echo "Open World $tag"
curl -fsSL -o "$work/OpenWorld.zip" "https://github.com/$openworld_repo/releases/download/$tag/OpenWorld.zip"
unzip -q "$work/OpenWorld.zip" -d "$work/ow"
[ -f "$work/ow/OpenWorld/mod.json" ] || { echo "OpenWorld.zip has no OpenWorld/mod.json: not installed." >&2; exit 1; }
# Replaced whole, so a file a new version drops does not stay behind. Its settings are in BepInEx/config and are kept.
rm -rf "$mods/OpenWorld"
mv "$work/ow/OpenWorld" "$mods/OpenWorld"

if [ "$multiplayer" = 1 ]; then
	echo "Multiplayer (HalfGripMods main)"
	curl -fsSL -o "$work/mods.tar.gz" "https://codeload.github.com/$mods_repo/tar.gz/refs/heads/main"
	mkdir -p "$work/mods"
	tar -xzf "$work/mods.tar.gz" -C "$work/mods" --strip-components=2 --wildcards '*/mods/Multiplayer/*'
	[ -f "$work/mods/Multiplayer/mod.json" ] || { echo "HalfGripMods has no mods/Multiplayer/mod.json: not installed." >&2; exit 1; }
	rm -rf "$mods/Multiplayer"
	mv "$work/mods/Multiplayer" "$mods/Multiplayer"
fi

cat <<EOF

Installed. One step left, in Steam: right-click Half Grip Demo > Properties > General > Launch Options, and paste
    ./run_bepinex.sh %command%
Then start the game. $game/BepInEx/LogOutput.log should have "Loaded Open World".
To play without mods, clear the launch option.
EOF
