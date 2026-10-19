#!/usr/bin/env bash
# Copies the sample advice files into the local SFG inbound folder and runs one settlement pass.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p data/sfg/inbound
cp samples/advice/*.csv data/sfg/inbound/
dotnet run --project src/Settlement.Agent "$@" -- run-once
