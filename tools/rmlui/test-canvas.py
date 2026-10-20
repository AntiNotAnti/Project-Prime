#!/usr/bin/env python3
"""Invented canvas ABI1 fixtures using only the CPU draw-list backend.

Usage: test-canvas.py NATIVE_LIBRARY RUNTIME_RMLUI_ASSETS
The temporary asset tree contains newly authored RML and three openly licensed
runtime fonts. No game source, original media, GL context or screenshots are used.
"""
from __future__ import annotations

import argparse
import ctypes as C
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import threading

U32, U64, I32, F32 = C.c_uint32, C.c_uint64, C.c_int32, C.c_float
NONE = 0xffffffff


class Lease(C.Structure):
    _fields_ = [("size", U32), ("version", U32), ("generation", U64), ("document_id", U64), ("epoch", U64)]


class Descriptor(C.Structure):
    _fields_ = [(name, U32) for name in ("size", "version", "width", "height", "offset", "length", "filter", "wrap_s", "wrap_t", "reserved")]


class Vertex(C.Structure):
    _fields_ = [("x", F32), ("y", F32), ("r", C.c_uint8), ("g", C.c_uint8), ("b", C.c_uint8), ("a", C.c_uint8), ("u", F32), ("v", F32)]


class Batch(C.Structure):
    _fields_ = [(name, U32) for name in ("texture", "first", "count", "reserved")]


class Header(C.Structure):
    _fields_ = [("size", U32), ("version", U32), ("revision", U64), ("virtual_width", F32), ("virtual_height", F32),
                ("vertex_count", U32), ("index_count", U32), ("batch_count", U32), ("reserved", U32)]


class State(C.Structure):
    _fields_ = [("size", U32), ("version", U32), ("revision", U64), ("texture_count", U32), ("batch_count", U32), ("flags", U32), ("reserved", U32)]


class Sampling(C.Structure):
    _fields_ = [(name, U32) for name in ("size", "version", "filter", "wrap_s", "wrap_t", "flags")]


class Update(C.Structure):
    _fields_ = [("size", U32), ("version", U32), ("generation", U64), ("visual_revision", U64),
                ("next_update_delay_seconds", C.c_double), ("flags", U32), ("reserved", U32)]


class DrawCommand(C.Structure):
    _fields_ = [("size", U32), ("kind", U32), ("geometry", U64), ("texture", U64), ("translation_x", F32), ("translation_y", F32),
                ("x", I32), ("y", I32), ("width", I32), ("height", I32), ("enabled", U32), ("operation", U32), ("transform", F32 * 16)]


class DrawGeometry(C.Structure):
    _fields_ = [("size", U32), ("vertex_count", U32), ("index_count", U32), ("reserved", U32),
                ("vertices", C.POINTER(Vertex)), ("indices", C.POINTER(I32))]


class DrawTexture(C.Structure):
    _fields_ = [("size", U32), ("width", U32), ("height", U32), ("reserved", U32), ("handle", U64), ("pixels", C.POINTER(C.c_uint8))]


def sized(kind):
    value = kind()
    value.size = C.sizeof(kind)
    if hasattr(value, "version"):
        value.version = 1
    return value


def clone(value):
    return type(value).from_buffer_copy(value)


