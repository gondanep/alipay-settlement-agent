---
name: add-advice-field
description: Add or change a field mapping in an Alipay advice profile through configuration only
argument-hint: "<profile> <column> <field-name> [type]"
agent: alipay-settlement
---

Add the advice column `$2` to profile `$1` as field `$3` (type `$4`, default String).

1. Edit `Settlement:Profiles:$1:Fields` in `src/Settlement.Agent/appsettings.json`. Use a canonical name from
   `src/Settlement.Core/Configuration/CanonicalFields.cs` if the column has that meaning; otherwise a custom
   name (custom fields are carried into `entries.csv`). Set `Type`, `Format` (dates: .NET format strings,
   `|` separates alternatives), `Required`, `Default`, `Transform` and `MaxLength` as needed.
2. Update the matching sample file in `samples/advice/` if it should contain the column.
3. Add or update a parser test in `tests/Settlement.Tests/AdviceParserTests.cs`.
4. Run `validate-config`, `dotnet test` and `dotnet format --verify-no-changes`.
