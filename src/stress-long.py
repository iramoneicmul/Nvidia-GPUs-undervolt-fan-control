import sys, time, torch
if not torch.cuda.is_available():
    print("CUDA indisponible"); sys.exit(2)

duree = int(sys.argv[1]) if len(sys.argv) > 1 else 600
taille = int(sys.argv[2]) if len(sys.argv) > 2 else 8192
dev = 'cuda'
a = torch.randn(taille, taille, device=dev, dtype=torch.float16)
b = torch.randn(taille, taille, device=dev, dtype=torch.float16)
for _ in range(3):
    c = a @ b
torch.cuda.synchronize()
t0 = time.time(); n = 0
print("charge", taille, "px pendant", duree, "s", flush=True)
while time.time() - t0 < duree:
    c = a @ b
    torch.cuda.synchronize()
    n += 1
print("termine:", n, "iterations en", round(time.time() - t0, 1), "s", flush=True)