class Harness:
    def __init__(self, library):
        self.lib = C.CDLL(str(library))
        self.checks = 0
        signatures = {
            "canvas_version": (U32, []), "generation": (U64, []), "home_document": (U64, []),
            "initialize_backend": (C.c_int, [C.c_int, C.c_int, F32, C.c_char_p, C.c_int]),
            "shutdown": (None, []), "update": (None, []), "render": (None, [C.c_int, C.c_int]),
            "update_status": (C.c_int, [C.POINTER(Update)]),
            "document_open": (U64, [C.c_char_p, C.c_int]), "document_close": (C.c_int, [U64]),
            "document_show": (C.c_int, [U64, C.c_int]), "document_set_bool": (C.c_int, [U64, C.c_char_p, C.c_int]),
            "document_set_text": (C.c_int, [U64, C.c_char_p, C.c_char_p]),
            "canvas_acquire": (C.c_int, [U64, U64, C.c_char_p, C.POINTER(Lease)]),
            "canvas_textures": (C.c_int, [C.POINTER(Lease), C.POINTER(Descriptor), U32, C.POINTER(C.c_uint8), U32]),
            "canvas_frame": (C.c_int, [C.POINTER(Lease), C.POINTER(Header), C.POINTER(Vertex), C.POINTER(I32), C.POINTER(Batch)]),
            "canvas_release": (C.c_int, [C.POINTER(Lease)]), "canvas_state": (C.c_int, [C.POINTER(Lease), C.POINTER(State)]),
            "draw_texture_sampling": (C.c_int, [U64, U64, C.POINTER(Sampling)]),
            "draw_command_count": (C.c_int, []), "draw_command": (C.c_int, [C.c_int, C.POINTER(DrawCommand)]),
            "draw_geometry": (C.c_int, [U64, C.POINTER(DrawGeometry)]), "draw_texture_count": (C.c_int, []),
            "draw_texture": (C.c_int, [C.c_int, C.POINTER(DrawTexture)]),
        }
        for name, (result, arguments) in signatures.items():
            function = getattr(self.lib, "pp_rmlui_" + name)
            function.restype, function.argtypes = result, arguments
            setattr(self, name, function)

    def check(self, condition, label):
        self.checks += 1
        if not condition:
            raise AssertionError(label)

    def acquire(self, document, element=b"canvas"):
        lease = sized(Lease)
        self.check(self.canvas_acquire(self.generation(), document, element, C.byref(lease)) == 1, "acquire visible foreground canvas")
        self.check(lease.generation == self.generation() and lease.document_id == document and lease.epoch > 0, "lease captures exact generation/document and nonzero epoch")
        return lease

    def state(self, lease):
        value = sized(State)
        self.check(self.canvas_state(C.byref(lease), C.byref(value)) == 1, "read current canvas state")
        self.check(value.reserved == 0 and value.flags & ~3 == 0, "state reserved/flags remain closed ABI")
        return value

    def scheduling(self):
        status = sized(Update)
        self.check(self.update_status(C.byref(status)) == 1, "read scheduling state")
        return status.generation, status.visual_revision, status.flags

    def picture(self):
        self.update()
        self.render(640, 480)

    def rejected(self, lease, operation, label):
        # Canvas foreground admission legitimately settles a prior successful
        # owner mutation's pending layout. Start from its completed picture so
        # that revision/dirty changes can be attributed to this rejected packet.
        self.picture()
        before = bytes(self.state(lease))
        schedule = self.scheduling()
        self.check(operation() == 0, label)
        self.check(bytes(self.state(lease)) == before, label + ": prior revision/table/frame remains atomic")
        self.check(self.scheduling() == schedule, label + ": no dirty/visual revision publication")

    def table(self, lease, descriptors, pixels):
        array = (Descriptor * len(descriptors))(*descriptors) if descriptors else None
        payload = (C.c_uint8 * len(pixels))(*pixels) if pixels else None
        return self.canvas_textures(C.byref(lease), array, len(descriptors), payload, len(pixels))

    def frame(self, lease, header, vertices, indices, batches):
        return self.canvas_frame(C.byref(lease), C.byref(header), vertices, indices, batches)

    def draw_copy(self):
        # Every pointer is copied before another native mutation. No pointer or
        # native renderer handle is retained beyond this local capture.
        commands = []
        count = self.draw_command_count()
        self.check(0 <= count <= 20000, "bounded native command inventory")
        for index in range(count):
            command = sized(DrawCommand)
            self.check(self.draw_command(index, C.byref(command)) == 1, "copy draw command")
            if command.kind != 1:
                continue
            geometry = sized(DrawGeometry)
            self.check(self.draw_geometry(command.geometry, C.byref(geometry)) == 1, "copy command geometry")
            self.check(geometry.vertex_count <= 65536 and geometry.index_count <= 196608, "bounded geometry pointer extent")
            vertices = [clone(geometry.vertices[i]) for i in range(geometry.vertex_count)]
            indices = [geometry.indices[i] for i in range(geometry.index_count)]
            commands.append((clone(command), vertices, indices))
        textures = {}
        for index in range(self.draw_texture_count()):
            texture = sized(DrawTexture)
            self.check(self.draw_texture(index, C.byref(texture)) == 1, "copy renderer texture")
            self.check(0 < texture.width <= 4096 and 0 < texture.height <= 4096, "bounded texture copy extent")
            sampling = sized(Sampling)
            self.check(self.draw_texture_sampling(self.generation(), texture.handle, C.byref(sampling)) == 1, "query captured texture sampling")
            textures[texture.handle] = (texture.width, texture.height, C.string_at(texture.pixels, texture.width * texture.height * 4), clone(sampling))
        return commands, textures


