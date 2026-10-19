---
name: investigate-break
description: Trace one settlement break (by transaction id) through the advice file, the engine rules and our internal transactions
argument-hint: "<transaction-id>"
agent: alipay-settlement
---

Investigate the break for transaction `$1`.

1. Find it in the latest `data/reports/*/breaks.csv` and `entries.csv`.
2. Find the raw advice line (line number in the report) in `data/work/` or `samples/advice/`.
3. Find the transaction and any original/refund/chargeback relatives in the internal transactions file
   configured at `Settlement:InternalSource:Path`.
4. Explain which rule raised the break (`src/Settlement.Core/Engine` or `Reconciliation/Reconciler.cs`),
   the likely root cause (provider, our system, timing, or configuration) and the recommended fix.
