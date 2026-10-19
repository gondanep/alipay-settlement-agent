# Alipay Settlement Agent

Picks up Alipay APM settlement advice files from Sterling File Gateway (SFG), parses them through a
configurable field layout, runs every line through a transaction-type engine, reconciles the result
against our internal transactions and writes settlement reports with classified breaks.

```
SFG mailbox (SFTP / local folder)
   └─> duplicate check (SHA-256 ledger)
        └─> profile selection (file name regex)
             └─> AdviceParser (config-driven fields, control totals)
                  └─> TransactionTypeEngine (Payment, Refund, Cancel, Chargeback, ... , config-only types)
                       └─> Reconciler (vs internal transactions)
                            └─> reports: summary.json, breaks.csv, entries.csv, rejected.csv
                                 └─> archive file to processed/ or error/
```

| Project | Purpose |
|---|---|
| `src/Settlement.Core` | Configuration model and validation, parser, transaction engine, reconciler, reports |
| `src/Settlement.Sfg` | SFG file sources: SFTP (SSH.NET) and local folder; processed-file ledger |
| `src/Settlement.Agent` | Executable: CLI commands and background worker |
| `tests/Settlement.Tests` | xUnit tests, including an SFTP integration test |
| `.devin/` | Devin `alipay-settlement` agent profile and skills |

## Run it

Requires the .NET 8 SDK.

```bash
dotnet build AlipaySettlement.sln
dotnet run --project src/Settlement.Agent -- validate-config
dotnet run --project src/Settlement.Agent -- process --file samples/advice/ALIPAY_SETTLE_2088000000000001_20261018.csv
./scripts/demo.sh            # copies the samples into data/sfg/inbound and runs one SFG pass
```

| Command | What it does |
|---|---|
| `run-once` | Process every pending file in the SFG inbound mailbox, then exit |
| `watch` | Poll the mailbox every `Settlement:Sfg:PollIntervalSeconds` (runs as a hosted worker) |
| `process --file <path>` | Settle one local file (no SFG, no archiving) |
| `validate-config` | Check profiles, field mappings and transaction type rules |

Options: `--config <path>` loads an extra JSON file on top of `appsettings.json`; `--fail-on-breaks`
returns exit code 2 when any break is found. Exit code 1 means a file failed or config is invalid.

Reports go to `data/reports/<file name>/`.

### Connecting to SFG

Set `Settlement:Sfg:Mode` to `Sftp` and supply host, user and paths. Secrets come from the environment,
never from committed config:

```bash
export Settlement__Sfg__Mode=Sftp
export Settlement__Sfg__Host=sfg.example.com
export Settlement__Sfg__Username=alipay_settle
export Settlement__Sfg__Password=...                 # or:
export Settlement__Sfg__PrivateKeyPath=/secrets/sfg_key
export Settlement__Sfg__PrivateKeyPassphrase=...
```

Files matching `Settlement:Sfg:FilePattern` are downloaded from `InboundPath`, settled, and moved to
`ProcessedPath` (or `ErrorPath` for failures and duplicate content). A copy of every file is kept in
`WorkingPath`, and `LedgerPath` records each file's SHA-256 so a resend of the same content is not
settled twice.

## Configuring the advice layout

Each provider file layout is a profile under `Settlement:Profiles` in
`src/Settlement.Agent/appsettings.json`. The profile is chosen by `FileNamePattern`.

| Profile setting | Meaning |
|---|---|
| `Delimiter`, `Quote`, `Encoding`, `HasHeader`, `SkipLeadingRows` | File format |
| `TrailerPrefixes` | Rows to ignore (e.g. `TOTAL`, `#`) |
| `ControlTotals` | Trailer row with expected record count / amount, checked against parsed rows |
| `Culture`, `TimeZone` | Number parsing and the zone that file timestamps are in |
| `Fields` | One entry per advice column (below) |
| `TransactionTypes` | Provider type code -> transaction rule (below) |

Every field is configurable:

```json
{ "Name": "Amount", "Source": "Amount", "Type": "Decimal", "Required": true }
{ "Name": "TransactionTime", "Source": "Payment_time", "Type": "DateTime", "Format": "yyyy-MM-dd HH:mm:ss|yyyyMMddHHmmss" }
{ "Name": "WalletBrand", "Index": 9, "Transform": "Upper", "MaxLength": 20, "Default": "UNKNOWN" }
```

