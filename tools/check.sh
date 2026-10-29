#!/bin/bash
# Compiles the mod against the game, BepInEx and HalfGripTools without starting the game, the way HalfGripTools' check-mods.ps1 does
# (C# 10, netstandard2.1), for Linux and CI. Nothing is written to the repo.
#   tools/check.sh <game folder> <HalfGripTools.dll>
# The game folder can be the HalfGripRefs repo's game/ folder. HalfGripTools.dll comes from building HalfGripTools:
#   dotnet build HalfGripTools/HalfGripTools.csproj -c Release -p:GameDir=<game folder>
set -euo pipefail
game="${1:?game folder}"
tools="${2:?HalfGripTools.dll}"
repo="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
managed="$game/HalfGrip_Data/Managed"
cat > "$work/check.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>HalfGripMod.OpenWorld</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$repo/OpenWorld/src/**/*.cs" />
    <Reference Include="$managed/*.dll" Exclude="$managed/mscorlib.dll;$managed/netstandard.dll;$managed/System*.dll" />
    <Reference Include="$game/BepInEx/core/BepInEx.dll" />
    <Reference Include="$game/BepInEx/core/0Harmony.dll" />
    <Reference Include="$tools" />
  </ItemGroup>
</Project>
XML
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build "$work/check.csproj" -c Release -v q --nologo "-p:OutDir=$work/out/"
