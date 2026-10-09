"""
Compare engine HUD matrices vs editor-computed matrices, element by element.

Walks the live process's HUD element list (via memory probe), then walks a .hud
source file applying the editor's transform composition rules, then reports
mismatches per element. The diff is what tells us exactly which math is still off.

Usage:
    python bf2_matrix_diff.py <path-to-.hud-file>

Run with the game already up and HUD visible. Default .hud is 1playerhud.hud.
"""

import ctypes
import ctypes.wintypes as wt
import re
import struct
import sys
from collections import OrderedDict
from typing import Optional


# ─── Win32 memory probe (mirrors bf2_live_probe.py) ──────────────────────────

PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400
TH32CS_SNAPPROCESS = 0x00000002
LIST_MODULES_ALL = 0x03

kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
psapi = ctypes.WinDLL('psapi', use_last_error=True)


class PROCESSENTRY32(ctypes.Structure):
    _fields_ = [
        ('dwSize', wt.DWORD), ('cntUsage', wt.DWORD), ('th32ProcessID', wt.DWORD),
        ('th32DefaultHeapID', ctypes.c_void_p), ('th32ModuleID', wt.DWORD),
        ('cntThreads', wt.DWORD), ('th32ParentProcessID', wt.DWORD),
        ('pcPriClassBase', wt.LONG), ('dwFlags', wt.DWORD),
        ('szExeFile', ctypes.c_char * 260),
    ]


def find_bf2_pid():
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if not snap or snap == 0xFFFFFFFF:
        return None, None
    try:
        entry = PROCESSENTRY32()
        entry.dwSize = ctypes.sizeof(PROCESSENTRY32)
        if not kernel32.Process32First(snap, ctypes.byref(entry)):
            return None, None
        while True:
            name = entry.szExeFile.decode('latin-1', errors='replace')
            lc = name.lower()
            if (lc in ('bf2_modtools_memext.exe', 'bf2_modtools.exe', 'battlefrontii.exe') or
                'bf2' in lc or 'battlefront' in lc or 'swbf' in lc):
                return entry.th32ProcessID, name
            if not kernel32.Process32Next(snap, ctypes.byref(entry)):
                return None, None
    finally:
        kernel32.CloseHandle(snap)


def get_module_base(handle, exe_name):
    needed = wt.DWORD(0)
    psapi.EnumProcessModulesEx(handle, None, 0, ctypes.byref(needed), LIST_MODULES_ALL)
    count = needed.value // ctypes.sizeof(wt.HMODULE)
    if count == 0:
        return None
    arr = (wt.HMODULE * count)()
    if not psapi.EnumProcessModulesEx(handle, arr, needed.value, ctypes.byref(needed), LIST_MODULES_ALL):
        return None
    name_buf = ctypes.create_string_buffer(260)
    for h in arr:
        if h == 0: continue
        if psapi.GetModuleBaseNameA(handle, h, name_buf, 260) == 0: continue
        if name_buf.value.decode('latin-1', errors='replace').lower() == exe_name.lower():
            return h
    return None


def read_bytes(handle, addr, size):
    buf = (ctypes.c_ubyte * size)()
    nread = ctypes.c_size_t(0)
    if not kernel32.ReadProcessMemory(handle, ctypes.c_void_p(addr), buf, size, ctypes.byref(nread)):
        return None
    return bytes(buf[:nread.value])


# ─── Live engine state ────────────────────────────────────────────────────────

PREFERRED_BASE = 0x00400000
LIST_HEAD = 0x00ad7ee0
NODE_TO_ELEMENT_OFFSET = -0xb4


