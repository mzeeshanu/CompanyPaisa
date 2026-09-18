using CompanyPaisa.Importer.Compensation;

namespace CompanyPaisa.Core.Tests;

/// <summary>The rules-based reader of officer appointments in 8-Ks, on wording taken from real filings.</summary>
public class NewHireParserTests
{
    private static readonly DateOnly Filed = new(2026, 4, 16);

    private static string Filing(string item502) =>
        $"<html><body><p>Item 5.02 Departure of Directors or Certain Officers; Appointment of Certain Officers.</p><p>{item502}</p>" +
        "<p>Item 9.01 Financial Statements and Exhibits.</p><p>SIGNATURES</p></body></html>";

    private static ParsedNewHire Single(string item502) => Assert.Single(NewHireParser.Parse(Filing(item502), Filed));

    [Fact]
    public void Reads_the_appointment_and_every_stated_amount()
    {
        var h = Single("""
            On April 13, 2026, the Company agreed to appoint Terrence O. Moorehead as the Company's new President and Chief Executive Officer,
            and as a member of the Board, effective as of August 5, 2026. Mr. Moorehead, age 63, previously served as President of Nature's Sunshine.
            Pursuant to the Employment Agreement, Mr. Moorehead's annual base salary will be $850,000. Mr. Moorehead will also have an aggregate
            annual cash bonus opportunity of up to 100% of his salary, provided that, so long as Mr. Moorehead has not resigned for "Good Reason"
            or been terminated "For Cause," the Company has guaranteed that his cash bonus for the fiscal year ending June 30, 2027 shall be $425,000.
            The RSUs consist of (i) $2,000,000 of RSUs that vest in three equal installments and (ii) $800,000 of RSUs, prorated.
            The PSUs consist of (i) $3,500,000 of PSUs that vest based on revenue and Adjusted EBITDA margin targets and (ii) $1,200,000 of PSUs
            that vest based on the achievement of certain revenue targets.
            """);

        Assert.Equal("Terrence O. Moorehead", h.Name);
        Assert.Equal("President and Chief Executive Officer", h.Title);
        Assert.Equal(new DateOnly(2026, 8, 5), h.StartsOn);
        Assert.Equal(8_775_000, h.Total);
        Assert.Equal(PackagePartKind.Salary, h.Parts[0].Kind);
        Assert.Equal(2, h.Parts.Count(p => p.Kind == PackagePartKind.PerformanceStock));
    }

    [Fact]
    public void Other_peoples_terms_severance_monthly_fees_and_caps_are_left_out()
    {
        var h = Single("""
            Steven Fife notified the Board of his decision to retire. The Company appointed Jane Q. Doe as Chief Financial Officer.
            Ms. Doe will receive an annual base salary of $500,000 and a one-time sign-on bonus of $250,000.
            If Ms. Doe is terminated without cause, she will receive severance of $1,000,000.
            She may earn up to $300,000 in additional incentives. She will be reimbursed for legal fees of $15,000.
            Mr. Beindorff will be paid $45,833 per month for acting as Interim CEO.
            Pursuant to the Transition Agreement, Mr. Fife will receive a payment of $600,000.
            """);

        Assert.Equal("Jane Q. Doe", h.Name);
        Assert.Equal([500_000m, 250_000m], h.Parts.Select(p => p.Amount));
        Assert.Equal(PackagePartKind.SignOnCash, h.Parts[1].Kind);
    }

    [Fact]
    public void A_total_and_its_pieces_count_once()
    {
        var h = Single("""
            The Board appointed Daniel T. Scavilla as President and Chief Executive Officer. Mr. Scavilla will be paid an annual base salary of $1,030,000.
            Mr. Scavilla will also receive an initial equity grant with an aggregate grant date value of approximately $6,400,000, reflecting a
            pro-rata annual grant of $3,875,000 plus an inducement grant of $2,525,000.
            """);

        Assert.Equal(1_030_000 + 3_875_000 + 2_525_000, h.Total);
    }

    [Fact]
    public void Future_years_targets_and_later_years_grants_are_not_part_of_joining()
    {
        var h = Single("""
            The Company named Shane Grant as Chief Operating Officer. Mr. Grant will receive a base salary of $1,100,000.
            Beginning in fiscal year 2027, Mr. Grant's target annual equity award will have a grant date value of $7,750,000.
            Mr. Grant will receive an annual grant of stock options (valued at $450,000) in September 2026.
            In 2027, he will also receive an annual grant of stock options (valued at $1,600,000).
            """);

        Assert.Equal([1_100_000m, 450_000m], h.Parts.Select(p => p.Amount));
        Assert.Equal(PackagePartKind.Options, h.Parts[1].Kind);
    }

    [Fact]
    public void Typos_and_monthly_salaries_are_dropped()
    {
        var typo = Single("""
            The Company appointed Adam M. Jaworski as Senior Vice President. Mr. Jaworski will receive an annual base salary of $365,000
            and a $50,000 signing bonus and a grant of $330,000,000 of restricted shares.
            """);
        var monthly = NewHireParser.Parse(Filing("""
            The Company appointed David White as Chief Executive Officer. Mr. White will be paid a monthly base salary of $21,700.
            """), Filed);

        Assert.Equal(415_000, typo.Total);
        Assert.Empty(monthly);
    }

    [Theory]
    [InlineData("The Board appointed John Smith as Interim Chief Executive Officer. Mr. Smith will receive a base salary of $400,000.")]
    [InlineData("The Board elected John Smith as a director. Mr. Smith will receive an annual retainer of $80,000.")]
    [InlineData("The Company appointed John Smith as Vice President, Marketing. Mr. Smith will receive a base salary of $300,000.")]
    public void Interim_board_and_junior_appointments_are_ignored(string text) =>
        Assert.Empty(NewHireParser.Parse(Filing(text), Filed));

    [Fact]
    public void Titles_are_trimmed_of_what_follows_them()
    {
        var h = Single("""
            The Company appointed Glendon E. French as President and Chief Executive Officer as of the Effective Date.
            Mr. French will receive a base salary of $625,000.
            """);
        Assert.Equal("President and Chief Executive Officer", h.Title);
    }
}
