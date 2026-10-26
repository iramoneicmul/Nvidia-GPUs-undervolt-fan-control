import torch, time, sys

# ---------------------------------------------------------------------
#  unlock-bench.py - debit FP16 / BF16 / TF32 / FP32 de la carte
#  Sert a chiffrer l'interet de gpu-unlock (issue rate FP/BF16).
#  Usage : unlock-bench.py [taille] [fichier-de-sortie]
# ---------------------------------------------------------------------

taille = int(sys.argv[1]) if len(sys.argv) > 1 else 8192
out = sys.argv[2] if len(sys.argv) > 2 else ""
dev = "cuda"

if not torch.cuda.is_available():
    print("CUDA indisponible")
    sys.exit(2)


def bench(dtype, tf32, secs=5, label=""):
    torch.backends.cuda.matmul.allow_tf32 = tf32
    try:
        a = torch.randn(taille, taille, device=dev, dtype=dtype)
        b = torch.randn(taille, taille, device=dev, dtype=dtype)
    except Exception as e:
        print(label + "=0.0 (allocation impossible)")
        return 0.0

    for _ in range(3):
        c = a @ b
    torch.cuda.synchronize()

    t0 = time.time()
    n = 0
    while time.time() - t0 < secs:
        c = a @ b
        torch.cuda.synchronize()
        n += 1
    dt = max(1e-6, time.time() - t0)

    flops = 2.0 * (taille ** 3) * n
    tflops = flops / dt / 1e12
    print("%s=%.2f" % (label, tflops))
    return tflops


res = {}
res["FP16"] = bench(torch.float16, False, 5, "FP16")
res["BF16"] = bench(torch.bfloat16, False, 5, "BF16")
res["TF32"] = bench(torch.float32, True, 5, "TF32")
res["FP32"] = bench(torch.float32, False, 5, "FP32")

taille_str = str(taille)
if out:
    try:
        with open(out, "w") as f:
            f.write("taille=%s\n" % taille_str)
            for k, v in res.items():
                f.write("%s=%.2f\n" % (k, v))
    except Exception:
        pass

print("RESULTAT taille=%s FP16=%.2f BF16=%.2f TF32=%.2f FP32=%.2f"
      % (taille_str, res["FP16"], res["BF16"], res["TF32"], res["FP32"]))
