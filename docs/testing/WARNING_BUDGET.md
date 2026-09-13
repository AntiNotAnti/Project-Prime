# Analyzer warning budget

Project Prime keeps analyzer/style migration outside a repo-wide
warnings-as-errors policy, while the named production-project budget is an
enforced CI gate. The committed
[`tools/warning-budget-baseline.json`](../../tools/warning-budget-baseline.json)
records warning counts by named production project and does not remove an
intentional `NoWarn`/format suppression.

Collect a fresh report with a clean compile:

```bash
python3 tools/check-warning-budget.py collect \
  --report artifacts/warnings/current.json --no-restore
```

Compare it with the committed budget:

```bash
python3 tools/check-warning-budget.py check \
  --baseline tools/warning-budget-baseline.json \
  --current artifacts/warnings/current.json \
  --output artifacts/warnings/comparison.json
```

The checker rejects new warning codes and increases in existing warning-code
counts. A named project build failure also fails the CI gate; the warning
report and comparison are uploaded with `always()` for diagnosis. Intentional
suppressions remain documented in the baseline and owned project files; fixing
a warning is a separate, focused change.
