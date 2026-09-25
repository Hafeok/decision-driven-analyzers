#!/usr/bin/env bash
#
# Proves the packaged analyzers and generator load and fire. RuleTiers.SamplesJobIsTheFalsePositiveCheck.
#
#   samples/Consumer/check-violations.sh <package-version>
#
# Expects artifacts/packages/DecisionDriven.Analyzers.<version>.nupkg to exist - in CI, the
# artifact the build job uploaded; locally, the output of
# `dotnet pack DecisionDriven.slnx -c Release -o artifacts/packages`.
#
# Four checks, each of which fails the script:
#
#   1. The package NuGet restores is byte-identical to the one in artifacts/packages. The feed
#      is added to the configured sources rather than replacing them, so a package with the same
#      id and version from anywhere else could otherwise be what gets tested.
#   2. The conforming build is silent. Warnings are errors, so it succeeding is the check.
#   3. With DD_SAMPLE_VIOLATIONS, every id in violations.expected is reported exactly once, and
#      nothing else is. Silence from step 2 is also what an analyzer that never loaded would
#      produce; this is the step that tells the two apart.
#   4. DDBUILD0001 fires when the Roslyn pin and the expected floor disagree. That check guards
#      this repository's own build and does not travel in the package, so it is run against the
#      analyzer project rather than the samples.
#
# A missing id in step 3 is a packaging defect until shown otherwise: the same rule passing its
# in-memory tests and not firing here means the nupkg is not what the tests tested.

set -euo pipefail

version="${1:?usage: check-violations.sh <package-version>}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
samples="$root/samples/Consumer"
feed="$root/artifacts/packages"
restored="$root/artifacts/samples-packages"
logs="$root/artifacts/samples-logs"

nupkg="$feed/DecisionDriven.Analyzers.$version.nupkg"
[ -f "$nupkg" ] || { echo "::error::$nupkg does not exist. Pack first."; exit 1; }

# A fresh packages folder every run: a same-version package left over from an earlier run is
# exactly the thing check 1 exists to rule out, and a stale cache would rule it in.
rm -rf "$restored" "$logs"
mkdir -p "$logs"

common=(
  --configuration Release
  -p:DdAnalyzersPackageVersion="$version"
  -p:RestorePackagesPath="$restored"
  -nologo
  -clp:NoSummary
)

# Dependency order, so that each violating build compiles against the conforming output of the
# projects it references (BuildProjectReferences=false). A project whose own violations fail
# its compile would otherwise stop every project above it from being compiled at all, and their
# findings would be missing for a reason that has nothing to do with the package.
projects=(
  Sample.Layer0
  Sample.Layer1
  Sample.Layer1.Tests
  Sample.Layer2
  Sample.Host
)

echo "== 1. conforming build from the package"
if ! dotnet build "$samples/Consumer.slnx" "${common[@]}" > "$logs/conforming.log" 2>&1; then
  cat "$logs/conforming.log"
  echo "::error::The conforming samples reported something. Every sample conforms, so this is a false positive in the rule, not in the sample (RuleTiers.SamplesJobIsTheFalsePositiveCheck)."
  exit 1
fi
echo "   silent"

echo "== 2. the restored package is the one that was built"
lower="$(echo "decisiondriven.analyzers" )"
restored_nupkg="$restored/$lower/$version/$lower.$version.nupkg"
[ -f "$restored_nupkg" ] || { echo "::error::NuGet restored no $restored_nupkg."; exit 1; }
want="$(sha512sum "$nupkg" | cut -d' ' -f1)"
got="$(sha512sum "$restored_nupkg" | cut -d' ' -f1)"
if [ "$want" != "$got" ]; then
  echo "::error::The restored DecisionDriven.Analyzers $version is not the one in artifacts/packages. Something else with that id and version was restored instead."
  exit 1
fi
echo "   sha512 ${want:0:16}... matches"