def descriptor(width=1, height=1, offset=0, filter=0, wrap_s=0, wrap_t=0):
    value = sized(Descriptor)
    value.width, value.height, value.offset, value.length = width, height, offset, width * height * 4
    value.filter, value.wrap_s, value.wrap_t = filter, wrap_s, wrap_t
    return value


def triangle_frame(revision=1):
    header = sized(Header)
    header.revision, header.virtual_width, header.virtual_height = revision, 320, 240
    header.vertex_count, header.index_count, header.batch_count = 9, 9, 3
    vertices = (Vertex * 9)(*[Vertex(x, y, 200, 100, 50, 128, u, v)
        for offset in (0, 20, 40) for x, y, u, v in ((offset, 0, -.5, 0), (offset + 16, 0, 2, 0), (offset, 16, 0, 1))])
    indices = (I32 * 9)(*range(9))
    batches = (Batch * 3)(Batch(0, 0, 3, 0), Batch(1, 3, 3, 0), Batch(NONE, 6, 3, 0))
    return header, vertices, indices, batches


def texture_validation(h, document):
    lease = h.acquire(document)
    h.picture()
    h.check(h.state(lease).flags == 0, "new epoch has no implicit table")
    for field, values in {
        "size": (0, 39, 41), "version": (0, 2), "width": (0, 513, NONE), "height": (0, 513, NONE),
        "length": (0, 3, 5, NONE), "offset": (1, 4, NONE), "filter": (2, NONE), "wrap_s": (2, NONE),
        "wrap_t": (2, NONE), "reserved": (1, NONE),
    }.items():
        for value in values:
            bad = descriptor()
            setattr(bad, field, value)
            h.rejected(lease, lambda: h.table(lease, [bad], [1, 2, 3, 4]), f"reject descriptor {field}={value}")
    for pixels in ([], [1, 2, 3], [1, 2, 3, 4, 5]):
        h.rejected(lease, lambda: h.table(lease, [descriptor()], pixels), "reject nonexact texture payload length")
    for second in (descriptor(offset=0), descriptor(offset=8), descriptor(offset=NONE)):
        h.rejected(lease, lambda: h.table(lease, [descriptor(), second], range(8)), "reject overlapping/gapped/overflowing packed offsets")
    h.rejected(lease, lambda: h.canvas_textures(C.byref(lease), None, 65, None, 0), "reject65texture declaration before pointer traversal")
    h.rejected(lease, lambda: h.canvas_textures(C.byref(lease), None, 1, None, 4), "reject missing descriptor/pixel pointers")
    h.rejected(lease, lambda: h.canvas_textures(C.byref(lease), None, 0, None, 4 * 1024 * 1024 + 1), "reject excessive payload before reads")
    # Two independent sampling states and byte-exact straight-alpha oracles.
    descriptors = [descriptor(filter=0, wrap_s=1), descriptor(2, 1, 4, filter=1, wrap_t=1)]
    pixels = [200, 100, 50, 128, 255, 127, 63, 64, 31, 255, 7, 0]
    supplied_descriptors = (Descriptor * 2)(*descriptors)
    supplied_pixels = (C.c_uint8 * len(pixels))(*pixels)
    h.check(h.canvas_textures(C.byref(lease), supplied_descriptors, 2, supplied_pixels, len(pixels)) == 1,
            "publish complete packed texture table once")
    supplied_descriptors[0].width = 512
    supplied_pixels[0] = 1
    state = h.state(lease)
    h.check(state.texture_count == 2 and state.flags == 2, "table-set flag/count exact without texture failure")
    h.rejected(lease, lambda: h.table(lease, descriptors, pixels), "same epoch table cannot be replaced")
    return lease


