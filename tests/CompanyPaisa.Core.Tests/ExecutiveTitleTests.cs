using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

/// <summary>Titles as they came out of real proxy statements' pay tables, and what the site should show.</summary>
public class ExecutiveTitleTests
{
    [Theory]
    // Footnotes that ran into the title cell.
    [InlineData("Chief Financial Officer Represents the aggregate grant date fair value of option awards granted in the applicable fiscal year.", "Chief Financial Officer")]
    [InlineData("Former Chief Product Officer Aggregate grant date fair values are computed in accordance with ASC Topic 718.", "Former Chief Product Officer")]
    [InlineData("Senior Vice President Strategy & Corporate Development Amounts in this column represent the cash sign-on bonus", "Senior Vice President Strategy & Corporate Development")]
    [InlineData("General Counsel and Secretary The value in this column reflects the aggregate grant date fair value", "General Counsel and Secretary")]
    [InlineData("Vice President, Executive Vice President of AAON Coil Products See discussion of assumptions made in valuing these awards", "Vice President, Executive Vice President of AAON Coil Products")]
    [InlineData("EVP, Chief Accounting Officer & Corporate Secretary Base salary for 2022 reflects 25 biweekly payments", "EVP, Chief Accounting Officer & Corporate Secretary")]
    [InlineData("Former Chief Revenue Officer 1 The salary amounts reported represent the U.S. dollar values", "Former Chief Revenue Officer")]
    // The next person's name (with an age or a middle initial) and title.
    [InlineData("Former President and CEO Mark Hernandez , 57 President and CEO", "Former President and CEO")]
    [InlineData("Senior Vice President, Timberlands Denise M. Merle Senior Vice President and Chief Administration Officer", "Senior Vice President, Timberlands")]
    [InlineData("Chief Expedition Officer Mr. Bressler President, Natural Habitat, Inc.", "Chief Expedition Officer")]
    [InlineData("Officer Kevin M. Speirits Former Interim CFO", "Former Interim CFO")]
    // The person's own name, or a year, in front.
    [InlineData("James L. Dolan Executive Chairman and Chief Executive Officer", "Executive Chairman and Chief Executive Officer")]
    [InlineData("2021 W. Todd Gray Executive Vice President and Treasurer", "Executive Vice President and Treasurer")]
    // Amounts, dashes, dot leaders and section headings from the table.
    [InlineData("Chief Commercial Officer 108,150 — 2,000,011 —", "Chief Commercial Officer")]
    [InlineData("Senior Vice President and Chief Development Officer (111,306) 523,988 603,574", "Senior Vice President and Chief Development Officer")]
    [InlineData("President & Chief Executive Officer — — — —", "President & Chief Executive Officer")]
    [InlineData("Former President, Product, Technology, and Operations ........................", "Former President, Product, Technology, and Operations")]
    [InlineData("Former President, Global Spine Former Officers:", "Former President, Global Spine")]
    [InlineData("Chief Operating Officer    975,586", "Chief Operating Officer")]
    [InlineData("Chief Executive Officer $- - $962 - Heather Dixon Chief Financial Officer", "Chief Executive Officer")]
    [InlineData(". Chief Operating Officer", "Chief Operating Officer")]
    [InlineData("-Executive Vice President and Chief Marketing Officer", "Executive Vice President and Chief Marketing Officer")]
    [InlineData("age President of Wealth Management", "President of Wealth Management")]
    [InlineData("former Chief Innovation Officer", "Former Chief Innovation Officer")]
    [InlineData("Former Co-CEO)", "Former Co-CEO")]
    [InlineData("Chief Financial Officer ...", "Chief Financial Officer")]
    [InlineData("Ph.D. Chief Scientific Officer", "Chief Scientific Officer")]
    [InlineData("M.D. Chief Medical Officer", "Chief Medical Officer")]
    public void Footnotes_other_people_and_amounts_are_cut_from_the_title(string raw, string shown) =>
        Assert.Equal(shown, ExecutiveTitles.Clean(raw));

    [Theory]
    [InlineData("Executive Vice President, General Counsel, Chief Compliance Officer and Corporate Secretary")]
    [InlineData("President, Foodservice Division, T. Marzetti Company")]
    [InlineData("Dr.P.H. Group Vice President")]
    [InlineData("President – PG&A and Aftermarket")]
    [InlineData("Chairman, President and Chief Executive Officer (Principal Executive Officer)")]
    [InlineData("Executive Vice President and Chief Financial Officer (principal financial officer)")]
    [InlineData("Senior Vice President, Americas")]
    public void Real_titles_are_left_alone(string title) => Assert.Equal(title, ExecutiveTitles.Clean(title));

    [Theory]
    [InlineData("These values represent the aggregate grant date fair value of PSU, ESG PSU, and RSU awards granted")]
    [InlineData("The amounts reflect the aggregate grant date fair value of RSU awards")]
    [InlineData("")]
    [InlineData(null)]
    public void A_title_that_was_only_footnote_text_becomes_the_fallback(string? raw) =>
        Assert.Equal(ExecutiveTitles.Fallback, ExecutiveTitles.Clean(raw));

    [Fact]
    public void A_very_long_title_is_cut_at_a_word()
    {
        var shown = ExecutiveTitles.Clean(string.Join(", ", Enumerable.Repeat("Executive Vice President of Global Operations", 5)));
        Assert.True(shown.Length <= ExecutiveTitles.MaxLength + 1);
        Assert.EndsWith("…", shown);
        Assert.DoesNotContain(",…", shown);
    }
}
