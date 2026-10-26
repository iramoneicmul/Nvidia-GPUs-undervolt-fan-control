#!/usr/bin/env bash
# =====================================================================
#  sign-release.sh - empreintes et signature GPG d'une release
#
#  Usage : ./sign-release.sh <repertoire-des-binaires>
#
#  Produit :
#    SHA256SUMS        liste des empreintes
#    SHA256SUMS.asc    signature GPG (si une cle est disponible)
#
#  Sans cle GPG, le script produit uniquement les empreintes et le signale.
# =====================================================================
set -euo pipefail

DIR="${1:-.}"

if [ ! -d "$DIR" ]; then
    echo "repertoire introuvable : $DIR" >&2
    exit 1
fi

cd "$DIR"

FILES=()
for f in nvidiaconf.exe nvidiaconf.msi; do
    [ -f "$f" ] && FILES+=("$f")
done

if [ ${#FILES[@]} -eq 0 ]; then
    echo "aucun binaire a signer dans $DIR" >&2
    exit 1
fi

echo "== empreintes =="
sha256sum "${FILES[@]}" | tee SHA256SUMS

if command -v gpg >/dev/null 2>&1 && gpg --list-secret-keys 2>/dev/null | grep -q '^sec'; then
    rm -f SHA256SUMS.asc
    gpg --armor --detach-sign --output SHA256SUMS.asc SHA256SUMS
    echo
    echo "== signature =="
    gpg --verify SHA256SUMS.asc SHA256SUMS
    echo "signature ecrite : $DIR/SHA256SUMS.asc"
else
    echo
    echo "aucune cle GPG disponible : empreintes seules."
    echo "Pour signer : gpg --gen-key puis relancer ce script."
fi
