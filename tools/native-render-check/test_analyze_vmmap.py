import unittest
from analyze_vmmap import byte_value, compare, parse


class VmmapAnalysisTests(unittest.TestCase):
    def test_units_and_raw_bytes(self):
        self.assertEqual(byte_value("32"), 32)
        self.assertEqual(byte_value("1.5M"), 1.5 * 1024**2)
        self.assertEqual(byte_value("1.2G"), 1.2 * 1024**3)
        with self.assertRaises(ValueError):
            byte_value("unavailable")

    def test_region_names_with_spaces_and_independent_columns(self):
        data = parse("REGION TYPE                        SIZE\nMalloc Small (empty)              52.0M    37.4M    30.0M       1K       0K       0K       0K       13\n")
        row = data["regions"]["Malloc Small (empty)"]
        self.assertEqual(row["residentBytes"], 37.4 * 1024**2)
        self.assertEqual(row["dirtyBytes"], 30 * 1024**2)
        self.assertEqual(row["swappedBytes"], 1024)

    def test_malloc_zone_addresses_normalized_and_allocated_is_distinct(self):
        text = "REGION TYPE   SIZE\nUntagged   1G  100M  90M  0K  0K  0K  0K  1\nMALLOC ZONE   SIZE\nDefaultMallocZone_0x104934000   390M 346M 345M 0K 90958 283M 62M 19 101\n"
        data = parse(text)
        row = data["mallocZones"]["DefaultMallocZone"]
        self.assertEqual(row["allocationCount"], 90958)
        self.assertEqual(row["allocatedBytes"], 283 * 1024**2)
        self.assertEqual(row["residentBytes"], 346 * 1024**2)

    def test_paired_delta_and_missing_category(self):
        a = parse("REGION TYPE   SIZE\nMalloc Small   100M 90M 80M 0K 0K 0K 0K 1\n")
        b = parse("REGION TYPE   SIZE\nMalloc Small   60M 40M 30M 0K 0K 0K 0K 1\nUntagged   1G 20M 10M 0K 0K 0K 0K 1\n")
        result = compare(a, b)
        self.assertEqual(result["regions"]["Malloc Small"]["candidateMinusBaseline"]["residentBytes"], -50 * 1024**2)
        self.assertIsNone(result["regions"]["Untagged"]["candidateMinusBaseline"])

    def test_failed_capture_is_not_zero_memory(self):
        data = parse("vmmap: cannot examine process\n")
        self.assertFalse(data["supported"])
        self.assertIsNone(data["physicalFootprintBytes"])
        self.assertFalse(compare(data, data)["supported"])


if __name__ == "__main__":
    unittest.main()
