"""Quick probe — read BBOX and vertex range from an MSH file to see what units it's in."""
import struct
import sys


def parse_msh(path: str) -> None:
    with open(path, 'rb') as f:
        data = f.read()

    pos = 0
    depth = 0
    parent_chunks = {'HEDR', 'MSH2', 'SINF', 'MATL', 'MATD', 'MODL', 'GEOM', 'SEGM'}

    min_x = min_y = min_z = float('inf')
    max_x = max_y = max_z = float('-inf')

    def walk(start: int, end: int, d: int) -> None:
        nonlocal min_x, min_y, min_z, max_x, max_y, max_z
        p = start
        while p + 8 <= end:
            cid = data[p:p+4].decode('ascii', errors='replace')
            size = struct.unpack_from('<I', data, p+4)[0]
            payload_start = p + 8
            payload_end = payload_start + size
            if payload_end > end:
                return
            indent = '  ' * d
            if cid == 'BBOX' and size >= 44:
                # quaternion(16) + center(12) + extents(12) + radius(4)
                cx, cy, cz = struct.unpack_from('<fff', data, payload_start + 16)
                ex, ey, ez = struct.unpack_from('<fff', data, payload_start + 28)
                radius = struct.unpack_from('<f', data, payload_start + 40)[0]
                print(f'{indent}BBOX center=({cx:.4f}, {cy:.4f}, {cz:.4f}) extents=({ex:.4f}, {ey:.4f}, {ez:.4f}) radius={radius:.4f}')
            elif cid == 'POSL':
                # UInt32 count, then count*3 floats
                count = struct.unpack_from('<I', data, payload_start)[0]
                print(f'{indent}POSL count={count}')
                for i in range(count):
                    off = payload_start + 4 + i * 12
                    x, y, z = struct.unpack_from('<fff', data, off)
                    if x < min_x: min_x = x
                    if x > max_x: max_x = x
                    if y < min_y: min_y = y
                    if y > max_y: max_y = y
                    if z < min_z: min_z = z
                    if z > max_z: max_z = z
            elif cid in parent_chunks:
                child_start = payload_start
                if cid == 'MATL' and size >= 4:
                    child_start += 4
                print(f'{indent}{cid} (parent, size={size})')
                walk(child_start, payload_end, d + 1)
            else:
                pass  # skip leaf
            p = payload_end

    walk(0, len(data), 0)

    if min_x != float('inf'):
        print()
        print(f'Aggregate vertex range: X=[{min_x:.4f}, {max_x:.4f}] Y=[{min_y:.4f}, {max_y:.4f}] Z=[{min_z:.4f}, {max_z:.4f}]')
        print(f'Extent (max-min): ({max_x-min_x:.4f}, {max_y-min_y:.4f}, {max_z-min_z:.4f})')


if __name__ == '__main__':
    path = sys.argv[1] if len(sys.argv) > 1 else r'C:\BF2_ModTools\data\Common\mshs\hud_jetpackbar_frame_front.msh'
    print(f'Reading: {path}')
    parse_msh(path)
