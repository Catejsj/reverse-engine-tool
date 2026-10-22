"""List every asset path inside an Unreal IoStore container (.utoc). Stdlib only, read-only.

Stage 2 — extract (doctrine D2): names first, contents never. The object dump only shows what is
*loaded* at the moment of the dump (S1 found 15 Niagara systems, none of them dust); this lists
everything the game ships, so a mod can find an effect, animation or sound by name.

Layout read here (toc versions 2-3, UE 4.26-4.27). Header is 144 bytes, then:
    chunk ids            entry_count x 12
    offsets + lengths    entry_count x 10
    compression blocks   block_count x block_entry_size
    compression names    name_count x name_length
    directory index      directory_index_size      (only if the container is Indexed, not Encrypted)
The directory index is three arrays: directories (name, first child, next sibling, first file),
files (name, next file, user data) and a string table. Names are indexes into the string table;
0xFFFFFFFF means "none".
"""

from __future__ import annotations

import struct
from pathlib import Path

MAGIC = b"-==--==--==--==-"
HEADER = 144
NONE = 0xFFFFFFFF
FLAG_ENCRYPTED, FLAG_SIGNED, FLAG_INDEXED = 2, 4, 8


class Reader:
    def __init__(self, data: bytes) -> None:
        self.data, self.pos = data, 0

    def u32(self) -> int:
        v = struct.unpack_from("<I", self.data, self.pos)[0]
        self.pos += 4
        return v

    def i32(self) -> int:
        v = struct.unpack_from("<i", self.data, self.pos)[0]
        self.pos += 4
        return v

    def fstring(self) -> str:
        """Unreal FString: int32 length incl. terminator; negative means UTF-16."""
        n = self.i32()
        if n == 0:
            return ""
        if n > 0:
            raw = self.data[self.pos:self.pos + n]
            self.pos += n
            return raw[:-1].decode("latin-1")
        raw = self.data[self.pos:self.pos + (-n) * 2]
        self.pos += (-n) * 2
        return raw[:-2].decode("utf-16-le")


def directory_index(path: Path) -> bytes:
    """The raw directory index bytes, or ValueError explaining why it cannot be read."""
    with path.open("rb") as f:
        head = f.read(HEADER)
        if head[:16] != MAGIC:
            raise ValueError(f"{path.name}: not a .utoc")
        version = head[16]
        if version not in (2, 3):
            raise ValueError(f"{path.name}: toc version {version} not supported (2-3 only)")
        header_size, entries, blocks, block_size, n_names, name_len, _, dir_size = \
            struct.unpack_from("<8I", head, 20)
        flags = head[80]
        if flags & FLAG_ENCRYPTED:
            raise ValueError(f"{path.name}: encrypted. RET does not decrypt (brief: no circumvention)")
        if not flags & FLAG_INDEXED or dir_size == 0:
            return b""
        if flags & FLAG_SIGNED:
            raise ValueError(f"{path.name}: signed containers not supported yet")
        f.seek(header_size + entries * 12 + entries * 10 + blocks * block_size + n_names * name_len)
        return f.read(dir_size)


def parse_index(raw: bytes) -> list[str]:
    """Every file path in a directory index, mount point included, sorted."""
    if not raw:
        return []
    r = Reader(raw)
    mount = r.fstring()
    dirs = [(r.u32(), r.u32(), r.u32(), r.u32()) for _ in range(r.u32())]
    files = [(r.u32(), r.u32(), r.u32()) for _ in range(r.u32())]
    strings = [r.fstring() for _ in range(r.u32())]

    def name(i: int) -> str:
        return "" if i == NONE else strings[i]

    out: list[str] = []
    stack = [(0, mount.rstrip("/"))] if dirs else []
    seen: set[int] = set()
    while stack:
        d, prefix = stack.pop()
        if d == NONE or d in seen:
            continue
        seen.add(d)
        d_name, child, sibling, file = dirs[d]
        here = f"{prefix}/{name(d_name)}" if d_name != NONE else prefix
        while file != NONE:
            f_name, file, _ = files[file]
            out.append(f"{here}/{name(f_name)}")
        if sibling != NONE:
            stack.append((sibling, prefix))
        if child != NONE:
            stack.append((child, here))
    return sorted(out)


def list_assets(paks: Path) -> dict[str, list[str]]:
    """{container name: [paths]} for every readable .utoc in a Paks folder."""
    result: dict[str, list[str]] = {}
    for toc in sorted(paks.glob("*.utoc")):
        result[toc.name] = parse_index(directory_index(toc))
    return result


def game_path(container_path: str) -> str:
    """`../../../SH3RE/Content/X/Y.uasset` -> `/Game/X/Y` (the path UE4SS LoadAsset / StaticFindObject take).
    Engine content maps to `/Engine/...`. Plugin content keeps its mount name. Non-asset files keep their extension."""
    p = container_path.lstrip("./")
    parts = p.split("/")
    if len(parts) > 2 and parts[1] == "Content":
        root = "/Engine" if parts[0] == "Engine" else "/Game"
        p = root + "/" + "/".join(parts[2:])
    elif "/Content/" in p:                       # plugin: <...>/Plugins/<Name>/Content/X
        name = p.split("/Content/")[0].split("/")[-1]
        p = f"/{name}/" + p.split("/Content/", 1)[1]
    else:
        p = "/" + p
    return p[: -len(".uasset")] if p.endswith(".uasset") else p
