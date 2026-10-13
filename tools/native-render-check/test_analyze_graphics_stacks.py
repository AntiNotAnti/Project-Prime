import io
import unittest

import analyze_graphics_stacks as graphics


class GraphicsStackTests(unittest.TestCase):
    def test_vm_precedes_driver_descendants_and_groups_are_disjoint(self):
        data = """2 calls for 1024 bytes:
0 libsystem_kernel.dylib 0x100 mmap + 164
1 libcoreclr.dylib 0x120 reserve + 68
2 com.apple.AGXMetalG16X 0x140 FragmentProgramVariant + 1
3 calls for 4096 bytes:
0 libsystem_malloc.dylib 0x100 malloc + 1
1 com.apple.AGXMetalG16X 0x120 FragmentProgramVariant + 2
2 com.apple.Metal 0x140 compile + 3
4 calls for 2048 bytes:
0 com.apple.IOSurface 0x100 surface + 1
1 com.apple.IOGPU 0x140 drawable + 3
"""
        categories = graphics.parse(io.StringIO(data))["categories"]
        self.assertEqual(categories["explicitMmapVm"]["bytes"], 1024)
        self.assertEqual(categories["agxFragmentVariant"], {"bytes": 4096, "calls": 3, "groups": 1})
        self.assertEqual(categories["ioSurface"]["bytes"], 2048)
        self.assertEqual(sum(c["bytes"] for c in categories.values()), 7168)

    def test_failed_capture_is_unavailable_not_zero_driver_memory(self):
        result = graphics.parse(["Unable to inspect target\n"])
        self.assertFalse(result["supported"])
        self.assertEqual(result["categories"], {})

    def test_other_runtime_origins_are_not_assigned_to_driver(self):
        data = "1 calls for 512 bytes:\n0 libsystem_malloc.dylib 0x100 malloc + 1\n1 libcoreclr.dylib 0x120 initialize + 2\n"
        self.assertEqual(graphics.parse(io.StringIO(data))["categories"], {"coreclr": {"bytes": 512, "calls": 1, "groups": 1}})
