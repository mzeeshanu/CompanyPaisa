using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

public class RegionTests
{
    [Theory]
    [InlineData("Texas", "US-TX")]
    [InlineData("tx", "US-TX")]
    [InlineData("CA", "US-CA")]
    [InlineData("Ontario", "CA-ON")]
    [InlineData("Québec", "CA-QC")]
    [InlineData("new-york-state", "US-NY")]
    [InlineData("NY", "US-NY")]
    [InlineData("Canada", "CA")]
    [InlineData("USA", "US")]
    [InlineData("united-states", "US")]
    [InlineData("UK", "UK")]
    [InlineData("Great Britain", "UK")]
    [InlineData("Holland", "NL")]
    [InlineData("NL", "NL")]
    [InlineData("Pakistan", "PK")]
    [InlineData("US-TX", "US-TX")]
    public void Names_and_codes_people_type_find_the_region(string typed, string code) =>
        Assert.Equal(code, Regions.Find(typed)?.Code);

    [Theory]
    [InlineData("Washington, USA", "US-WA")]
    [InlineData("washington us", "US-WA")]
    [InlineData("Washington, United States", "US-WA")]
    [InlineData("New York, USA", "US-NY")]
    [InlineData("Georgia, USA", "US-GA")]
    [InlineData("Ontario, Canada", "CA-ON")]
    [InlineData("ON, Canada", "CA-ON")]
    [InlineData("Newfoundland, Canada", "CA-NL")]
    public void A_country_after_the_name_picks_its_state_or_province(string typed, string code) =>
        Assert.Equal(code, Regions.Find(typed)?.Code);

    [Fact]
    public void City_names_with_a_country_split_into_the_city_and_the_country()
    {
        Assert.Equal(("Dallas", "US"), Regions.SplitCountry("Dallas, USA") is var (city, country) ? (city, country.Code) : default);
        Assert.Equal(("Toronto", "CA"), Regions.SplitCountry("Toronto Canada") is var (c2, k2) ? (c2, k2.Code) : default);
        Assert.Null(Regions.SplitCountry("USA"));
        Assert.Null(Regions.SplitCountry("Washington DC"));
        Assert.Null(Regions.Find("Dallas, USA"));                     // a city, not a state: the city lookup takes it
        Assert.Equal("US-WA", Regions.StateAlsoNamed("Washington")?.Code);
        Assert.Null(Regions.StateAlsoNamed("Texas"));
    }

    [Theory]
    [InlineData("New York")]
    [InlineData("Washington")]
    [InlineData("Lehi")]
    [InlineData("84043")]
    [InlineData("SW1A 1AA")]
    [InlineData("")]
    public void Cities_and_postcodes_are_not_regions(string typed) => Assert.Null(Regions.Find(typed));

    [Fact]
    public void Slugs_round_trip_through_the_address()
    {
        foreach (var r in Regions.All) Assert.Equal(r.Code, Regions.Find(r.Slug)?.Code);
        Assert.Equal("new-york-state", Regions.FromCode("US-NY")!.Slug);
        Assert.Equal("united-kingdom", Regions.FromCode("UK")!.Slug);
    }

    [Fact]
    public void NL_is_Newfoundland_in_the_Americas_and_the_Netherlands_in_Europe()
    {
        var stJohns = Location("NL", 47.56, -52.71);
        var amsterdam = Location("NL", 52.37, 4.90);
        Assert.Equal("CA", Regions.CountryOf(stJohns));
        Assert.Equal("NL", Regions.CountryOf(amsterdam));
        Assert.True(Regions.FromCode("CA-NL")!.Contains(stJohns));
        Assert.False(Regions.FromCode("NL")!.Contains(stJohns));
        Assert.True(Regions.FromCode("NL")!.Contains(amsterdam));
    }

    [Fact]
    public void A_state_holds_its_own_locations_and_the_country_holds_them_all()
    {
        var austin = Location("TX", 30.27, -97.74);
        var lehi = Location("UT", 40.39, -111.85);
        Assert.True(Regions.FromCode("US-TX")!.Contains(austin));
        Assert.False(Regions.FromCode("US-TX")!.Contains(lehi));
        Assert.True(Regions.FromCode("US")!.Contains(austin) && Regions.FromCode("US")!.Contains(lehi));
        Assert.Equal(RegionKind.State, Regions.FromCode("US-TX")!.Kind);
    }

    private static CompanyLocation Location(string state, double lat, double lng) => new()
    {
        LocationId = $"{state}-{lat}", CompanyId = "X", Type = LocationType.Headquarters, Label = "HQ", City = "City", State = state,
        Point = new GeoPoint(lat, lng),
    };
}
