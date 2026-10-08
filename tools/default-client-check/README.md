# Ordinary client defaults and Studio boundary

Run `python3 tools/default-client-check/check.py --dotnet /path/to/dotnet` after cutover. This evaluates actual project properties and source/dependency items for four desktop game RIDs, both Android RIDs, explicit compatibility projections, dedicated servers, and Studio's referenced engine isolation.

It does not restore, build, publish, open a window or write into shared bin/obj directories. Actual source builds, native package verification, Android assembly-store audits and paired Studio smoke tests remain separate required gates. Use `--output /path/report.json` to save bounded content-free results.