def frame_validation(h, lease):
    header, vertices, indices, batches = triangle_frame()
    h.check(h.frame(lease, header, vertices, indices, batches) == 1, "publish valid textured/untextured frame")
    vertices[0].r, vertices[0].x = 0, 999
    indices[0] = 8
    batches[0].texture = 1
    h.picture()
    h.check(h.state(lease).revision == 1 and h.state(lease).batch_count == 3, "successful revision and batch count retained")
    for field, values in {
        "size": (0, 39, 41), "version": (0, 2), "revision": (0, 1), "virtual_width": (0, .5, -.5, float("nan"), float("inf"), 16385),
        "virtual_height": (0, .5, -1, float("nan"), -float("inf"), 16385), "vertex_count": (65537,),
        "index_count": (196609, 8), "batch_count": (4097,), "reserved": (1,),
    }.items():
        for value in values:
            bad = clone(header)
            bad.revision = 2
            setattr(bad, field, value)
            h.rejected(lease, lambda: h.frame(lease, bad, vertices, indices, batches), f"reject frame {field}={value}")
    for field in ("x", "y", "u", "v"):
        for value in (float("nan"), float("inf"), -float("inf"), 1048577, -1048577):
            bad_vertices = (Vertex * 9).from_buffer_copy(vertices)
            setattr(bad_vertices[0], field, value)
            candidate = clone(header)
            candidate.revision = 2
            h.rejected(lease, lambda: h.frame(lease, candidate, bad_vertices, indices, batches), f"reject nonfinite/out-of-domain vertex {field}")
    for invalid in (-1, 9, 2147483647):
        bad_indices = (I32 * 9)(*range(9))
        bad_indices[8] = invalid
        candidate = clone(header)
        candidate.revision = 2
        h.rejected(lease, lambda: h.frame(lease, candidate, vertices, bad_indices, batches), "reject out-of-range signed index")
    for field, values in {"texture": (2, NONE - 1), "first": (1, 4, NONE), "count": (0, 2, 4, 12, NONE), "reserved": (1,)}.items():
        for value in values:
            bad_batches = (Batch * 3).from_buffer_copy(batches)
            setattr(bad_batches[0], field, value)
            candidate = clone(header)
            candidate.revision = 2
            h.rejected(lease, lambda: h.frame(lease, candidate, vertices, indices, bad_batches), "reject invalid texture or noncontiguous batch coverage")
    candidate = clone(header)
    candidate.revision = 2
    for args in ((None, indices, batches), (vertices, None, batches), (vertices, indices, None)):
        h.rejected(lease, lambda: h.frame(lease, candidate, *args), "reject missing nonempty frame pointer")
    commands, textures = h.draw_copy()
    explicit = {handle: value for handle, value in textures.items() if value[3].flags == 1}
    h.check(len(explicit) == 2, "exact two canvas textures have explicit sampling metadata")
    one = next(value for value in explicit.values() if value[:2] == (1, 1))
    two = next(value for value in explicit.values() if value[:2] == (2, 1))
    h.check(one[2] == bytes([100, 50, 25, 128]), "straight textureRGBA premultiplies floor once")
    h.check(two[2] == bytes([64, 31, 15, 64, 0, 0, 0, 0]), "partial and zero alpha texture channels premultiply exactly")
    h.check((one[3].filter, one[3].wrap_s, one[3].wrap_t) == (0, 1, 0)
            and (two[3].filter, two[3].wrap_s, two[3].wrap_t) == (1, 0, 1), "each texture owns independent nearest/linear and repeat/clamp axes")
    canvas = [(command, verts, inds) for command, verts, inds in commands if command.texture in explicit or
              command.texture == 0 and any(v.a == 128 for v in verts)]
    h.check(len(canvas) >= 3, "textured and untextured batches reach actual CPU native draw list")
    for _, verts, inds in canvas:
        h.check(all((v.r, v.g, v.b, v.a) == (100, 50, 25, 128) for v in verts), "straight vertexRGBA premultiplies once independent of texture")
        h.check(all(0 <= i < len(verts) for i in inds), "copied native emitted indices remain valid")
    first_handle = next(handle for handle, value in explicit.items() if value[:2] == (1, 1))
    first = next(verts for command, verts, _ in canvas if command.texture == first_handle)
    h.check(len(first) == 3 and [(v.x, v.y) for v in first] == [(0, 0), (16, 0), (0, 16)],
            "frame copies caller vertices/indices/batches and maps invented320x240 virtual space exactly once")
    h.check([(v.u, v.v) for v in first] == [(-.5, 0), (2, 0), (0, 1)], "native canvas preserves signed/repeating UV without clamping geometry")
    legacy = [value for value in textures.values() if value[3].flags == 0]
    h.check(bool(legacy), "ordinary authored text still produces a legacy-sampling texture")
    for handle in explicit:
        sampling = sized(Sampling)
        h.check(h.draw_texture_sampling(h.generation() + 1, handle, C.byref(sampling)) == 0, "sampling rejects stale generation")
    # Subsequent captures cannot accumulate another alpha multiplication.
    h.picture()
    _, repeated = h.draw_copy()
    h.check(all(repeated[key][2] == textures[key][2] for key in explicit), "render reuse never premultiplies stored canvas pixels again")
    clear = sized(Header)
    clear.revision, clear.virtual_width, clear.virtual_height = 2, 320, 240
    h.check(h.frame(lease, clear, None, None, None) == 1, "all-zero counts explicitly clear a frame")
    h.check(h.state(lease).revision == 2 and h.state(lease).batch_count == 0, "clear advances only current revision and drops batches")


