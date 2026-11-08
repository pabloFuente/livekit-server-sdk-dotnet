#!/usr/bin/env bash
# Runs the tests with code coverage, writes the reports to coverage/report (index.html, the merged
# Cobertura.xml, SummaryGithub.md, Summary.txt) and checks the minimums of coverage/thresholds.json,
# which also writes the README coverage badges of the SDKs to coverage/report/badges.
# Arguments go to `dotnet test`. CI (dotnet.yml) runs it on the full suite, which needs Docker:
#   ./coverage.sh                                            # what CI checks
#   ./coverage.sh LivekitApi.Tests --filter "Category=Unit"  # a subset: report only, gate fails
set -uo pipefail
cd "$(dirname "$0")"

rm -rf coverage/raw coverage/report
dotnet test "$@" --collect "XPlat Code Coverage" --results-directory coverage/raw
tests=$?

dotnet tool restore >/dev/null || exit 1
dotnet reportgenerator -reports:'coverage/raw/*/coverage.cobertura.xml' -targetdir:coverage/report \
  -reporttypes:'Html;Cobertura;MarkdownSummaryGithub;TextSummary' -verbosity:Warning || exit 1
summary="${GITHUB_STEP_SUMMARY:-/dev/null}"

if [ "$tests" -ne 0 ]; then
  cat coverage/report/SummaryGithub.md >> "$summary"
  echo "Tests failed, so the coverage minimums were not checked." >&2
  exit "$tests"
fi
python3 coverage/check_coverage.py | tee -a "$summary"
gate=$?
cat coverage/report/SummaryGithub.md >> "$summary"
echo "Coverage report: coverage/report/index.html"
exit "$gate"
