using BenchmarkDotNet.Attributes;
using Robust.Shared.Analyzers;
using System.Runtime.InteropServices;
using System;
using System.Runtime.Intrinsics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Robust.Benchmarks.SIMD;

/// <summary>
///     This tries out various ways of loading a custom struct into a Vector128 for use with simd operations.
///     In this case, the simd operation is just a straightforward multiplication.
/// </summary>
[Virtual]
public class SimdLoadStructBenchmark
{
    // Using shitty hacks via a private numeric type that overlaps with other floats.
    [Benchmark(Baseline = true)] public Color NumericHacks() => Color.NumericHacks(Color.White, Color.White);

    // using pointers in fixed blocks
    [Benchmark] public Color LoadVector() => Color.LoadVector(Color.White, Color.White);

    // Using Unsafe.As()
    [Benchmark] public Color UnsafeAs() => Color.UnsafeAs(Color.White, Color.White);

    // naive multiplication.
    [Benchmark] public Color Naive() => Color.MultiplyNaive(Color.White, Color.White);
}

[Serializable] [StructLayout(LayoutKind.Explicit)]
public struct Color
{
    public static Color White = new(1, 1, 1, 1);

    [FieldOffset(sizeof(float) * 0)] public float R;
    [FieldOffset(sizeof(float) * 1)] public float G;
    [FieldOffset(sizeof(float) * 2)] public float B;
    [FieldOffset(sizeof(float) * 3)] public float A;
    [FieldOffset(sizeof(float) * 0)] Vector4 _vec4; // overlaps with RGBA

    public Color(float r, float g, float b, float a)
    {
        Unsafe.SkipInit(out this);
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public Color(in Vector4 vec)
    {
        Unsafe.SkipInit(out this);
        _vec4 = vec;
    }

    public static Color MultiplyNaive(in Color a, in Color b)
    {
        return new (a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);
    }

    public static Color NumericHacks(in Color a, in Color b)
    {
        var vecA = a._vec4.AsVector128();
        var vecB = b._vec4.AsVector128();
        return new Color(Sse.Multiply(vecA, vecB).AsVector4());
    }

    public unsafe static Color LoadVector(in Color a, in Color b)
    {
        fixed (float* aa = &a.R, bb = &b.R)
        {
            var vecA = Sse.LoadVector128(aa);
            var vecB = Sse.LoadVector128(bb);
            return new Color(Sse.Multiply(vecA, vecB).AsVector4());
        }
    }

    public unsafe static Color UnsafeAs(Color a, Color b)
    {
        var vecA = Unsafe.As<Color, Vector128<float>>(ref a);
        var vecB = Unsafe.As<Color, Vector128<float>>(ref b);
        var vecC = Sse.Multiply(vecA, vecB);
        return Unsafe.As<Vector128<float>, Color>(ref vecC);
    }
}