def capacity_boundaries(h, home):
    lease = h.acquire(home)
    descriptors = [descriptor(offset=index * 4) for index in range(64)]
    h.check(h.table(lease, descriptors, bytes(256)) == 1 and h.state(lease).texture_count == 64,
            "exact64-image table is admitted rather than arbitrary lower truncation")
    h.check(h.canvas_release(C.byref(lease)) == 1, "release64-image callback table before next capacity fixture")
    lease = h.acquire(home)
    descriptors = (Descriptor * 4)(*[descriptor(512, 512, index * 1048576) for index in range(4)])
    pixels = bytearray(4 * 1024 * 1024)
    pointer = (C.c_uint8 * len(pixels)).from_buffer(pixels)
    h.check(h.canvas_textures(C.byref(lease), descriptors, 4, pointer, len(pixels)) == 1,
            "exact4MiB packed table is admitted with512pixel dimension boundaries")
    h.check(h.canvas_release(C.byref(lease)) == 1, "release exact-boundary source copies without renderer uploads")


def lifecycle(h, home):
    for field, value in (("size", 0), ("size", 31), ("version", 0), ("version", 2)):
        bad = sized(Lease)
        setattr(bad, field, value)
        h.check(h.canvas_acquire(h.generation(), home, b"canvas", C.byref(bad)) == 0, "acquire rejects invalid output ABI header")
    for element in (b"", b"missing", b"plain", b"x" * 257):
        h.check(h.canvas_acquire(h.generation(), home, element, C.byref(sized(Lease))) == 0, "acquire rejects absent/noncanvas/overlong element identity")
    old = h.acquire(home)
    replacement = h.acquire(home)
    h.check(replacement.epoch != old.epoch, "replacement acquire creates a distinct epoch")
    h.check(h.canvas_state(C.byref(old), C.byref(sized(State))) == 0, "replaced epoch cannot read current state")
    h.check(h.canvas_release(C.byref(old)) == 1 and h.canvas_release(C.byref(old)) == 1, "wellformed current-generation superseded release is idempotent")
    h.check(h.state(replacement).revision == 0, "superseded release cannot retire replacement")
    for kind in (State,):
        for field, value in (("size", 0), ("size", 31), ("version", 0), ("version", 2)):
            bad = sized(kind)
            setattr(bad, field, value)
            h.check(h.canvas_state(C.byref(replacement), C.byref(bad)) == 0, "state refuses malformed output header without replacing lease")
    h.check(h.table(replacement, [], []) == 1, "empty texture table explicitly commits once")
    empty = sized(Header)
    empty.revision, empty.virtual_width, empty.virtual_height = 1, 320, 240
    h.check(h.frame(replacement, empty, None, None, None) == 1, "empty-table canvas accepts explicit empty frame")
    # Finite domain boundaries are inclusive; this avoids an implementation
    # accepting only NaN-free values while overflowing its virtual transform.
    for field in ("generation", "document_id", "epoch", "version", "size"):
        stale = clone(replacement)
        setattr(stale, field, getattr(stale, field) + 1)
        h.check(h.canvas_state(C.byref(stale), C.byref(sized(State))) == 0, "state rejects altered lease " + field)
        h.check(h.table(stale, [], []) == 0 and h.frame(stale, empty, None, None, None) == 0, "publication rejects altered lease " + field)
    errors = []
    def foreign():
        for call in (lambda: h.canvas_acquire(h.generation(), home, b"canvas", C.byref(sized(Lease))),
                     lambda: h.canvas_state(C.byref(replacement), C.byref(sized(State))),
                     lambda: h.table(replacement, [], []), lambda: h.frame(replacement, empty, None, None, None),
                     lambda: h.canvas_release(C.byref(replacement))):
            errors.append(call())
    worker = threading.Thread(target=foreign)
    worker.start()
    worker.join(5)
    h.check(not worker.is_alive() and errors == [0] * 5, "every lease operation rejects a foreign thread without owner mutation")
    h.check(h.document_set_bool(home, b"visible:canvas", 0) == 1, "hide canvas element through existing document owner")
    h.update()
    hidden = sized(Lease)
    h.check(h.canvas_acquire(h.generation(), home, b"canvas", C.byref(hidden)) == 0, "hidden element cannot acquire")
    empty.revision = 2
    h.check(h.frame(replacement, empty, None, None, None) == 0, "hidden element cannot publish")
    h.check(h.state(replacement).revision == 1, "hidden live lease can inspect its retained revision")
    h.check(h.canvas_release(C.byref(replacement)) == 1, "hidden element can release owned resources")
    h.check(h.document_set_bool(home, b"visible:canvas", 1) == 1, "restore fixture canvas visibility")
    h.update()
    home_lease = h.acquire(home)
    h.check(h.table(home_lease, [], []) == 1, "prepare otherwise valid home table before foreground predicate test")
    modal = h.document_open(b"modal.rml", 1)
    h.check(modal != 0, "open invented modal document")
    h.update()
    h.check(h.canvas_acquire(h.generation(), home, b"canvas", C.byref(sized(Lease))) == 0, "background document cannot acquire behind modal foreground")
    h.check(h.frame(home_lease, empty, None, None, None) == 0, "background document cannot publish behind foreground modal")
    modal_lease = h.acquire(modal)
    h.check(h.document_show(modal, 0) == 1 and h.canvas_release(C.byref(modal_lease)) == 1, "hidden document still permits exact owner release")
    h.check(h.document_close(modal) == 1 and h.canvas_release(C.byref(modal_lease)) == 1, "closed-document current-generation release remains harmless idempotent")
    h.check(h.canvas_state(C.byref(modal_lease), C.byref(sized(State))) == 0, "closed document can never publish/read old epoch")
    h.check(h.canvas_release(C.byref(home_lease)) == 1, "release background lease without foreground authority")
    # Every live slot is visible; max8 is global, not per document/element.
    slots = [h.acquire(home, b"canvas" if i == 0 else f"canvas{i}".encode()) for i in range(8)]
    h.check(h.canvas_acquire(h.generation(), home, b"canvas8", C.byref(sized(Lease))) == 0, "global registry refuses ninth live lease")
    h.check(h.canvas_release(C.byref(slots[4])) == 1, "releasing one lease frees a bounded registry slot")
    ninth = h.acquire(home, b"canvas8")
    for lease in slots + [ninth]:
        h.check(h.canvas_release(C.byref(lease)) == 1, "registry release requires no tombstone allocation")
    # Existing SetInnerRML path retires nested canvas before detach. A secondary
    # document is used because home ordinary text names bind its menu model.
    nested = h.document_open(b"nested.rml", 1)
    h.check(nested != 0, "open invented nested canvas document")
    nested_lease = h.acquire(nested)
    h.check(h.table(nested_lease, [], []) == 1, "prepare otherwise valid nested table before ancestor visibility test")
    h.check(h.document_set_bool(nested, b"visible:container", 0) == 1, "hide canvas ancestor through existing document owner")
    h.update()
    h.check(h.canvas_acquire(h.generation(), nested, b"canvas", C.byref(sized(Lease))) == 0
            and h.frame(nested_lease, empty, None, None, None) == 0, "hidden ancestor fences acquire and otherwise valid frame publication")
    h.check(h.canvas_release(C.byref(nested_lease)) == 1, "release while ancestor remains hidden")
    h.check(h.document_set_bool(nested, b"visible:container", 1) == 1, "restore own ancestor before structural replacement")
    h.update()
    nested_lease = h.acquire(nested)
    h.check(h.document_set_text(nested, b"container", b"own replacement text") == 1, "existing text mutation replaces canvas subtree")
    h.check(h.canvas_state(C.byref(nested_lease), C.byref(sized(State))) == 0, "text subtree replacement retires old canvas epoch")
    h.check(h.canvas_release(C.byref(nested_lease)) == 1 and h.document_close(nested) == 1, "detached subtree release is harmless")
    return h.acquire(home)