def probe_engine():
    """Returns (screen_w, screen_h, dict[name] -> 16-float matrix tuple)."""
    pid, exe = find_bf2_pid()
    if pid is None:
        print('ERROR: BF2 process not found. Is the game running?')
        return None
    handle = kernel32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid)
    if not handle:
        print('ERROR: OpenProcess failed (try running as admin).')
        return None
    try:
        base = get_module_base(handle, exe)
        slide = base - PREFERRED_BASE if base else 0

        screen_w = struct.unpack('<i', read_bytes(handle, 0x00e5b508 + slide, 4))[0]
        screen_h = struct.unpack('<i', read_bytes(handle, 0x00e5b50c + slide, 4))[0]
        ui_w = struct.unpack('<f', read_bytes(handle, 0x00ba38fc + slide, 4))[0]

        # Walk linked list
        elements = OrderedDict()
        head = LIST_HEAD + slide
        cur = struct.unpack('<I', read_bytes(handle, head, 4))[0]
        visited = set()
        while cur != head and cur != 0 and cur not in visited and len(elements) < 500:
            visited.add(cur)
            elem_ptr = cur + NODE_TO_ELEMENT_OFFSET
            mt_b = read_bytes(handle, elem_ptr + 0xb0, 4)
            if not mt_b: break
            mt_ptr = struct.unpack('<I', mt_b)[0]
            mat_b = read_bytes(handle, mt_ptr + 0x30, 64)
            if mat_b and len(mat_b) == 64:
                floats = struct.unpack('<16f', mat_b)
                # Find instance name. Restricted to first 0x40 bytes of the element struct
                # to avoid grabbing strings from neighbouring elements in adjacent memory.
                # The instance name pointer is typically at one of a handful of known slots.
                block = read_bytes(handle, elem_ptr, 0x40) or b''
                name = None
                for off in range(0, len(block) - 4, 4):
                    ptr = struct.unpack_from('<I', block, off)[0]
                    if not (0x400000 <= ptr <= 0x40000000): continue
                    s_b = read_bytes(handle, ptr, 64) or b''
                    nul = s_b.find(b'\x00')
                    if nul < 4 or nul > 60: continue
                    s = s_b[:nul].decode('latin-1', errors='replace')
                    if not all(0x20 <= ord(c) < 0x7f for c in s): continue
                    # Only accept strings that look like instance names (alphanumeric + . _),
                    # not arbitrary log strings or paths.
                    if not re.match(r'^[a-zA-Z][a-zA-Z0-9._]*$', s): continue
                    if any(k in s.lower() for k in ('player', 'team', 'reticule', 'health', 'weapon', 'ammo', 'jet', 'tooltip', 'message', 'objective', 'target', 'energy', 'shield', 'spawn', 'map', 'fov', 'powerup', 'lockon', 'hint', 'mapkey', 'flag', 'hero', 'config', 'vehicle')):
                        name = s
                        break
                if name and name not in elements:
                    elements[name] = floats
            nxt = read_bytes(handle, cur, 4)
            if not nxt: break
            cur = struct.unpack('<I', nxt)[0]

        return {
            'screen_w': screen_w, 'screen_h': screen_h, 'ui_w': ui_w,
            'elements': elements,
        }
    finally:
        kernel32.CloseHandle(handle)


# ─── HUD file parser (minimal) ────────────────────────────────────────────────

class HudBlock:
    def __init__(self, name, args):
        self.name = name
        self.args = args  # list of strings (raw)
        self.children = []

    @property
    def first_arg(self) -> Optional[str]:
        if not self.args: return None
        return self.args[0].strip().strip('"')

    def find(self, name):
        for c in self.children:
            if c.name == name:
                return c
        return None

    def all_children_named(self, name):
        return [c for c in self.children if c.name == name]


def tokenize(src: str):
    # Strip /* */ and // comments
    src = re.sub(r'/\*.*?\*/', '', src, flags=re.DOTALL)
    src = re.sub(r'//[^\n]*', '', src)
    # Tokens: ( ) { } strings, words, numbers
    pattern = re.compile(r'(\"[^\"]*\"|\(|\)|\{|\}|,|[^\s(){,]+)')
    return [m.group(0) for m in pattern.finditer(src)]


def parse_hud(text: str):
    tokens = tokenize(text)
    pos = [0]

    def peek(offset=0):
        return tokens[pos[0] + offset] if pos[0] + offset < len(tokens) else None

    def consume():
        tok = tokens[pos[0]]
        pos[0] += 1
        return tok

    def parse_block_or_eof():
        if pos[0] >= len(tokens):
            return None
        name = consume()
        if not re.match(r'^[A-Za-z_]', name):
            return None
        args = []
        if peek() == '(':
            consume()  # (
            while peek() != ')' and peek() is not None:
                tok = consume()
                if tok == ',': continue
                args.append(tok)
            if peek() == ')': consume()
        block = HudBlock(name, args)
        if peek() == '{':
            consume()  # {
            while peek() != '}' and peek() is not None:
                child = parse_block_or_eof()
                if child is None: break
                block.children.append(child)
            if peek() == '}': consume()
        return block

    top = []
    while pos[0] < len(tokens):
        b = parse_block_or_eof()
        if b is None: break
        top.append(b)
    return top


