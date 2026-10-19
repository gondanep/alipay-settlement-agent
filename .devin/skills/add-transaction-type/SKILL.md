---
name: add-transaction-type
description: Onboard a new provider transaction type code, by configuration or with a dedicated handler
argument-hint: "<profile> <provider-code> <canonical-type>"
agent: alipay-settlement
---

Onboard provider code `$2` in profile `$1` as `$3`.

1. If `$3` is an existing handler type (Payment, Refund, Cancel, Chargeback, ChargebackReversal, Fee,
   Adjustment), add `"$2": { "Type": "$3" }` under `Settlement:Profiles:$1:TransactionTypes`.
2. If it is new but needs no special validation, add it with `Direction` (Credit/Debit/Signed) and optional
   `RequiresOriginal` / `MatchInternal`; the generic handler processes it.
3. Only if it needs its own rules, add a `TransactionTypeHandler` subclass in
   `src/Settlement.Core/Engine/TransactionTypeHandler.cs`, register it in `TransactionTypeEngine.DefaultHandlers`,
   and add tests in `tests/Settlement.Tests/TransactionTypeEngineTests.cs`.
4. Run `validate-config`, `dotnet test` and `dotnet format --verify-no-changes`.
