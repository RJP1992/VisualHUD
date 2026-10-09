"""
BF2_modtools_MemExt.exe live-memory probe.

Run this WHILE the modtools EXE is running with a HUD visible (load any level
in instant action) to dump the runtime values of the engine's HUD-rendering
globals. We use these to derive the exact mesh-unit-to-pixel formula that the
editor needs for accurate Model3D preview.

Requires: Windows + Python 3.x. No third-party packages — uses ctypes / Win32.

Usage:
    python bf2_live_probe.py

If the script can't find the process, double-check that you launched the
EXACT exe Ghidra has loaded (BF2_modtools_MemExt.exe), not the regular game.
"""

import ctypes
import ctypes.wintypes as wt
import struct
import sys

# Windows API constants
PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400
TH32CS_SNAPPROCESS = 0x00000002

kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
psapi = ctypes.WinDLL('psapi', use_last_error=True)

# OpenProcess
kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.OpenProcess.restype = wt.HANDLE

kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.CloseHandle.restype = wt.BOOL

# ReadProcessMemory
kernel32.ReadProcessMemory.argtypes = [
    wt.HANDLE, wt.LPCVOID, wt.LPVOID, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)
]
kernel32.ReadProcessMemory.restype = wt.BOOL


class PROCESSENTRY32(ctypes.Structure):
    _fields_ = [
        ('dwSize', wt.DWORD),
        ('cntUsage', wt.DWORD),
        ('th32ProcessID', wt.DWORD),
        ('th32DefaultHeapID', ctypes.c_void_p),
        ('th32ModuleID', wt.DWORD),
        ('cntThreads', wt.DWORD),
        ('th32ParentProcessID', wt.DWORD),
        ('pcPriClassBase', wt.LONG),
        ('dwFlags', wt.DWORD),
        ('szExeFile', ctypes.c_char * 260),
    ]


kernel32.CreateToolhelp32Snapshot.argtypes = [wt.DWORD, wt.DWORD]
kernel32.CreateToolhelp32Snapshot.restype = wt.HANDLE
kernel32.Process32First.argtypes = [wt.HANDLE, ctypes.POINTER(PROCESSENTRY32)]
kernel32.Process32First.restype = wt.BOOL
kernel32.Process32Next.argtypes = [wt.HANDLE, ctypes.POINTER(PROCESSENTRY32)]
kernel32.Process32Next.restype = wt.BOOL


# EnumProcessModulesEx — to find the EXE module's actual base address (handles ASLR if on)
LIST_MODULES_ALL = 0x03
psapi.EnumProcessModulesEx.argtypes = [wt.HANDLE, ctypes.POINTER(wt.HMODULE), wt.DWORD, ctypes.POINTER(wt.DWORD), wt.DWORD]
psapi.EnumProcessModulesEx.restype = wt.BOOL
psapi.GetModuleBaseNameA.argtypes = [wt.HANDLE, wt.HMODULE, ctypes.c_char_p, wt.DWORD]
psapi.GetModuleBaseNameA.restype = wt.DWORD


# Static addresses (preferred-base 0x00400000) extracted from Ghidra analysis of
# BF2_modtools_MemExt.exe. We add the module's actual base offset at runtime
# (= actual_base - 0x00400000) before reading.
PREFERRED_BASE = 0x00400000

ADDRESSES = [
    # (address, type, label)
    (0x00ad7e7c, 'f', 'sRuntimeScale (HUD::Element global runtime scale)'),
    (0x00ba38fc, 'f', 'uiWidth_ (current viewport width in pixels)'),
    (0x00e5b504, 'f', 'sf? (camera frustum width or height)'),
    (0x00e5b534, 'f', 'sf? (the other one)'),
    (0x00e5b508, 'i', 'm_uiWidthInPixels (RedInterfaceScreen::s_screenFull)'),
    (0x00e5b50c, 'i', 'm_uiHeightInPixels'),
    (0x00ed70dc, 'f', 's_fCotHalfFOVx_ (RedHotSpot)'),
    (0x00ed70e0, 'f', 's_fCotHalfFOVy_ (RedHotSpot)'),
    (0x00ae3044, 'f', 'FLOAT_00ae3044 (1.0 default)'),
    (0x00ae3048, 'f', 'FLOAT_00ae3048 (1.0 default — render mode 2 X factor)'),
    (0x00ae304c, 'f', 'FLOAT_00ae304c (1.0 default — render mode 2 Y factor)'),
    (0x00ae3050, 'f', 'FLOAT_00ae3050 = 640/screen_width (X scale factor)'),
    (0x00ae3054, 'f', 'FLOAT_00ae3054 (= ae3050)'),
    (0x00ae3058, 'f', 'FLOAT_00ae3058 = 480/screen_height (Y scale factor)'),
]

