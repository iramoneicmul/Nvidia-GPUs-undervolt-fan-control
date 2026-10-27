"""Verifie l'absence de cles dupliquees dans lang.cs.

Le dictionnaire est construit par un initialiseur statique : une cle dupliquee
provoque une ArgumentException a l'initialisation, donc un plantage de
l'application au demarrage. Ce controle est appele par build.ps1.
"""
import collections
import re
import sys

path = sys.argv[1] if len(sys.argv) > 1 else "lang.cs"
source = open(path, encoding="utf-8").read()

blocks = re.split(
    r"private static readonly Dictionary<string, string> (\w+) = new Dictionary<string, string>",
    source,
)

failed = False
for i in range(1, len(blocks), 2):
    name = blocks[i]
    keys = re.findall(r'\{ "((?:[^"\\]|\\.)*)",', blocks[i + 1])
    dups = [k for k, c in collections.Counter(keys).items() if c > 1]
    if dups:
        failed = True
        print("  DOUBLON dans %s : %s" % (name, ", ".join(dups)))
    else:
        print("  %s : %d cles, aucun doublon" % (name, len(keys)))

sys.exit(1 if failed else 0)
