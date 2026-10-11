#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet restore PhotoLab.slnx
dotnet run --project src/PhotoLab.Desktop/PhotoLab.Desktop.csproj -c Release
