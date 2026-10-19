---
name: run-settlement
description: Run Alipay settlement for pending SFG advice files (or one local file) and summarise the result and breaks
argument-hint: "[path-to-advice-file]"
agent: alipay-settlement
---

Run the settlement agent and report the outcome.

1. `dotnet run --project src/Settlement.Agent -- validate-config` and stop if it fails.
2. If an argument was given, run `dotnet run --project src/Settlement.Agent -- process --file $1`.
   Otherwise run `dotnet run --project src/Settlement.Agent -- run-once` to process the SFG inbound mailbox.
3. Read `data/reports/<file name without extension>/summary.json` and `breaks.csv`.
4. Report totals per type, net settlement per currency, match rate and every break grouped by severity
   (High first), with the reason and suggested next action for each.
