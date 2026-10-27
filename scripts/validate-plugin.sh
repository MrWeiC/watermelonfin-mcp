#!/bin/sh
set -eu
project_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$project_dir"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/watermelonfin-dotnet-cli}"
dotnet_binary="${DOTNET_BINARY:-dotnet}"
"$dotnet_binary" build plugin/WatermelonFin.Plugin.Tests --configuration Release --disable-build-servers -p:UseSharedCompilation=false
"$dotnet_binary" plugin/WatermelonFin.Plugin.Tests/bin/Release/net10.0/WatermelonFin.Plugin.Tests.dll
"$dotnet_binary" build plugin/WatermelonFin.Plugin.AdapterTests --configuration Release --disable-build-servers -p:UseSharedCompilation=false
"$dotnet_binary" plugin/WatermelonFin.Plugin.AdapterTests/bin/Release/net10.0/WatermelonFin.Plugin.AdapterTests.dll
node --test tests/*.test.js
python3 scripts/package-plugin.py
