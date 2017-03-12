#!/bin/sh

FAKE_VERSION="4.53.0"

mono tools/NuGet/NuGet.exe install FAKE -Version $FAKE_VERSION -OutputDirectory packages -ExcludeVersion
mono packages/FAKE/tools/FAKE.exe build.fsx $@
