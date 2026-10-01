using System.Runtime.InteropServices;

namespace HitCircleAPI.rosu_pp;

public sealed class Beatmap : IDisposable
{
    internal IntPtr Handle;

    private Beatmap(IntPtr handle) => Handle = handle;

    public static Beatmap FromPath(string path)
    {
        var h = Native.beatmap_from_path(path);
        if (h == IntPtr.Zero) throw new Exception($"Failed to load beatmap: {path}");
        return new Beatmap(h);
    }

    public static unsafe Beatmap FromBytes(byte[] data)
    {
        fixed (byte* ptr = data)
        {
            var h = Native.beatmap_from_bytes(ptr, (nuint)data.Length);
            if (h == IntPtr.Zero) throw new Exception("Failed to parse beatmap from bytes");
            return new Beatmap(h);
        }
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) { Native.beatmap_free(Handle); Handle = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }
    ~Beatmap() => Dispose();
}

public sealed class Difficulty : IDisposable
{
    private IntPtr _h;

    public Difficulty() => _h = Native.difficulty_new();

    public Difficulty Mods(uint mods)           { Native.difficulty_mods(_h, mods);                   return this; }
    public Difficulty ClockRate(double rate)    { Native.difficulty_clock_rate(_h, rate);              return this; }
    public Difficulty PassedObjects(uint n)     { Native.difficulty_passed_objects(_h, n);             return this; }
    public Difficulty Ar(float ar, bool fixed_) { Native.difficulty_ar(_h, ar, fixed_);               return this; }
    public Difficulty Cs(float cs, bool fixed_) { Native.difficulty_cs(_h, cs, fixed_);               return this; }
    public Difficulty Hp(float hp, bool fixed_) { Native.difficulty_hp(_h, hp, fixed_);               return this; }
    public Difficulty Od(float od, bool fixed_) { Native.difficulty_od(_h, od, fixed_);               return this; }
    public Difficulty Lazer(bool lazer)         { Native.difficulty_lazer(_h, lazer);                 return this; }

    public DifficultyAttrs Calculate(Beatmap map)
    {
        var attrs = Native.difficulty_calculate(_h, map.Handle);
        if (attrs == IntPtr.Zero) throw new Exception("difficulty_calculate returned null");
        return new DifficultyAttrs(attrs);
    }

    public void Dispose()
    {
        if (_h != IntPtr.Zero) { Native.difficulty_free(_h); _h = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }
    ~Difficulty() => Dispose();
}

public sealed class DifficultyAttrs : IDisposable
{
    internal IntPtr Handle;

    internal DifficultyAttrs(IntPtr h) => Handle = h;

    public double Stars    => Native.difficulty_attrs_stars(Handle);
    public uint MaxCombo   => Native.difficulty_attrs_max_combo(Handle);
    /// <summary>0=osu, 1=taiko, 2=catch, 3=mania</summary>
    public byte Mode       => Native.difficulty_attrs_mode(Handle);

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) { Native.difficulty_attrs_free(Handle); Handle = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }
    ~DifficultyAttrs() => Dispose();
}

public sealed class Performance : IDisposable
{
    private IntPtr _h;

    public Performance(Beatmap map)        => _h = Native.performance_from_beatmap(map.Handle);
    /// <summary>Takes ownership of attrs — do not use attrs after this.</summary>
    public Performance(DifficultyAttrs attrs)
    {
        _h = Native.performance_from_attrs(attrs.Handle);
        attrs.Handle = IntPtr.Zero; // transferred
    }

    public Performance Mods(uint mods)              { Native.performance_mods(_h, mods);                     return this; }
    public Performance Accuracy(double acc)         { Native.performance_accuracy(_h, acc);                  return this; }
    public Performance Combo(uint combo)            { Native.performance_combo(_h, combo);                   return this; }
    public Performance N300(uint n)                 { Native.performance_n300(_h, n);                        return this; }
    public Performance N100(uint n)                 { Native.performance_n100(_h, n);                        return this; }
    public Performance N50(uint n)                  { Native.performance_n50(_h, n);                         return this; }
    public Performance Misses(uint n)               { Native.performance_misses(_h, n);                      return this; }
    public Performance NGeki(uint n)                { Native.performance_n_geki(_h, n);                      return this; }
    public Performance NKatu(uint n)                { Native.performance_n_katu(_h, n);                      return this; }
    public Performance PassedObjects(uint n)        { Native.performance_passed_objects(_h, n);              return this; }
    public Performance ClockRate(double rate)       { Native.performance_clock_rate(_h, rate);               return this; }
    public Performance Lazer(bool lazer)            { Native.performance_lazer(_h, lazer);                   return this; }

