#!/bin/bash
# run16.sh <tvdir> <outdir> [env...]: 16 seeds, 4 at a time
TV=$1; OUT=$2; shift 2
mkdir -p $OUT
for g in 0 4 8 12; do
  for i in 1 2 3 4; do
    n=$((g+i))
    env TV_DRIVE_ONLY=1 TV_DRIVE_NOTES=1 "$@" dotnet $TV/trafficview.dll --seed $n > $OUT/s$n.txt 2>&1 &
  done
  wait
done
echo done > $OUT/DONE