# ─── Editor's transform composition rules (ported from HudCanvas.xaml.cs) ─────

BITMAP_TYPES = {'Bitmap', 'BarBitmap', 'ProceduralBarBitmap'}


def get_position(block):
    p = block.find('Position')
    if not p or len(p.args) < 3: return None
    try:
        x, y, z = float(p.args[0]), float(p.args[1]), float(p.args[2])
        frame = p.args[3].strip('"') if len(p.args) > 3 else 'Viewport'
        return (x, y, z, frame)
    except (ValueError, IndexError):
        return None


def get_scale(block):
    s = block.find('Scale')
    if not s or len(s.args) < 3: return None
    try:
        return (float(s.args[0]), float(s.args[1]), float(s.args[2]))
    except (ValueError, IndexError):
        return None


def get_rotation(block):
    r = block.find('Rotation')
    if not r or len(r.args) < 3: return None
    try:
        return (float(r.args[0]), float(r.args[1]), float(r.args[2]))
    except (ValueError, IndexError):
        return None


def get_active_meshinfo(block):
    if block.name != 'Model3D':
        return None
    for c in block.children:
        if c.name == 'MeshInfo' and c.args and c.args[0].strip('"'):
            return c
    return None


def compute_matrix(block, parent_x, parent_y, parent_sx, parent_sy, parent_rot_z, screen_w, screen_h, parent_aspect_correction):
    """Returns (tx, ty, scale_x, scale_y) — comparable to engine matrix [12,13,0,5]."""
    # MeshInfo override for Model3D
    active_mi = get_active_meshinfo(block)
    pos = (active_mi or block).find('Position')
    pos = get_position(active_mi or block) if pos else None

    if pos:
        x, y, z, frame = pos
        if frame == 'Pixels':
            dx = x  # raw pixels — no normalization needed since matrix translation IS pixels
            dy = y
        else:
            # Viewport, Screen, Frame all get screen multiplied
            dx = x * screen_w
            dy = y * screen_h
    else:
        dx, dy = 0.0, 0.0

    # Apply parent rotation to local offset (clockwise screen-space)
    if abs(parent_rot_z) > 0.001:
        rad = parent_rot_z * 3.141592653589793 / 180.0
        from math import cos, sin
        c, s = cos(rad), sin(rad)
        # Note: live engine probe stores translations in PIXELS already, so we pre-scale here
        # using parent's scale (which is dimensionless), then add to parent world position.
        sdx = dx * parent_sx
        sdy = dy * parent_sy
        nsdx = sdx * c - sdy * s
        sdy = sdx * s + sdy * c
        sdx = nsdx
    else:
        sdx = dx * parent_sx
        sdy = dy * parent_sy

    tx = parent_x + sdx
    ty = parent_y + sdy

    # Pixel snap
    tx = round(tx)
    ty = round(ty)

    # Scale composition
    scale = get_scale(active_mi) if active_mi else get_scale(block)
    local_sx = scale[0] if scale else 1.0
    local_sy = scale[1] if scale else 1.0
    world_sx = parent_sx * local_sx
    world_sy = parent_sy * local_sy

    # Aspect Y correction for Bitmap-types only
    final_sx = world_sx
    final_sy = world_sy
    if block.name in BITMAP_TYPES and parent_aspect_correction != 1.0:
        final_sy *= parent_aspect_correction

    return tx, ty, final_sx, final_sy, world_sx, world_sy


