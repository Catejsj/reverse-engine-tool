"""Read a Unity IL2CPP global-metadata.dat (version 31, Unity 2022.3): assemblies, types, fields, methods.

Why: Root's game logic is compiled to native code (GameAssembly.dll), but IL2CPP keeps every class, field and method
NAME in this metadata file. Reading it tells us how Root models a faction before we touch any code (doctrine D1:
understanding before breakthrough). Read-only, stdlib only.

Layout (the same tables Il2CppDumper reads, sizes for v31):
  header: magic 0xFAB11BAF, version, then (offset, size) pairs; the ones used here are
          2 strings, 5 methods, 11 fields, 19 type definitions, 20 images
  type definition 88 bytes (Root/Unity 2022.3 keeps elementTypeIndex, measured: record 1 starts 88 bytes in),
  method 36, field 12, image 40. Names are offsets into the string table (NUL-terminated).
The record sizes are checked against the table sizes when loading, so a different version fails loudly.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from pathlib import Path

MAGIC = 0xFAB11BAF
TYPE_SIZE, METHOD_SIZE, FIELD_SIZE, IMAGE_SIZE = 88, 36, 12, 40
T_STRINGS, T_METHODS, T_FIELDS, T_TYPES, T_IMAGES = 2, 5, 11, 19, 20


@dataclass
class TypeDef:
    index: int
    namespace: str
    name: str
    image: str
    parent: int                 # type index (Il2CppType table, not resolved here)
    declaring: int
    flags: int
    fields: list[str] = field(default_factory=list)
    methods: list[str] = field(default_factory=list)

    @property
    def full_name(self) -> str:
        return f"{self.namespace}.{self.name}" if self.namespace else self.name


class Metadata:
    def __init__(self, path: Path) -> None:
        self.data = path.read_bytes()
        magic, self.version = struct.unpack_from("<Ii", self.data, 0)
        if magic != MAGIC:
            raise ValueError(f"{path}: not IL2CPP metadata (magic {magic:#x})")
        if self.version != 31:
            raise ValueError(f"{path}: metadata version {self.version}, this reader knows 31")
        self.tables = [struct.unpack_from("<II", self.data, 8 + 8 * i) for i in range(24)]
        for t, size in ((T_TYPES, TYPE_SIZE), (T_METHODS, METHOD_SIZE), (T_FIELDS, FIELD_SIZE), (T_IMAGES, IMAGE_SIZE)):
            if self.tables[t][1] % size:
                raise ValueError(f"table {t} size {self.tables[t][1]} is not a multiple of {size}: layout differs")

    def string(self, index: int) -> str:
        start = self.tables[T_STRINGS][0] + index
        end = self.data.index(0, start)
        return self.data[start:end].decode("utf-8", "replace")

    def _records(self, table: int, size: int):
        off, total = self.tables[table]
        for i in range(total // size):
            yield i, off + i * size

    def images(self) -> list[tuple[str, int, int]]:
        """(assembly file name, first type index, type count)."""
        out = []
        for _, o in self._records(T_IMAGES, IMAGE_SIZE):
            name, start, count = (struct.unpack_from("<IiiI", self.data, o)[k] for k in (0, 2, 3))
            out.append((self.string(name), start, count))
        return out

    def types(self) -> list[TypeDef]:
        owner = {}
        for name, start, count in self.images():
            for i in range(start, start + count):
                owner[i] = name
        f_off = self.tables[T_FIELDS][0]
        m_off = self.tables[T_METHODS][0]
        out = []
        for i, o in self._records(T_TYPES, TYPE_SIZE):
            # name, namespace, byval type, declaring, parent, element type, generic container, flags, field start,
            # method start; then event/property/nested/interface/vtable/interface-offset starts; counts at +64.
            name, ns, declaring, parent, flags, field_start, method_start = \
                (struct.unpack_from("<IIiiiiiIii", self.data, o)[k] for k in (0, 1, 3, 4, 7, 8, 9))
            method_count, field_count = (struct.unpack_from("<HHH", self.data, o + 64)[k] for k in (0, 2))
            t = TypeDef(i, self.string(ns), self.string(name), owner.get(i, "?"), parent, declaring, flags)
            for k in range(field_count):
                t.fields.append(self.string(struct.unpack_from("<I", self.data, f_off + (field_start + k) * FIELD_SIZE)[0]))
            for k in range(method_count):
                t.methods.append(self.string(struct.unpack_from("<I", self.data, m_off + (method_start + k) * METHOD_SIZE)[0]))
            out.append(t)
        return out
