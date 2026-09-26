using Ossapi.Mods;
using Xunit;

namespace Ossapi.Tests.Unit;

public class ModTests
{
    [Fact]
    public void NM_has_value_zero()
        => Assert.Equal(0, Mod.NM.Value);

    [Fact]
    public void HD_has_correct_bit()
        => Assert.Equal(1 << 3, Mod.HD.Value);

    [Fact]
    public void NC_is_DT_plus_NC_bit()
        => Assert.Equal((1 << 9) | (1 << 6), Mod.NC.Value);

    [Fact]
    public void PF_is_SD_plus_PF_bit()
        => Assert.Equal((1 << 14) | (1 << 5), Mod.PF.Value);

    [Fact]
    public void Parse_HDHR()
    {
        var mod = Mod.Parse("HDHR");
        Assert.Equal(Mod.HD | Mod.HR, mod);
        Assert.Equal(24, mod.Value);
    }

    [Fact]
    public void Parse_HDDT()
    {
        var mod = Mod.Parse("HDDT");
        Assert.Equal(Mod.HDDT, mod);
        Assert.Equal(72, mod.Value);
    }

    [Fact]
    public void Parse_NC_becomes_DT_NC()
    {
        var mod = Mod.Parse("NC");
        Assert.Equal(Mod.NC, mod);
        Assert.Equal(576, mod.Value);
    }

    [Theory]
    [InlineData("HD")]
    [InlineData("HDHR")]
    [InlineData("HDDT")]
    [InlineData("NM")]
    public void ToShortName_roundtrips(string acronym)
    {
        var mod = Mod.Parse(acronym);
        Assert.Equal(acronym, mod.ToShortName());
    }

    [Fact]
    public void NC_ToShortName_is_NC_not_DTNC()
        => Assert.Equal("NC", Mod.NC.ToShortName());

    [Fact]
    public void Decompose_HDHR_gives_HD_and_HR()
    {
        var components = Mod.Parse("HDHR").Decompose(clean: true);
        Assert.Contains(Mod.HD, components);
        Assert.Contains(Mod.HR, components);
        Assert.Equal(2, components.Count);
    }

    [Fact]
    public void Decompose_NC_clean_removes_DT()
    {
        // NC.Value = 576 = _NC (512) | DT (64). Clean decompose should keep _NC and drop DT.
        var components = Mod.NC.Decompose(clean: true);
        Assert.Contains(Mod._NC, components);
        Assert.DoesNotContain(Mod.DT, components);
    }

    [Fact]
    public void Contains_works()
    {
        Assert.True(Mod.HDDT.Contains(Mod.HD));
        Assert.True(Mod.HDDT.Contains(Mod.DT));
        Assert.False(Mod.HDDT.Contains(Mod.HR));
    }

    [Fact]
    public void Or_operator_combines()
    {
        var mod = Mod.HD | Mod.HR;
        Assert.Equal(24, mod.Value);
    }

    [Fact]
    public void Subtract_operator_removes_bit()
    {
        var mod = Mod.HDHR - Mod.HR;
        Assert.Equal(Mod.HD, mod);
    }

    [Fact]
    public void Equality()
    {
        Assert.Equal(Mod.HD, Mod.Hidden);
        Assert.NotEqual(Mod.HD, Mod.HR);
    }

    [Fact]
    public void Parse_empty_throws()
        => Assert.Throws<ArgumentException>(() => Mod.Parse(""));

    [Fact]
    public void Parse_odd_length_throws()
        => Assert.Throws<ArgumentException>(() => Mod.Parse("HDH"));

    [Fact]
    public void Parse_unknown_acronym_throws()
        => Assert.Throws<ArgumentException>(() => Mod.Parse("ZZ"));
}
