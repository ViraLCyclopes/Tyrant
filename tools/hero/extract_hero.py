"""Exports the game's main-menu background as Tyrant's start-screen art (studio/static/hero.jpg).

Run once: python tools/hero/extract_hero.py "<Prehistoric Kingdom folder>"
Reads Prehistoric Kingdom_Data/sharedassets1.assets (path id 14, MainMenuBackground); never writes into the game.
Needs UnityPy (pip install UnityPy).
"""
import pathlib
import sys

import UnityPy

PATH_ID = 14
OUT = pathlib.Path(__file__).resolve().parents[2] / "studio" / "static" / "hero.jpg"


def main() -> None:
    game = pathlib.Path(sys.argv[1])
    env = UnityPy.load(str(game / "Prehistoric Kingdom_Data" / "sharedassets1.assets"))
    obj = next(o for o in env.objects if o.path_id == PATH_ID)
    data = obj.read()
    if data.m_Name != "MainMenuBackground":
        raise SystemExit(f"path id {PATH_ID} is {data.m_Name!r}, not MainMenuBackground (game updated?)")
    OUT.parent.mkdir(parents=True, exist_ok=True)
    data.image.convert("RGB").save(OUT, "JPEG", quality=85, optimize=True)
    print(f"wrote {OUT} ({data.image.width}x{data.image.height})")


if __name__ == "__main__":
    main()
