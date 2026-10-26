import torch, time, sys

duree = int(sys.argv[1]) if len(sys.argv) > 1 else 600
taille = int(sys.argv[3]) if len(sys.argv) > 3 else 8192
dev = 'cuda'

torch.backends.cuda.matmul.allow_tf32 = False

a = torch.randn(taille, taille, device=dev, dtype=torch.float16)
b = torch.randn(taille, taille, device=dev, dtype=torch.float16)
w = torch.randn(128, 64, 3, 3, device=dev, dtype=torch.float16)
x = torch.randn(2, 64, 384, 384, device=dev, dtype=torch.float16)

ref = (a @ b).clone()
torch.cuda.synchronize()

n = 0
bad = 0
maxdiff = 0.0
t0 = time.time()

print("stabilite:", duree, "s /", taille, "x", taille, flush=True)
while time.time() - t0 < duree:
    for _ in range(15):
        c = a @ b
        torch.cuda.synchronize()
    # charge graphique en alternance
    y = torch.nn.functional.conv2d(x, w, padding=1)
    torch.cuda.synchronize()

    d = (c - ref).abs().max().item()
    n += 1
    if d != 0.0:
        bad += 1
        if d > maxdiff:
            maxdiff = d

res = f"RESULTAT iterations={n} ecarts={bad} maxdiff={maxdiff:.6f}"
print(res, flush=True)
if len(sys.argv) > 2:
    try:
        open(sys.argv[2], "w").write(res + "\n")
    except Exception:
        pass