    /// <summary>Consumes this handle and returns the result. Do not use after calling.</summary>
    public PerformanceResult Calculate()
    {
        var result = Native.performance_calculate(_h);
        _h = IntPtr.Zero; // consumed by Rust
        return result;
    }

    public void Dispose()
    {
        if (_h != IntPtr.Zero) { Native.performance_free(_h); _h = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }
    ~Performance() => Dispose();
}

[StructLayout(LayoutKind.Sequential)]
public struct PerformanceResult
{
    public double Pp;
    public double Stars;
    public double PpAim;
    public double PpSpeed;
    public double PpAcc;
    public uint MaxCombo;
    /// <summary>0=osu, 1=taiko, 2=catch, 3=mania</summary>
    public byte Mode;
}

internal static unsafe class Native
{
    private const string Lib = "rosu_pp_ffi";

    [DllImport(Lib)] internal static extern IntPtr beatmap_from_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] internal static extern IntPtr beatmap_from_bytes(byte* data, nuint len);
    [DllImport(Lib)] internal static extern void   beatmap_free(IntPtr h);

    [DllImport(Lib)] internal static extern IntPtr difficulty_new();
    [DllImport(Lib)] internal static extern void   difficulty_free(IntPtr h);
    [DllImport(Lib)] internal static extern void   difficulty_mods(IntPtr h, uint mods);
    [DllImport(Lib)] internal static extern void   difficulty_clock_rate(IntPtr h, double rate);
    [DllImport(Lib)] internal static extern void   difficulty_passed_objects(IntPtr h, uint n);
    [DllImport(Lib)] internal static extern void   difficulty_ar(IntPtr h, float ar, bool fixed_);
    [DllImport(Lib)] internal static extern void   difficulty_cs(IntPtr h, float cs, bool fixed_);
    [DllImport(Lib)] internal static extern void   difficulty_hp(IntPtr h, float hp, bool fixed_);
    [DllImport(Lib)] internal static extern void   difficulty_od(IntPtr h, float od, bool fixed_);
    [DllImport(Lib)] internal static extern void   difficulty_lazer(IntPtr h, bool lazer);
    [DllImport(Lib)] internal static extern IntPtr difficulty_calculate(IntPtr diff, IntPtr map);

    [DllImport(Lib)] internal static extern void   difficulty_attrs_free(IntPtr h);
    [DllImport(Lib)] internal static extern double difficulty_attrs_stars(IntPtr h);
    [DllImport(Lib)] internal static extern uint   difficulty_attrs_max_combo(IntPtr h);
    [DllImport(Lib)] internal static extern byte   difficulty_attrs_mode(IntPtr h);

    [DllImport(Lib)] internal static extern IntPtr            performance_from_beatmap(IntPtr map);
    [DllImport(Lib)] internal static extern IntPtr            performance_from_attrs(IntPtr attrs);
    [DllImport(Lib)] internal static extern void              performance_free(IntPtr h);
    [DllImport(Lib)] internal static extern void              performance_mods(IntPtr h, uint mods);
    [DllImport(Lib)] internal static extern void              performance_accuracy(IntPtr h, double acc);
    [DllImport(Lib)] internal static extern void              performance_combo(IntPtr h, uint combo);
    [DllImport(Lib)] internal static extern void              performance_n300(IntPtr h, uint n);
    [DllImport(Lib)] internal static extern void              performance_n100(IntPtr h, uint n);
    [DllImport(Lib)] internal static extern void              performance_n50(IntPtr h, uint n);
    [DllImport(Lib)] internal static extern void              performance_misses(IntPtr h, uint n);
    [DllImport(Lib)] internal static extern void              performance_n_geki(IntPtr h, uint n);
    [DllImport(Lib)] internal static extern void              performance_n_katu(IntPtr h, uint n);
    [DllImport(Lib)] internal static extern void              performance_passed_objects(IntPtr h, uint n);
    [DllImport(Lib)] internal static extern void              performance_clock_rate(IntPtr h, double rate);
    [DllImport(Lib)] internal static extern void              performance_lazer(IntPtr h, bool lazer);
    [DllImport(Lib, EntryPoint = "performance_calculate_v2")] internal static extern PerformanceResult performance_calculate(IntPtr h);
}
