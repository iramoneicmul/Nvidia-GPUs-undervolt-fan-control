// =====================================================================
//  OpenClTest.cs  -  charge GPU et verification de calcul, sans Python
//
//  Utilise OpenCL (OpenCL.dll, fourni par le pilote NVIDIA).
//  Kernel de multiplication de matrices en int32 : le resultat est
//  deterministe, ce qui permet une comparaison bit a bit a chaque
//  execution. Toute difference signale une erreur de calcul materielle.
// =====================================================================
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal static class OpenClTest
{
    private const string KernelSource = @"
__kernel void burn(__global float* out, const int iters)
{
    float a = (float)get_global_id(0) * 0.00001f;
    float b = a + 1.0f, c = a + 2.0f, d = a + 3.0f;
    for (int i = 0; i < iters; i++)
    {
        a = fma(a, 1.000001f, 0.1f);
        b = fma(b, 1.000001f, 0.2f);
        c = fma(c, 1.000001f, 0.3f);
        d = fma(d, 1.000001f, 0.4f);
    }
    out[get_global_id(0)] = a + b + c + d;
}

__kernel void mmul_int(__global const int* A,
                       __global const int* B,
                       __global int* C,
                       const int N)
{
    int i = get_global_id(0);
    int j = get_global_id(1);
    int sum = 0;
    int base = i * N;
    for (int k = 0; k < N; k++)
        sum += A[base + k] * B[k * N + j];
    C[base + j] = sum;
}
";

    // ---- OpenCL interop ----
    private const uint CL_DEVICE_TYPE_GPU = 1 << 2;
    private const ulong CL_MEM_READ_ONLY = 1 << 2;
    private const ulong CL_MEM_WRITE_ONLY = 1 << 1;
    private const ulong CL_MEM_COPY_HOST_PTR = 1 << 5;

    [DllImport("OpenCL.dll")] private static extern int clGetPlatformIDs(uint n, IntPtr[] p, out uint c);
    [DllImport("OpenCL.dll")] private static extern int clGetDeviceIDs(IntPtr p, uint t, uint n, IntPtr[] d, out uint c);
    [DllImport("OpenCL.dll")] private static extern IntPtr clCreateContext(IntPtr props, uint n, IntPtr[] d, IntPtr cb, IntPtr data, out int err);
    [DllImport("OpenCL.dll")] private static extern IntPtr clCreateCommandQueue(IntPtr ctx, IntPtr dev, ulong props, out int err);
    [DllImport("OpenCL.dll")] private static extern IntPtr clCreateProgramWithSource(IntPtr ctx, uint n, string[] s, IntPtr len, out int err);
    [DllImport("OpenCL.dll")] private static extern int clBuildProgram(IntPtr prog, uint n, IntPtr[] dev, string opt, IntPtr cb, IntPtr data);
    [DllImport("OpenCL.dll")] private static extern IntPtr clCreateKernel(IntPtr prog, string name, out int err);
    [DllImport("OpenCL.dll")] private static extern IntPtr clCreateBuffer(IntPtr ctx, ulong flags, IntPtr size, IntPtr host, out int err);
    [DllImport("OpenCL.dll")] private static extern int clSetKernelArg(IntPtr k, uint idx, IntPtr size, IntPtr val);
    [DllImport("OpenCL.dll")] private static extern int clEnqueueNDRangeKernel(IntPtr q, IntPtr k, uint dim, IntPtr[] off, IntPtr[] glob, IntPtr[] loc, uint nw, IntPtr[] ev, IntPtr evt);
    [DllImport("OpenCL.dll")] private static extern int clEnqueueReadBuffer(IntPtr q, IntPtr b, uint block, IntPtr off, IntPtr size, IntPtr ptr, uint nw, IntPtr[] ev, IntPtr evt);
    [DllImport("OpenCL.dll")] private static extern int clEnqueueWriteBuffer(IntPtr q, IntPtr b, uint block, IntPtr off, IntPtr size, IntPtr ptr, uint nw, IntPtr[] ev, IntPtr evt);
    [DllImport("OpenCL.dll")] private static extern int clFinish(IntPtr q);
    [DllImport("OpenCL.dll")] private static extern int clReleaseMemObject(IntPtr o);
    [DllImport("OpenCL.dll")] private static extern int clReleaseKernel(IntPtr k);
    [DllImport("OpenCL.dll")] private static extern int clReleaseProgram(IntPtr p);
    [DllImport("OpenCL.dll")] private static extern int clReleaseCommandQueue(IntPtr q);
    [DllImport("OpenCL.dll")] private static extern int clReleaseContext(IntPtr c);

    public static bool Available
    {
        get
        {
            try
            {
                var pf = new IntPtr[4];
                uint n;
                return clGetPlatformIDs(4, pf, out n) == 0 && n > 0;
            }
            catch { return false; }
        }
    }

    /// <summary>Charge + verification pendant la duree demandee.</summary>
    public static string Run(int seconds, int n = 1024, Action<string> log = null)
    {
        IntPtr ctx = IntPtr.Zero, queue = IntPtr.Zero, prog = IntPtr.Zero, kernel = IntPtr.Zero;
        IntPtr bufA = IntPtr.Zero, bufB = IntPtr.Zero, bufC = IntPtr.Zero;
        GCHandle hA = default(GCHandle), hB = default(GCHandle);
        IntPtr burn = IntPtr.Zero, burnBuf = IntPtr.Zero;
        try
        {
            var platforms = new IntPtr[4];
            uint pcount;
            if (clGetPlatformIDs(4, platforms, out pcount) != 0 || pcount == 0)
                return "OpenCL indisponible";

            var devices = new IntPtr[4];
            uint dcount;
            if (clGetDeviceIDs(platforms[0], CL_DEVICE_TYPE_GPU, 4, devices, out dcount) != 0 || dcount == 0)
                return "aucun peripherique OpenCL GPU";

            int err;
            ctx = clCreateContext(IntPtr.Zero, 1, devices, IntPtr.Zero, IntPtr.Zero, out err);
            if (ctx == IntPtr.Zero) return "contexte OpenCL : " + err;

            queue = clCreateCommandQueue(ctx, devices[0], 0, out err);
            if (queue == IntPtr.Zero) return "queue OpenCL : " + err;

            prog = clCreateProgramWithSource(ctx, 1, new[] { KernelSource }, IntPtr.Zero, out err);
            if (prog == IntPtr.Zero) return "programme OpenCL : " + err;

            if (clBuildProgram(prog, 1, devices, "", IntPtr.Zero, IntPtr.Zero) != 0)
                return "compilation OpenCL impossible";

            kernel = clCreateKernel(prog, "mmul_int", out err);
            if (kernel == IntPtr.Zero) return "kernel OpenCL : " + err;

            burn = clCreateKernel(prog, "burn", out err);
            if (burn != IntPtr.Zero)
            {
                burnBuf = clCreateBuffer(ctx, CL_MEM_WRITE_ONLY, (IntPtr)(1 << 20), IntPtr.Zero, out err);
            }

            // donnees deterministes
            var rnd = new Random(12345);
            var a = new int[n * n];
            var b = new int[n * n];
            for (int i = 0; i < a.Length; i++) { a[i] = rnd.Next(0, 16); b[i] = rnd.Next(0, 16); }

            int bytes = n * n * sizeof(int);
            hA = GCHandle.Alloc(a, GCHandleType.Pinned);
            hB = GCHandle.Alloc(b, GCHandleType.Pinned);

            bufA = clCreateBuffer(ctx, CL_MEM_READ_ONLY | CL_MEM_COPY_HOST_PTR, (IntPtr)bytes, hA.AddrOfPinnedObject(), out err);
            bufB = clCreateBuffer(ctx, CL_MEM_READ_ONLY | CL_MEM_COPY_HOST_PTR, (IntPtr)bytes, hB.AddrOfPinnedObject(), out err);
            bufC = clCreateBuffer(ctx, CL_MEM_WRITE_ONLY, (IntPtr)bytes, IntPtr.Zero, out err);
            IntPtr pA = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr pB = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr pC = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr pN = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteIntPtr(pA, bufA);
            Marshal.WriteIntPtr(pB, bufB);
            Marshal.WriteIntPtr(pC, bufC);
            Marshal.WriteInt32(pN, n);

            clSetKernelArg(kernel, 0, (IntPtr)IntPtr.Size, pA);
            clSetKernelArg(kernel, 1, (IntPtr)IntPtr.Size, pB);
            clSetKernelArg(kernel, 2, (IntPtr)IntPtr.Size, pC);
            clSetKernelArg(kernel, 3, (IntPtr)sizeof(int), pN);

            Marshal.FreeHGlobal(pA);
            Marshal.FreeHGlobal(pB);
            Marshal.FreeHGlobal(pC);
            Marshal.FreeHGlobal(pN);

            var global = new[] { (IntPtr)n, (IntPtr)n };
            var c = new int[n * n];
            var reference = new int[n * n];
            var pinnedC = GCHandle.Alloc(c, GCHandleType.Pinned);

            // premiere execution = reference
            clEnqueueNDRangeKernel(queue, kernel, 2, null, global, null, 0, null, IntPtr.Zero);
            clFinish(queue);
            clEnqueueReadBuffer(queue, bufC, 1, IntPtr.Zero, (IntPtr)bytes, pinnedC.AddrOfPinnedObject(), 0, null, IntPtr.Zero);
            Array.Copy(c, reference, c.Length);

            var sw = Stopwatch.StartNew();
            int iterations = 0, bad = 0;
            long maxdiff = 0;

            while (sw.Elapsed.TotalSeconds < seconds)
            {
                if (burn != IntPtr.Zero && burnBuf != IntPtr.Zero)
                {
                    IntPtr bp = Marshal.AllocHGlobal(IntPtr.Size);
                    IntPtr ip = Marshal.AllocHGlobal(sizeof(int));
                    Marshal.WriteIntPtr(bp, burnBuf);
                    Marshal.WriteInt32(ip, 8000);
                    clSetKernelArg(burn, 0, (IntPtr)IntPtr.Size, bp);
                    clSetKernelArg(burn, 1, (IntPtr)sizeof(int), ip);
                    clEnqueueNDRangeKernel(queue, burn, 1, null, new[] { (IntPtr)262144 }, null, 0, null, IntPtr.Zero);
                    Marshal.FreeHGlobal(bp);
                    Marshal.FreeHGlobal(ip);
                }

                clEnqueueNDRangeKernel(queue, kernel, 2, null, global, null, 0, null, IntPtr.Zero);
                clFinish(queue);
                iterations++;

                if (iterations % 5 == 0)
                {
                    clEnqueueReadBuffer(queue, bufC, 1, IntPtr.Zero, (IntPtr)bytes, pinnedC.AddrOfPinnedObject(), 0, null, IntPtr.Zero);
                    long diff = 0;
                    for (int i = 0; i < c.Length; i++)
                    {
                        long d = Math.Abs((long)c[i] - reference[i]);
                        if (d > diff) diff = d;
                    }
                    if (diff != 0) { bad++; if (diff > maxdiff) maxdiff = diff; }
                    if (log != null && iterations % 50 == 0) log(iterations + " executions, ecart max " + maxdiff);
                }
            }

            return string.Format("RESULTAT moteur=opencl iterations={0} ecarts={1} maxdiff={2}", iterations, bad, maxdiff);
        }
        catch (Exception e)
        {
            return "OpenCL erreur : " + e.Message;
        }
        finally
        {
            try { if (bufA != IntPtr.Zero) clReleaseMemObject(bufA); } catch { }
            try { if (bufB != IntPtr.Zero) clReleaseMemObject(bufB); } catch { }
            try { if (bufC != IntPtr.Zero) clReleaseMemObject(bufC); } catch { }
            try { if (kernel != IntPtr.Zero) clReleaseKernel(kernel); } catch { }
            try { if (burnBuf != IntPtr.Zero) clReleaseMemObject(burnBuf); } catch { }
            try { if (prog != IntPtr.Zero) clReleaseProgram(prog); } catch { }
            try { if (queue != IntPtr.Zero) clReleaseCommandQueue(queue); } catch { }
            try { if (ctx != IntPtr.Zero) clReleaseContext(ctx); } catch { }
            try { if (hA.IsAllocated) hA.Free(); } catch { }
            try { if (hB.IsAllocated) hB.Free(); } catch { }
        }
    }
}
