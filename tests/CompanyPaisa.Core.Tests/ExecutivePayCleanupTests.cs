using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

/// <summary>The pay-row cleanup on cases taken from the published data (names as proxy tables gave them).</summary>
public class ExecutivePayCleanupTests
{
    [Theory]
    [InlineData("Timothy Pitoniak (", "Timothy Pitoniak")]
    [InlineData("Frank Ruffo ( )", "Frank Ruffo")]
    [InlineData("Amy M. Rocklin (1)", "Amy M. Rocklin")]
    [InlineData("David E. Benson VP", "David E. Benson")]
    [InlineData("John O. Larsen: Board", "John O. Larsen")]
    [InlineData("Jonathan Fitzpatrick Non", "Jonathan Fitzpatrick")]
    [InlineData("Thomas M. Rutledg e", "Thomas M. Rutledge")]
    [InlineData("Jeffrey Hoover CLO and", "Jeffrey Hoover")]
    [InlineData("Michael McElhaugh Current", "Michael McElhaugh")]
    [InlineData("W. Bradley Southern Chairperson and", "W. Bradley Southern")]
    [InlineData("A.J. Restel SEVP-Chief", "A.J. Restel")]
    [InlineData("Evan Hafer Prior", "Evan Hafer")]
    [InlineData("Suresh Kumar Global", "Suresh Kumar")]
    [InlineData("First Financial Corporation Rodger A. McHargue", "Rodger A. McHargue")]
    [InlineData("Brendan E. Krueger (Sr.", "Brendan E. Krueger")]
    public void Cleans_table_debris_from_names(string raw, string clean) => Assert.Equal(clean, ExecutivePayCleanup.CleanName(raw));

    [Theory]
    [InlineData("Feng Ming (Fermi) Wang")]
    [InlineData("Yin-Chieh (\"Jeff\") Cheng")]
    [InlineData("Luis von Ahn")]
    [InlineData("Eleanor de Groot")]
    [InlineData("William M. Hickey III")]
    [InlineData("K. Lyons-Tarr")]
    [InlineData("Najm-ul- Hassan")]
    public void Leaves_real_names_alone(string name) => Assert.Equal(name, ExecutivePayCleanup.CleanName(name));

    [Theory]
    [InlineData("Other NEOs Required to Be Discussed")]
    [InlineData("Current NEOs")]
    [InlineData("Counsel and")]
    [InlineData("Strategy and Transformation")]
    [InlineData("Marketing &")]
    [InlineData("Incentive Award")]
    [InlineData("Alaska Airlines")]
    public void Rejects_labels_that_are_not_people(string name) => Assert.False(ExecutivePayCleanup.IsPerson(ExecutivePayCleanup.CleanName(name)));

    [Theory]
    [InlineData("Charles Collier", "Charlie Collier")]
    [InlineData("Mathew Kalish", "Matthew Kalish")]
    [InlineData("Basil Shikin", "Vasily Shikin")]
    [InlineData("C. Douglas McMillon", "Doug McMillon")]
    [InlineData("Caryn Seidman", "Caryn Seidman-Becker")]
    [InlineData("J. Duato", "Joaquin Duato")]
    [InlineData("Ben Minicucci", "Benito Minicucci")]
    public void Recognises_one_person_written_two_ways(string a, string b) => Assert.True(ExecutivePayCleanup.SamePerson(a, b));

    [Theory]
    [InlineData("K. Rupert Murdoch", "Lachlan K. Murdoch")]
    [InlineData("Cameron Winklevoss", "Tyler Winklevoss")]
    [InlineData("Daniel Roberts", "William Roberts")]
    [InlineData("W. Robert Berkley, Jr.", "William R. Berkley")]
    public void Keeps_different_people_apart(string a, string b) => Assert.False(ExecutivePayCleanup.SamePerson(a, b) && a.Length == b.Length);

    private static ExecutiveCompensation Pay(string company, string person, string name, int year, decimal total, string title = "Chief Executive Officer") =>
        new() { CompanyId = company, PersonId = person, ExecutiveName = name, Title = title, Year = year, Salary = 1_000_000, Total = total };

    [Fact]
    public void Merges_one_executive_under_two_spellings_and_keeps_the_insider_id()
    {
        // Roku 2022: the same package under "Charles" (company-scoped id) and "Charlie" (SEC insider id); 2023 only as Charles.
        var pay = new[]
        {
            Pay("ROKU", "charles-collier-roku", "Charles Collier", 2022, 53_304_896, "President, Roku Media"),
            Pay("ROKU", "charlie-collier-1234", "Charlie Collier", 2022, 53_304_896, "President, Roku Media"),
            Pay("ROKU", "charles-collier-roku", "Charles Collier", 2023, 12_000_000, "President, Roku Media"),
            Pay("ROKU", "anthony-wood-99", "Anthony Wood", 2022, 40_000_000)
        };
        var people = new[]
        {
            new Person { PersonId = "charles-collier-roku", Name = "Charles Collier" },
            new Person { PersonId = "charlie-collier-1234", Name = "Charlie Collier", SecCik = "1234" },
            new Person { PersonId = "anthony-wood-99", Name = "Anthony Wood", SecCik = "99" }
        };

        var r = ExecutivePayCleanup.Apply(pay, people);

        Assert.Equal(3, r.Pay.Count);
        Assert.Equal(1, r.DuplicatesRemoved);
        Assert.All(r.Pay.Where(p => p.ExecutiveName.Contains("Collier")), p => Assert.Equal("charlie-collier-1234", p.PersonId));
        Assert.Equal(["charlie-collier-1234", "anthony-wood-99"], r.People.Select(p => p.PersonId));
        Assert.Equal("charlie-collier-1234", r.Merged[("ROKU", "charles-collier-roku")]);
    }