def write_assets(root, runtime_assets):
    fonts = root / "fonts"
    fonts.mkdir()
    for name in ("Rajdhani-SemiBold.ttf", "Rajdhani-Bold.ttf", "JetBrainsMono-Regular.ttf"):
        source = runtime_assets / "fonts" / name
        if not source.is_file() or source.is_symlink():
            raise ValueError("required regular runtime font missing: " + name)
        shutil.copyfile(source, fonts / name)
    style = "body{font-family:Rajdhani;font-size:16px;margin:0px;color:white;} prime-canvas{display:block;position:absolute;width:320px;height:240px;left:0px;top:0px;}"
    def document(body):
        return "<rml><head><title>Invented canvas ABI fixture</title><style>" + style + "</style></head><body>" + body + "</body></rml>"
    canvases = "".join(f'<prime-canvas id="canvas{i if i else ""}"></prime-canvas>' for i in range(9))
    (root / "prime_home.rml").write_text(document(canvases + '<div id="plain" style="position:absolute;top:250px;">OWN FIXTURE</div>'))
    (root / "modal.rml").write_text(document('<prime-canvas id="canvas"></prime-canvas>'))
    (root / "nested.rml").write_text(document('<div id="container"><prime-canvas id="canvas"></prime-canvas></div>'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("library", type=Path)
    parser.add_argument("runtime_assets", type=Path)
    args = parser.parse_args()
    for kind, expected in ((Lease, 32), (Descriptor, 40), (Vertex, 20), (Batch, 16), (Header, 40), (State, 32), (Sampling, 24), (Update, 40), (DrawCommand, 120)):
        if C.sizeof(kind) != expected:
            raise RuntimeError("host ctypes ABI layout mismatch: " + kind.__name__)
    library = args.library.resolve(strict=True)
    before = hashlib.sha256(library.read_bytes()).hexdigest()
    h = Harness(library)
    with tempfile.TemporaryDirectory(prefix="prime-canvas-own-fixtures-") as temporary:
        assets = Path(temporary)
        write_assets(assets, args.runtime_assets.resolve(strict=True))
        h.check(h.canvas_version() == 1, "independent canvas ABI version1")
        try:
            h.check(h.initialize_backend(640, 480, 1, str(assets).encode(), 1) == 1, "initialize CPU draw-list backend1 with invented asset tree")
            home = h.home_document()
            h.check(home != 0, "bootstrap fixture document has opaque nonzero ID")
            lease = texture_validation(h, home)
            frame_validation(h, lease)
            h.check(h.canvas_release(C.byref(lease)) == 1, "release first validation lease")
            capacity_boundaries(h, home)
            final = lifecycle(h, home)
            generation = h.generation()
            h.shutdown()
            h.check(h.generation() != generation and h.canvas_state(C.byref(final), C.byref(sized(State))) == 0,
                    "shutdown invalidates current generation and all live epochs")
            h.check(h.canvas_release(C.byref(final)) == 0, "shutdown stale-generation release rejects")
            h.check(h.initialize_backend(640, 480, 1, str(assets).encode(), 1) == 1, "fresh CPU runtime can initialize after teardown")
            h.check(h.canvas_acquire(generation, home, b"canvas", C.byref(sized(Lease))) == 0, "old generation/document cannot target new runtime")
        finally:
            h.shutdown()
    after = hashlib.sha256(library.read_bytes()).hexdigest()
    h.check(before == after, "native library bytes unchanged during CPU fixtures")
    print(json.dumps({"result": "PASS", "checks": h.checks, "canvas_abi": 1, "backend": 1, "library_sha256": before,
                      "temporary_assets_removed": True, "scope": "invented CPU ABI fixtures; no GL, source media or original credits presentation"}, sort_keys=True))


if __name__ == "__main__":
    main()