- `Source` is the header name; `Index` is the zero-based column (needed when `HasHeader` is false).
- `Type`: `String`, `Decimal`, `Integer`, `Date`, `DateTime`, `Boolean`. `Format` alternatives are `|`-separated.
- Canonical names (`TransactionId`, `OriginalTransactionId`, `TransactionType`, `Amount`, `Currency`, `Fee`,
  `SettlementAmount`, `SettlementCurrency`, `ExchangeRate`, `TransactionTime`, `SettlementTime`, `Status`,
  `BatchId`) drive matching. Any other name is a custom field carried through to `entries.csv`.
- `TransactionId`, `TransactionType`, `Amount` and `Currency` must be mapped.

## Transaction-type engine

| Canonical type | Sign | Needs original | Matched to internal | Extra validation |
|---|---|---|---|---|
| `Payment` | + | no | yes | |
| `Refund` | - | yes | yes | total refunded (incl. earlier refunds in our system) <= original |
| `Cancel` | - | yes | yes | equals original amount |
| `Chargeback` | - | yes | yes | |
| `ChargebackReversal` | + | yes | yes | |
| `Fee` | - | no | no | |
| `Adjustment` | as in file | no | no | |

Map provider codes to these types:

```json
"TransactionTypes": {
  "P":   { "Type": "Payment" },
  "R":   { "Type": "Refund" },
  "TAX": { "Type": "WithholdingTax", "Direction": "Debit", "MatchInternal": false }
}
```

A rule with `Direction` (`Credit`, `Debit`, `Signed`) defines a new type with no code change. `RequiresOriginal`
and `MatchInternal` override a handler's defaults. Types that need their own validation get a new
`TransactionTypeHandler` subclass in `src/Settlement.Core/Engine` registered in `TransactionTypeEngine`.

Net per line = signed gross - fee.

## Reconciliation and breaks

Our transactions come from `Settlement:InternalSource` (CSV or JSON export, columns configurable). Matching
uses `TransactionId` (our reference, Alipay `Partner_transaction_id`).

| Break | Raised when |
|---|---|
| `InvalidRecord` | A line fails field conversion or a required field is empty |
| `UnknownTransactionType` | Type code not configured |
| `ControlTotalMismatch` | Trailer count or amount differs from parsed rows |
| `Duplicate` | Same type + transaction id appears twice in the file |
| `MissingInInternal` | In the advice, not in our system |
| `MissingInAdvice` | Settled in our system, older than `SettlementWindowDays`, not in the advice |
| `AmountMismatch` / `CurrencyMismatch` / `TypeMismatch` / `StatusMismatch` | Field differs from our record (amount within `AmountTolerance` is a match) |
| `OriginalNotFound` / `RefundExceedsOriginal` / `CancelAmountMismatch` | Engine rule failures |
| `LateSettlement` | Settled more than `SettlementWindowDays` after the transaction |

Severity is High at or above `HighSeverityAmount` and always for duplicates, control totals and
refund-over-original.

## Samples

`samples/advice/ALIPAY_SETTLE_2088000000000001_20261018.csv` contains one example of each transaction
type plus seeded breaks; `samples/internal/transactions.csv` is the matching internal export.
`samples/advice/APLUS_SETTLE_20261018.csv` is a pipe-delimited Alipay+ layout using a config-only tax type.

The Alipay column names in `appsettings.json` follow the common Alipay cross-border settlement file
layout. Confirm them against the settlement file specification in your Alipay merchant contract and
adjust the profile; no code change is needed.

## Tests and lint

```bash
dotnet test AlipaySettlement.sln
dotnet format AlipaySettlement.sln --verify-no-changes

# SFTP integration test (skipped unless SFTP_TEST_HOST is set)
docker run -d --name sftp -p 2222:22 atmoz/sftp:alpine sfg:sfgpass:::inbound
SFTP_TEST_HOST=localhost SFTP_TEST_PORT=2222 SFTP_TEST_USER=sfg SFTP_TEST_PASSWORD=sfgpass dotnet test AlipaySettlement.sln
```

## Using it with Devin

`.devin/agents/alipay-settlement.md` is a custom subagent profile with the settlement rules. Skills:

- `/run-settlement [file]`: run settlement and summarise totals and breaks
- `/investigate-break <txn-id>`: trace one break to its root cause
- `/add-advice-field <profile> <column> <name> [type]`: add a column mapping through config
- `/add-transaction-type <profile> <code> <type>`: onboard a new provider type code
