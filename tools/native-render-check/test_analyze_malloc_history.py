import unittest
from analyze_malloc_history import parse


class MallocHistoryAnalysisTests(unittest.TestCase):
    def test_outstanding_source_vectors_group_counted_once(self):
        text = "3 calls for 61440 bytes:\n0 libsystem_malloc.dylib 0x100 malloc + 4\n1 com.apple.audio.OpenAL 0x200 vector<BufferInfo>::reserve + 8\n2 com.apple.audio.OpenAL 0x300 allocator<BufferInfo>::allocate + 12\n3 com.apple.audio.OpenAL 0x400 alGenSources + 16\n"
        result = parse(text)
        self.assertTrue(result["supported"])
        self.assertEqual(result["sourceVectorBytes"], 61440)
        self.assertEqual(result["sourceVectorCalls"], 3)
        self.assertEqual(result["openAlMallocBytes"], 61440)

    def test_virtual_mapping_is_not_malloc_or_resident(self):
        text = "2 calls for 67108864 bytes:\n0 libsystem_kernel.dylib 0x100 mmap + 4\n1 com.apple.audio.OpenAL 0x200 alGenSources + 8\n"
        result = parse(text)
        self.assertEqual(result["virtualMappingGroupBytes"], 67108864)
        self.assertEqual(result["openAlMallocBytes"], 0)

    def test_other_malloc_module_not_assigned_to_openal(self):
        text = "10 calls for 1000 bytes:\n0 libsystem_malloc.dylib 0x100 calloc + 4\n1 libcoreclr.dylib 0x200 CreateTypes + 8\n"
        result = parse(text)
        self.assertEqual(result["groups"], 1)
        self.assertEqual(result["openAlMallocBytes"], 0)

    def test_library_alias_and_multiple_groups(self):
        text = "1 calls for 128 bytes:\n0 libsystem_malloc.dylib 0x100 malloc + 4\n1 libopenal.1.dylib 0x200 alGenSources + 8\n\n2 calls for 256 bytes:\n0 libsystem_malloc.dylib 0x300 malloc + 4\n1 libopenal.1.dylib 0x400 CreateContext + 8\n"
        result = parse(text)
        self.assertEqual(result["openAlMallocBytes"], 384)
        self.assertEqual(result["openAlMallocCalls"], 3)
        self.assertEqual(result["sourceVectorBytes"], 0)

    def test_failed_capture_is_unsupported(self):
        self.assertFalse(parse("malloc_history: no stack logging data found")['supported'])


if __name__ == "__main__":
    unittest.main()