# RedInterfaceScreen::s_screenFull layout fields (offsets from m_uiWidthInPixels base 0x00e5b508)
# We'll read a chunk and decode safezone fields too.
SAFEZONE_BASE = 0x00e5b508  # m_uiWidthInPixels start
SAFEZONE_FIELDS = [
    (0x00, 'I', 'm_uiWidthInPixels'),
    (0x04, 'I', 'm_uiHeightInPixels'),
    (0x08, 'f', 'm_fSafeZoneOffsetX'),
    (0x0c, 'f', 'm_fSafeZoneOffsetY'),
    (0x10, 'f', 'm_fSafeZoneWidth'),
    (0x14, 'f', 'm_fSafeZoneHeight'),
]


def find_pid(exe_name: str) -> int | None:
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snap == 0 or snap == 0xFFFFFFFF:
        return None
    try:
        entry = PROCESSENTRY32()
        entry.dwSize = ctypes.sizeof(PROCESSENTRY32)
        if not kernel32.Process32First(snap, ctypes.byref(entry)):
            return None
        while True:
            name = entry.szExeFile.decode('latin-1', errors='replace').lower()
            if name == exe_name.lower():
                return entry.th32ProcessID
            if not kernel32.Process32Next(snap, ctypes.byref(entry)):
                return None
    finally:
        kernel32.CloseHandle(snap)


def get_module_base(handle, exe_name: str) -> int | None:
    """Find the actual load base of the EXE module inside the process."""
    needed = wt.DWORD(0)
    # Probe size first
    psapi.EnumProcessModulesEx(handle, None, 0, ctypes.byref(needed), LIST_MODULES_ALL)
    count = needed.value // ctypes.sizeof(wt.HMODULE)
    if count == 0:
        return None
    arr = (wt.HMODULE * count)()
    if not psapi.EnumProcessModulesEx(handle, arr, needed.value, ctypes.byref(needed), LIST_MODULES_ALL):
        return None
    name_buf = ctypes.create_string_buffer(260)
    for h in arr:
        if h == 0:
            continue
        if psapi.GetModuleBaseNameA(handle, h, name_buf, 260) == 0:
            continue
        if name_buf.value.decode('latin-1', errors='replace').lower() == exe_name.lower():
            return h
    return None


def read_bytes(handle, addr: int, size: int) -> bytes | None:
    buf = (ctypes.c_ubyte * size)()
    nread = ctypes.c_size_t(0)
    if not kernel32.ReadProcessMemory(handle, ctypes.c_void_p(addr), buf, size, ctypes.byref(nread)):
        return None
    return bytes(buf[:nread.value])


