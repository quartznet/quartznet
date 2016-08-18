#!/bin/sh

FAKE_VERSION="4.36.0"
FAKE_EXE="packages/FAKE.$FAKE_VERSION/tools/FAKE.exe"

echo $FAKE_EXE

mono tools/NuGet/NuGet.exe install FAKE -Version $FAKE_VERSION -OutputDirectory packages
ls -ls packages/
mono $FAKE_EXE build.fsx $@