    [Fact]
    public void Keeps_similar_names_with_different_pay_apart()
    {
        // The Murdochs at Fox: an initial in common, different people, different pay.
        var pay = new[] { Pay("FOXA", "rupert", "K. Rupert Murdoch", 2024, 21_169_943), Pay("FOXA", "lachlan", "Lachlan K. Murdoch", 2024, 23_806_025) };

        var r = ExecutivePayCleanup.Apply(pay, [new Person { PersonId = "rupert", Name = "K. Rupert Murdoch" }, new Person { PersonId = "lachlan", Name = "Lachlan K. Murdoch" }]);

        Assert.Equal(2, r.Pay.Count);
        Assert.Empty(r.Merged);
    }

    [Fact]
    public void Never_merges_two_sec_insiders()
    {
        var pay = new[] { Pay("X", "a-1", "John Smith", 2024, 1_000_000), Pay("X", "b-2", "John Smith", 2024, 1_000_000) };

        var r = ExecutivePayCleanup.Apply(pay, [new Person { PersonId = "a-1", Name = "John Smith", SecCik = "1" }, new Person { PersonId = "b-2", Name = "John Smith", SecCik = "2" }]);

        Assert.Equal(2, r.Pay.Count);
    }

    [Fact]
    public void Drops_labels_and_rejoins_names_split_into_the_title()
    {
        var pay = new[]
        {
            Pay("HTZ", "other-htz", "Other NEOs Required to Be Discussed", 2022, 182_136_137),
            Pay("YOU", "caryn-you", "Caryn Seidman-", 2021, 40_341_012, "Becker Chairman and Chief Executive Officer"),
            Pay("AIV", "wes-aiv", "Wes Powell -President", 2023, 3_000_000, "and Chief Executive Officer")
        };

        var r = ExecutivePayCleanup.Apply(pay, []);

        Assert.Equal(1, r.RowsDropped);
        Assert.Contains(r.Pay, p => p is { ExecutiveName: "Caryn Seidman-Becker", Title: "Chairman and Chief Executive Officer" });
        Assert.Contains(r.Pay, p => p is { ExecutiveName: "Wes Powell", Title: "President and Chief Executive Officer" });
    }

    private static ExecutiveCompensation Package(string person, string name, string title, string filing) => new()
    {
        CompanyId = "AAON", PersonId = person, ExecutiveName = name, Title = title, Year = 2023,
        Salary = 745_192, StockAwards = 2_802_616, Other = 1_458_977, Total = 5_006_785, SourceFiling = filing
    };

    [Fact]
    public void Drops_a_row_whose_title_names_the_person_it_belongs_to()
    {
        // AAON's 2026 proxy: Gary Fields' 2023 row read under Matthew Tobolski, Fields' name in Tobolski's title cell.
        var pay = new[]
        {
            Package("gary-fields", "Gary D. Fields", "Chief Executive Officer", "aaon-20250402.htm"),
            Package("matthew-tobolski", "Matthew J. Tobolski", "Chief Executive Officer Gary D. Fields Special Advisor to the Board", "aaon-20260401.htm")
        };

        var r = ExecutivePayCleanup.Apply(pay, []);

        var row = Assert.Single(r.Pay);
        Assert.Equal("Gary D. Fields", row.ExecutiveName);
        Assert.Equal(1, r.Misattributed);
    }

    [Fact]
    public void Merges_one_surname_with_identical_pay_from_two_filings_but_not_from_one()
    {
        // Walmart: "Kath McLay" (2024 proxy) and "Kathryn McLay" (2023 proxy) — one person.
        var twoFilings = ExecutivePayCleanup.Apply(
        [
            Package("kath-mclay", "Kath McLay", "President and CEO, Walmart International", "wmt-20240424.htm"),
            Package("kathryn-mclay", "Kathryn McLay", "President and CEO, Sam's Club", "wmt-20230420.htm")
        ], []);
        // IREN: the Roberts brothers, co-CEOs on one plan in one filing — two people.
        var oneFiling = ExecutivePayCleanup.Apply(
        [
            Package("daniel-roberts", "Daniel Roberts", "Co-Chief Executive Officer", "iren-20250901.htm"),
            Package("william-roberts", "William Roberts", "Co-Chief Executive Officer", "iren-20250901.htm")
        ], []);

        Assert.Single(twoFilings.Pay);
        Assert.Equal(2, oneFiling.Pay.Count);
    }

    [Fact]
    public void Cleans_titles_after_using_them()
    {
        var r = ExecutivePayCleanup.Apply([Package("richard-you", "Richard N. Patterson, Jr.", "Patterson, Jr. Chief Information Security Officer 2,147,000", "you.htm")], []);

        Assert.Equal("Chief Information Security Officer", Assert.Single(r.Pay).Title);
    }
}
