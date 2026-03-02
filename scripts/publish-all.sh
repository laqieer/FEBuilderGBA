#!/usr/bin/env bash
# publish-all.sh — Build self-contained cross-platform packages
# Usage: ./scripts/publish-all.sh [rid...]
# Example: ./scripts/publish-all.sh linux-x64 osx-arm64 win-x64
#
# If no RIDs specified, builds all three platforms.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(dirname "$SCRIPT_DIR")"

RIDS=("${@:-linux-x64 osx-arm64 win-x64}")
if [ $# -eq 0 ]; then
    RIDS=(linux-x64 osx-arm64 win-x64)
fi

echo "=== FEBuilderGBA Cross-Platform Publish ==="
echo "RIDs: ${RIDS[*]}"
echo ""

for RID in "${RIDS[@]}"; do
    echo "--- Publishing CLI for $RID ---"
    dotnet publish "$REPO_DIR/FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj" \
        -c Release -r "$RID" --self-contained true \
        -o "$REPO_DIR/publish/cli-$RID"

    echo "--- Publishing Avalonia for $RID ---"
    dotnet publish "$REPO_DIR/FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj" \
        -c Release -r "$RID" --self-contained true \
        -o "$REPO_DIR/publish/avalonia-$RID"

    echo "--- Creating archive for $RID ---"
    cd "$REPO_DIR/publish"
    if command -v tar &>/dev/null; then
        tar czf "FEBuilderGBA-CLI-$RID.tar.gz" "cli-$RID/"
        tar czf "FEBuilderGBA-Avalonia-$RID.tar.gz" "avalonia-$RID/"
    fi
    cd "$REPO_DIR"

    echo ""
done

echo "=== Publish complete ==="
echo "Output: $REPO_DIR/publish/"
ls -la "$REPO_DIR/publish/"*.tar.gz 2>/dev/null || echo "(no archives created — tar not available)"