echo "== 3. violating build: each expected id exactly once"
: > "$logs/violations.log"
: > "$logs/violations.diagnostics"
for project in "${projects[@]}"; do
  # Expected to fail: every violation is an error, DD0016 and DD0017 included, because the
  # samples build with warnings as errors.
  #
  # Counted from a file logger, not the console. The console logger in this SDK prints every
  # diagnostic a second time in its summary whatever -clp says, which reads as each rule firing
  # twice; the file logger honours NoSummary, so each diagnostic is one line in it.
  dotnet build "$samples/$project/$project.csproj" "${common[@]}" \
    --no-restore \
    -p:DD_SAMPLE_VIOLATIONS=true \
    -p:BuildProjectReferences=false \
    -fl "-flp:logfile=$logs/$project.diagnostics;verbosity=quiet;NoSummary" \
    >> "$logs/violations.log" 2>&1 || true
  cat "$logs/$project.diagnostics" >> "$logs/violations.diagnostics"
done

# One line per diagnostic: "<file>(<line>,<col>): error DD0001: <message> [<project>]", or
# "CSC : error DD0001: ..." for one reported on the compilation rather than a location.
grep -oE ': (error|warning) [A-Z]+[0-9]+:' "$logs/violations.diagnostics" \
  | sed -E 's/^: (error|warning) //; s/:$//' \
  | sort | uniq -c | awk '{ print $2, $1 }' > "$logs/violations.counts"

failed=0
while read -r id; do
  [ -z "$id" ] && continue
  count="$(awk -v id="$id" '$1 == id { print $2 }' "$logs/violations.counts")"
  count="${count:-0}"
  if [ "$count" = "1" ]; then
    echo "   $id  once"
  elif [ "$count" = "0" ]; then
    echo "::error::$id was not reported. Its in-memory tests pass, so the package is not what they tested - a packaging defect until shown otherwise."
    failed=1
  else
    echo "::error::$id was reported $count times; its violating sample is meant to break it exactly once."
    failed=1
  fi
done < "$samples/violations.expected"

# Anything reported that nobody expected: a violating sample tripping a second rule, a sample that
# does not compile for an unrelated reason, or a rule that fires where it should not.
while read -r id count; do
  if ! grep -qx "$id" "$samples/violations.expected"; then
    echo "::error::$id was reported $count time(s) and is not in violations.expected."
    failed=1
  fi
done < "$logs/violations.counts"

if [ "$failed" != "0" ]; then
  echo "--- full violating build output ---"
  cat "$logs/violations.log"
  exit 1
fi

echo "== 4. DDBUILD0001 against the analyzer project"
# A deliberately wrong floor. This is the repository checking its own Roslyn pin, which is why it
# is here and not in a sample: no consumer ever sees DDBUILD0001.
if dotnet build "$root/src/DecisionDriven.Analyzers/DecisionDriven.Analyzers.csproj" \
    --configuration Release -nologo \
    -fl "-flp:logfile=$logs/ddbuild.diagnostics;verbosity=quiet;NoSummary" \
    -p:ExpectedRoslynVersion=0.0.0-sample-violation > "$logs/ddbuild.log" 2>&1; then
  echo "::error::The analyzer project built with a Roslyn floor that matches no pin; DDBUILD0001 did not fire."
  exit 1
fi
ddbuild="$(grep -cE ': error DDBUILD0001:' "$logs/ddbuild.diagnostics" || true)"
if [ "$ddbuild" != "1" ]; then
  cat "$logs/ddbuild.log"
  echo "::error::DDBUILD0001 was reported $ddbuild times; expected exactly once."
  exit 1
fi
other="$(grep -oE ': error [A-Z]+[0-9]+:' "$logs/ddbuild.diagnostics" | grep -v DDBUILD0001 || true)"
if [ -n "$other" ]; then
  cat "$logs/ddbuild.log"
  echo "::error::The DDBUILD0001 build also reported: $other"
  exit 1
fi
echo "   DDBUILD0001  once"

echo "All $(grep -c . "$samples/violations.expected") packaged ids and DDBUILD0001 fired exactly once; the conforming build is silent."