def main() -> int:
    candidates = [
        'BF2_modtools_MemExt.exe',
        'BF2_modtools.exe',
        'BattlefrontII.exe',
        'BF2DevNoGreen1920x1080Windowed.exe',
        'BF2DevNoGreen1920x1080Win.exe',  # truncated form seen in tasklist
    ]
    exe = None
    pid = None
    for name in candidates:
        pid = find_pid(name)
        if pid is not None:
            exe = name
            break
    # Last resort: any *.exe with 'BF2' or 'Battlefront' in the name
    if pid is None:
        snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
        if snap and snap != 0xFFFFFFFF:
            try:
                entry = PROCESSENTRY32()
                entry.dwSize = ctypes.sizeof(PROCESSENTRY32)
                if kernel32.Process32First(snap, ctypes.byref(entry)):
                    while True:
                        name = entry.szExeFile.decode('latin-1', errors='replace')
                        lc = name.lower()
                        if ('bf2' in lc or 'battlefront' in lc) and lc.endswith('.exe'):
                            exe = name
                            pid = entry.th32ProcessID
                            break
                        if not kernel32.Process32Next(snap, ctypes.byref(entry)):
                            break
            finally:
                kernel32.CloseHandle(snap)
    if pid is None:
        print(f'ERROR: could not find any of: {", ".join(candidates)}.')
        print('Make sure the modtools EXE is running with a HUD visible.')
        return 1

    print(f'Found {exe} at PID {pid}.')
    if exe != 'BF2_modtools_MemExt.exe':
        print(f'WARN: this script\'s addresses came from BF2_modtools_MemExt.exe analysis.')
        print(f'      "{exe}" is a different build — values may not match.')
        print()

    handle = kernel32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid)
    if not handle:
        err = ctypes.get_last_error()
        print(f'ERROR: OpenProcess failed (error {err}). Try running this script as administrator.')
        return 1

    try:
        actual_base = get_module_base(handle, exe)
        if actual_base is None:
            print('ERROR: could not find the EXE module inside the process.')
            return 1
        slide = actual_base - PREFERRED_BASE
        print(f'Module base: 0x{actual_base:08x}  (slide from preferred 0x{PREFERRED_BASE:08x}: {"+" if slide >= 0 else ""}0x{slide & 0xffffffff:08x})')
        print()

        print('--- Static globals (Ghidra-identified) ---')
        for addr, kind, label in ADDRESSES:
            real = addr + slide
            data = read_bytes(handle, real, 4)
            if data is None or len(data) != 4:
                print(f'  0x{addr:08x}  ({label:60})  READ FAILED')
                continue
            if kind == 'f':
                val = struct.unpack('<f', data)[0]
                print(f'  0x{addr:08x}  ({label:60})  = {val:.6f}')
            elif kind == 'i':
                val = struct.unpack('<i', data)[0]
                print(f'  0x{addr:08x}  ({label:60})  = {val}')

        print()
        print('--- s_screenFull struct fields (decoded) ---')
        block = read_bytes(handle, SAFEZONE_BASE + slide, 0x18)
        if block is None:
            print('  READ FAILED')
        else:
            for off, fmt, label in SAFEZONE_FIELDS:
                val = struct.unpack_from('<' + fmt, block, off)[0]
                print(f'  +0x{off:02x}  ({label:30})  = {val}')

        # Derived calculations
        print()
        print('--- Derived (using the values just read) ---')
        cot_x = struct.unpack('<f', read_bytes(handle, 0x00ed70dc + slide, 4))[0]
        cot_y = struct.unpack('<f', read_bytes(handle, 0x00ed70e0 + slide, 4))[0]
        screen_w = struct.unpack('<i', read_bytes(handle, 0x00e5b508 + slide, 4))[0]
        screen_h = struct.unpack('<i', read_bytes(handle, 0x00e5b50c + slide, 4))[0]
        ui_w = struct.unpack('<f', read_bytes(handle, 0x00ba38fc + slide, 4))[0]
        sf1 = struct.unpack('<f', read_bytes(handle, 0x00e5b504 + slide, 4))[0]
        sf2 = struct.unpack('<f', read_bytes(handle, 0x00e5b534 + slide, 4))[0]

        print(f'  Screen: {screen_w}x{screen_h}')
        print(f'  uiWidth_: {ui_w}')
        print(f'  sf-pair: ({sf1}, {sf2})')
        print(f'  cot_x={cot_x:.6f}  cot_y={cot_y:.6f}')
        if cot_x > 0 and screen_w > 0:
            # If a unit-sized mesh at element depth z=-uiWidth_/sfWidth projects to:
            #   ndc_x = 1 * cot_x / |z|;  pixel = ndc_x * screen_w/2
            # then mesh-units-to-pixels = cot_x * screen_w / (2 * |z|)
            for sf_label, sf_val in (('sf1', sf1), ('sf2', sf2)):
                if sf_val > 0:
                    z = ui_w / sf_val if ui_w > 0 else screen_w / sf_val
                    ratio = cot_x * screen_w / (2.0 * z)
                    print(f'  Textbook px/unit using {sf_label}={sf_val:.6f}: '
                          f'z={z:.3f}, ratio={ratio:.4f} (clearly wrong if ~1)')

        # --- HUD element walker ---
        # PTR_LOOP_00ad7ee0 is the head of a doubly-linked list. Each node contains a pointer
        # offset back to the containing HUD::Element. The element's mTransform pointer is at
        # element+0xb0; the matrix is at mTransform+0x30 (16 floats = 4x4).
        print()
        print('--- HUD element linked-list walk ---')
        LIST_HEAD = 0x00ad7ee0 + slide
        # List head layout observed in FUN_00694450: struct has next/prev pointers at +0, +4.
        # Nodes are pointed to via forward-link; from the HUD::Element base the node is at
        # element + 0x2d*4 = 0xb4 (see SetRuntimeScaleAll_ code: ppiVar2 + -0x2d gives element).
        NODE_TO_ELEMENT_OFFSET = -0x2d * 4  # = -0xb4

        head_next_bytes = read_bytes(handle, LIST_HEAD, 4)
        if head_next_bytes is None:
            print('  Could not read list head.')
        else:
            cur_node = struct.unpack('<I', head_next_bytes)[0]
            count = 0
            max_dump = 60  # dump all 40 with headroom
            visited = set()
            while cur_node != LIST_HEAD and cur_node != 0 and count < 200 and cur_node not in visited:
                visited.add(cur_node)
                element_ptr = cur_node + NODE_TO_ELEMENT_OFFSET
                # mTransform pointer at element + 0xb0
                mt_bytes = read_bytes(handle, element_ptr + 0xb0, 4)
                if mt_bytes is None:
                    break
                mt_ptr = struct.unpack('<I', mt_bytes)[0]
                # Read 16 floats starting at mTransform + 0x30
                mat_bytes = read_bytes(handle, mt_ptr + 0x30, 64)
                # Read frame enum at mTransform + 0x10
                frame_bytes = read_bytes(handle, mt_ptr + 0x10, 4)
                # Read ScriptCB_IFObj name (stored at element + something) — skip for now
                # Read the Scalable flag at element + 0xfc
                flags_bytes = read_bytes(handle, element_ptr + 0xfc, 4)

                if count < max_dump and mat_bytes and len(mat_bytes) == 64:
                    floats = struct.unpack('<16f', mat_bytes)
                    frame_val = struct.unpack('<I', read_bytes(handle, element_ptr + 0x10, 4) or b'\xff\xff\xff\xff')[0]
                    flags = struct.unpack('<I', flags_bytes)[0] if flags_bytes else 0

                    # Type name at element+0x18 (set by Element ctor). Brute-force search
                    # the whole element block for any ASCII string that includes "player"
                    # or other HUD instance-name signal.
                    instance_name = ''
                    block = read_bytes(handle, element_ptr, 0x300) or b''
                    # Scan dwords for pointers that lead to instance-name-like strings
                    for off in range(0, len(block) - 4, 4):
                        ptr = struct.unpack_from('<I', block, off)[0]
                        if not (0x400000 <= ptr <= 0x40000000): continue
                        s_b = read_bytes(handle, ptr, 64) or b''
                        nul = s_b.find(b'\x00')
                        if nul < 4 or nul > 60: continue
                        s = s_b[:nul].decode('latin-1', errors='replace')
                        if not all(0x20 <= ord(c) < 0x7f for c in s): continue
                        if any(k in s.lower() for k in ('player', 'team', 'reticule', 'health', 'weapon', 'ammo', 'jet', 'tooltip', 'message')):
                            instance_name = f' name="{s}" (off=+0x{off:x})'
                            break

                    # Print row 0-3 with full data so we can see rotation/skew off-diagonals
                    has_any = any(abs(f) > 0.001 for f in floats[:12]) or any(abs(f) > 0.001 for f in floats[12:15])
                    if has_any:
                        print(f'  [{count}] element=0x{element_ptr:08x}  frame={frame_val}{instance_name}')
                        print(f'      row0: {floats[0]:9.3f} {floats[1]:9.3f} {floats[2]:9.3f} {floats[3]:9.3f}')
                        print(f'      row1: {floats[4]:9.3f} {floats[5]:9.3f} {floats[6]:9.3f} {floats[7]:9.3f}')
                        print(f'      row2: {floats[8]:9.3f} {floats[9]:9.3f} {floats[10]:9.3f} {floats[11]:9.3f}')
                        print(f'      row3: {floats[12]:9.3f} {floats[13]:9.3f} {floats[14]:9.3f} {floats[15]:9.3f}')

                # Advance to next node
                next_bytes = read_bytes(handle, cur_node, 4)
                if next_bytes is None:
                    break
                cur_node = struct.unpack('<I', next_bytes)[0]
                count += 1

            print(f'  ({count} elements walked, dumped first {min(count, max_dump)})')

        print()
        print('Copy this entire output and paste it back to the assistant.')
    finally:
        kernel32.CloseHandle(handle)

    return 0


if __name__ == '__main__':
    sys.exit(main())