def walk_hud(top_blocks, screen_w, screen_h, results, parent_x=0, parent_y=0, parent_sx=1.0, parent_sy=1.0, parent_rot_z=0.0):
    """Recursively compute matrices for every named element. Stores both LOCAL (relative to parent,
    matches engine's stored matrix translation) and WORLD (cumulative, what we'd render at)."""
    aspect = screen_w / screen_h
    hud_design_aspect = 4.0 / 3.0
    aspect_correction = aspect / hud_design_aspect if aspect > hud_design_aspect else 1.0

    for block in top_blocks:
        # Compute LOCAL contribution of this element (= dx, dy in pixels before adding parent).
        active_mi = get_active_meshinfo(block)
        pos = get_position(active_mi or block)
        if pos:
            x, y, _, frame = pos
            if frame == 'Pixels':
                local_tx, local_ty = x, y
            else:
                local_tx, local_ty = x * screen_w, y * screen_h
        else:
            local_tx, local_ty = 0.0, 0.0
        # Engine stores rounded local pixel translations
        local_tx = round(local_tx)
        local_ty = round(local_ty)

        tx, ty, render_sx, render_sy, world_sx, world_sy = compute_matrix(
            block, parent_x, parent_y, parent_sx, parent_sy, parent_rot_z,
            screen_w, screen_h, aspect_correction
        )
        name = block.first_arg
        if name and name.startswith('"'):
            name = name.strip('"')
        if name and re.match(r'^[a-zA-Z]', name):
            results[name] = {
                'tx': tx, 'ty': ty,
                'local_tx': local_tx, 'local_ty': local_ty,
                'scale_x': render_sx, 'scale_y': render_sy,
                'type': block.name,
            }

        local_rot = get_rotation(block)
        local_rot_z = local_rot[2] if local_rot else 0.0
        world_rot_z = parent_rot_z + local_rot_z

        walk_hud(block.children, screen_w, screen_h, results,
                 parent_x=tx, parent_y=ty,
                 parent_sx=world_sx, parent_sy=world_sy,
                 parent_rot_z=world_rot_z)


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    hud_path = sys.argv[1] if len(sys.argv) > 1 else r'C:\BF2_ModTools\data\Common\hud\1playerhud.hud'

    print('=== Live engine probe ===')
    engine = probe_engine()
    if engine is None:
        return 1
    print(f'  Screen: {engine["screen_w"]}x{engine["screen_h"]}    uiWidth: {engine["ui_w"]}')
    print(f'  Engine elements found (named): {len(engine["elements"])}')

    print()
    print(f'=== Editor compute (parsing {hud_path}) ===')
    with open(hud_path, 'r', encoding='latin-1', errors='replace') as f:
        text = f.read()
    top = parse_hud(text)
    editor = {}
    walk_hud(top, engine['screen_w'], engine['screen_h'], editor)
    print(f'  Editor blocks parsed: {len(editor)}')

    print()
    print('=== Per-element diff (engine vs editor) ===')
    print(f'{"NAME":<40} {"TYPE":<8} {"ENGINE tx,ty":<22} {"EDITOR tx,ty":<22} {"dtx":>7} {"dty":>7} {"ENG sx,sy":<15} {"EDR sx,sy":<15}')
    print('-' * 160)

    matched = 0
    mismatched = 0
    name_only_in_engine = 0
    name_only_in_editor = 0

    common = set(engine['elements'].keys()) & set(editor.keys())
    only_engine = set(engine['elements'].keys()) - set(editor.keys())
    only_editor = set(editor.keys()) - set(engine['elements'].keys())

    for name in sorted(common):
        eng = engine['elements'][name]
        ed = editor[name]
        # Engine stores LOCAL pixel offset in matrix translation slots [12, 13]
        # Editor's local equivalent is precomputed in 'local_tx'/'local_ty'
        eng_tx, eng_ty = eng[12], eng[13]
        eng_sx, eng_sy = eng[0], eng[5]
        edr_local_tx, edr_local_ty = ed['local_tx'], ed['local_ty']
        edr_sx, edr_sy = ed['scale_x'], ed['scale_y']
        d_tx = edr_local_tx - eng_tx
        d_ty = edr_local_ty - eng_ty
        is_match = abs(d_tx) < 1.0 and abs(d_ty) < 1.0 and abs(edr_sx - eng_sx) < 0.01 and abs(edr_sy - eng_sy) < 0.01
        marker = ' ' if is_match else 'X'
        if is_match:
            matched += 1
        else:
            mismatched += 1
        print(f'{marker} {name:<38} {ed["type"]:<8} '
              f'({eng_tx:7.1f},{eng_ty:7.1f}) ({edr_local_tx:7.1f},{edr_local_ty:7.1f}) {d_tx:+7.1f} {d_ty:+7.1f} '
              f'({eng_sx:.3f},{eng_sy:.3f}) ({edr_sx:.3f},{edr_sy:.3f})')

    print()
    print(f'Summary: matched={matched}, mismatched={mismatched}, only-in-engine={len(only_engine)}, only-in-editor={len(only_editor)}')
    if only_engine:
        print(f'\nNames only in engine ({len(only_engine)}): {", ".join(sorted(only_engine)[:10])}{"..." if len(only_engine) > 10 else ""}')
    if only_editor:
        print(f'\nNames only in editor ({len(only_editor)}): {", ".join(sorted(only_editor)[:10])}{"..." if len(only_editor) > 10 else ""}')

    return 0


if __name__ == '__main__':
    sys.exit(main())
