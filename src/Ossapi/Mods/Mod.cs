using System.Collections.ObjectModel;

namespace Ossapi.Mods;

/// <summary>Represents an osu! mod bitmask, including compound NC and PF values.</summary>
public readonly struct Mod : IEquatable<Mod>
{
    public int Value { get; }

    public Mod(int value) => Value = value;

    // --- individual bit constants ---
    public static readonly Mod NM  = new(0);
    public static readonly Mod NF  = new(1 << 0);
    public static readonly Mod EZ  = new(1 << 1);
    public static readonly Mod TD  = new(1 << 2);
    public static readonly Mod HD  = new(1 << 3);
    public static readonly Mod HR  = new(1 << 4);
    public static readonly Mod SD  = new(1 << 5);
    public static readonly Mod DT  = new(1 << 6);
    public static readonly Mod RX  = new(1 << 7);
    public static readonly Mod HT  = new(1 << 8);
    public static readonly Mod _NC = new(1 << 9);
    public static readonly Mod FL  = new(1 << 10);
    public static readonly Mod AT  = new(1 << 11);
    public static readonly Mod SO  = new(1 << 12);
    public static readonly Mod AP  = new(1 << 13);
    public static readonly Mod _PF = new(1 << 14);
    public static readonly Mod K4  = new(1 << 15);
    public static readonly Mod K5  = new(1 << 16);
    public static readonly Mod K6  = new(1 << 17);
    public static readonly Mod K7  = new(1 << 18);
    public static readonly Mod K8  = new(1 << 19);
    public static readonly Mod FI  = new(1 << 20);
    public static readonly Mod RD  = new(1 << 21);
    public static readonly Mod CN  = new(1 << 22);
    public static readonly Mod TP  = new(1 << 23);
    public static readonly Mod K9  = new(1 << 24);
    public static readonly Mod CO  = new(1 << 25);
    public static readonly Mod K1  = new(1 << 26);
    public static readonly Mod K3  = new(1 << 27);
    public static readonly Mod K2  = new(1 << 28);
    public static readonly Mod V2  = new(1 << 29);
    public static readonly Mod MR  = new(1 << 30);

    // long-name aliases
    public static readonly Mod NoMod       = NM;
    public static readonly Mod NoFail      = NF;
    public static readonly Mod Easy        = EZ;
    public static readonly Mod TouchDevice = TD;
    public static readonly Mod Hidden      = HD;
    public static readonly Mod HardRock    = HR;
    public static readonly Mod SuddenDeath = SD;
    public static readonly Mod DoubleTime  = DT;
    public static readonly Mod Relax       = RX;
    public static readonly Mod HalfTime    = HT;
    public static readonly Mod _Nightcore  = _NC;
    public static readonly Mod Flashlight  = FL;
    public static readonly Mod Autoplay    = AT;
    public static readonly Mod SpunOut     = SO;
    public static readonly Mod Autopilot   = AP;
    public static readonly Mod _Perfect    = _PF;
    public static readonly Mod Key4        = K4;
    public static readonly Mod Key5        = K5;
    public static readonly Mod Key6        = K6;
    public static readonly Mod Key7        = K7;
    public static readonly Mod Key8        = K8;
    public static readonly Mod FadeIn      = FI;
    public static readonly Mod Random      = RD;
    public static readonly Mod Cinema      = CN;
    public static readonly Mod Target      = TP;
    public static readonly Mod Key9        = K9;
    public static readonly Mod KeyCoop     = CO;
    public static readonly Mod Key1        = K1;
    public static readonly Mod Key3        = K3;
    public static readonly Mod Key2        = K2;
    public static readonly Mod ScoreV2     = V2;
    public static readonly Mod Mirror      = MR;

    // compound mods — NC and PF include their companion bits (in-game behavior)
    public static readonly Mod NC = new(_NC.Value | DT.Value);
    public static readonly Mod PF = new(_PF.Value | SD.Value);
    public static readonly Mod Nightcore = NC;
    public static readonly Mod Perfect   = PF;

    // key mod mask
    public static readonly Mod KM     = new(K1.Value | K2.Value | K3.Value | K4.Value | K5.Value | K6.Value | K7.Value | K8.Value | K9.Value | CO.Value);
    public static readonly Mod KeyMod = KM;

    // common combinations
    public static readonly Mod HDDT   = new(HD.Value | DT.Value);
    public static readonly Mod HDHR   = new(HD.Value | HR.Value);
    public static readonly Mod HDDTHR = new(HD.Value | DT.Value | HR.Value);

    // ordered list matching Python Mod.ORDER (used for decompose ordering)
    public static readonly ReadOnlyCollection<Mod> Order = new([
        NM, EZ, HD, HT, DT, _NC, HR, FL, NF, SD, _PF, RX, AP, SO, AT, V2, TD,
        FI, RD, CN, TP, K1, K2, K3, K4, K5, K6, K7, K8, K9, CO, MR
    ]);

    // mapping from bit value to (acronym, long name)
    private static readonly Dictionary<int, (string Short, string Long)> IntToMod = new()
    {
        [0]       = ("NM", "NoMod"),
        [1 << 0]  = ("NF", "NoFail"),
        [1 << 1]  = ("EZ", "Easy"),
        [1 << 2]  = ("TD", "TouchDevice"),
        [1 << 3]  = ("HD", "Hidden"),
        [1 << 4]  = ("HR", "HardRock"),
        [1 << 5]  = ("SD", "SuddenDeath"),
        [1 << 6]  = ("DT", "DoubleTime"),
        [1 << 7]  = ("RX", "Relax"),
        [1 << 8]  = ("HT", "HalfTime"),
        [1 << 9]  = ("NC", "Nightcore"),
        [1 << 10] = ("FL", "Flashlight"),
        [1 << 11] = ("AT", "Autoplay"),
        [1 << 12] = ("SO", "SpunOut"),
        [1 << 13] = ("AP", "Autopilot"),
        [1 << 14] = ("PF", "Perfect"),
        [1 << 15] = ("4K", "Key4"),
        [1 << 16] = ("5K", "Key5"),
        [1 << 17] = ("6K", "Key6"),
        [1 << 18] = ("7K", "Key7"),
        [1 << 19] = ("8K", "Key8"),
        [1 << 20] = ("FI", "FadeIn"),
        [1 << 21] = ("RD", "Random"),
        [1 << 22] = ("CN", "Cinema"),
        [1 << 23] = ("TP", "Target"),
        [1 << 24] = ("9K", "Key9"),
        [1 << 25] = ("CO", "KeyCoop"),
        [1 << 26] = ("1K", "Key1"),
        [1 << 27] = ("3K", "Key3"),
        [1 << 28] = ("2K", "Key2"),
        [1 << 29] = ("V2", "ScoreV2"),
        [1 << 30] = ("MR", "Mirror"),
    };

    private static readonly Dictionary<string, Mod> ShortNameToMod;

    static Mod()
    {
        ShortNameToMod = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
        foreach (var (bit, names) in IntToMod)
            ShortNameToMod[names.Short] = new Mod(bit);
    }

    /// <summary>Parses a two-letter-acronym string like "HDHR" or "DTNC".</summary>
    public static Mod Parse(string s)
    {
        if (string.IsNullOrEmpty(s))
            throw new ArgumentException("Mod string cannot be empty.", nameof(s));
        if (s.Length % 2 != 0)
            throw new ArgumentException($"Mod string '{s}' has odd length.", nameof(s));

        var result = NM;
        for (var i = 0; i < s.Length; i += 2)
        {
            var acronym = s.Substring(i, 2);
            if (!ShortNameToMod.TryGetValue(acronym, out var m))
                throw new ArgumentException($"Unknown mod acronym '{acronym}' in '{s}'.", nameof(s));
            // NC/PF as typed by users mean the compound versions
            if (m == _NC) m = NC;
            if (m == _PF) m = PF;
            result |= m;
        }
        return result;
    }

    /// <summary>Returns the two-letter acronym string, e.g. "HDHR".</summary>
    public string ToShortName()
    {
        if (IntToMod.TryGetValue(Value, out var exact))
            return exact.Short;
        return string.Concat(Decompose(clean: true).Select(m => IntToMod[m.Value].Short));
    }

    /// <summary>Returns the long name string, e.g. "Hidden HardRock".</summary>
    public string ToLongName()
    {
        if (IntToMod.TryGetValue(Value, out var exact))
            return exact.Long;
        return string.Join(" ", Decompose(clean: true).Select(m => IntToMod[m.Value].Long));
    }

    /// <summary>Returns ordered atomic mods; clean mode removes DT from NC and SD from PF.</summary>
    public IReadOnlyList<Mod> Decompose(bool clean = false)
    {
        var value = Value;
        var components = IntToMod.Keys
            .Where(bit => bit != 0 && (value & bit) != 0)
            .Select(bit => new Mod(bit))
            .ToHashSet();

        var ordered = Order.Where(m => components.Contains(m)).ToList();

        if (clean)
        {
            if (ordered.Contains(_NC) && ordered.Contains(DT)) ordered.Remove(DT);
            if (ordered.Contains(_PF) && ordered.Contains(SD)) ordered.Remove(SD);
        }

        return ordered;
    }

    public bool Contains(Mod other) => (Value & other.Value) == other.Value;

    public static Mod operator |(Mod a, Mod b) => new(a.Value | b.Value);
    public static Mod operator &(Mod a, Mod b) => new(a.Value & b.Value);
    public static Mod operator ~(Mod m)        => new(~m.Value);
    public static Mod operator -(Mod a, Mod b) => new(a.Value & ~b.Value);

    public static bool operator ==(Mod a, Mod b) => a.Value == b.Value;
    public static bool operator !=(Mod a, Mod b) => a.Value != b.Value;

    public bool Equals(Mod other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Mod m && Equals(m);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => ToShortName();
}
