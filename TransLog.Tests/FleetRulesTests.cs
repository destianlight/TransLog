using TransLog.Web.Fleet;

namespace TransLog.Tests;

/// <summary>Part 3a: the small rules behind the fleet pages. No database needed.</summary>
public class FleetRulesTests
{
    [Theory]
    [InlineData("llt 587-nw", "LLT587NW")]
    [InlineData("  CA 123 456 ", "CA123456")]
    [InlineData("ABC.123.GP", "ABC123GP")]
    [InlineData("", "")]
    public void Plates_are_stored_upper_case_without_spaces(string typed, string expected) =>
        Assert.Equal(expected, Plates.Normalize(typed));

    [Theory]
    [InlineData("LLT587NW", true)]
    [InlineData("A", false)]
    [InlineData("", false)]
    [InlineData("ÄÖÜ123", false)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", false)]
    public void Plates_need_2_to_20_letters_or_digits(string plate, bool valid) =>
        Assert.Equal(valid, Plates.IsValid(plate));

    [Theory]
    [InlineData("+27 82 123 4567", "0821234567")]
    [InlineData("27821234567", "0821234567")]
    [InlineData("082-123-4567", "0821234567")]
    [InlineData("(082) 123 4567", "0821234567")]
    public void Cell_numbers_become_ten_digits_starting_with_zero(string typed, string expected)
    {
        var cell = CellNumbers.Normalize(typed);
        Assert.Equal(expected, cell);
        Assert.True(CellNumbers.IsValid(cell));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData("82123456789")]
    [InlineData("1821234567")]
    public void Wrong_cell_numbers_are_refused(string typed) =>
        Assert.False(CellNumbers.IsValid(CellNumbers.Normalize(typed)));

    [Theory]
    [InlineData("8001015009087", true)]
    [InlineData("8503155000081", true)]
    [InlineData("8001015009088", false)] // wrong check digit
    [InlineData("9002295124082", false)] // 29 February 1990 does not exist, 2090 neither
    [InlineData("800101500908", false)]  // 12 digits
    [InlineData("80010150090AB", false)]
    [InlineData("", false)]
    public void ID_numbers_need_a_real_date_and_a_correct_check_digit(string id, bool valid) =>
        Assert.Equal(valid, SaId.IsValid(id));

    [Theory]
    [InlineData(null, ExpiryState.None, "")]
    [InlineData("2026-11-26", ExpiryState.Expired, "Expired")]
    [InlineData("2026-11-27", ExpiryState.DueSoon, "Expires today")]
    [InlineData("2026-11-28", ExpiryState.DueSoon, "Due tomorrow")]
    [InlineData("2026-12-27", ExpiryState.DueSoon, "Due in 30 days")]
    [InlineData("2026-12-28", ExpiryState.Ok, "")]
    public void Expiry_dates_are_flagged_30_days_ahead(string? date, ExpiryState state, string text)
    {
        var today = new DateOnly(2026, 11, 27);
        var info = Expiry.Describe(date is null ? null : DateOnly.Parse(date), today);
        Assert.Equal(state, info.State);
        Assert.Equal(text, info.Text);
    }
}
