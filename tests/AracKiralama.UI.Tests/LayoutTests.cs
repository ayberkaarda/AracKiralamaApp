using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Tests.Support;
using AracKiralama.UI.Theming;
using static AracKiralama.UI.Tests.Support.StaThread;

namespace AracKiralama.UI.Tests;

/// <summary>
/// The screens keep every control inside its parent, without overlaps, on a small laptop, a common laptop and a
/// large screen at four text sizes, with long car and account names in their lists; long names never hide a
/// plate, a price or a status. Nothing is shown.
/// </summary>
public sealed class LayoutTests
{
    public static TheoryData<string> ScreenNames => [.. TestForms.Names];

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_FitsEveryScreenAtEveryTextSize(string name) => Run(() =>
        LayoutAssert.FitsInsideOnEveryScreen(() => TestForms.Create(name)));

    /// <summary>A car with a very long name keeps its open or closed pill at full width; the name gets what is left.</summary>
    [Fact]
    public void LongCarNames_KeepTheirStatus()
    {
        var car = ControlledCarService.Car(9, TestForms.LongBrand, TestForms.LongModel);
        car.IsListed = false;

        var row = new CarItem(car).Row();

        Assert.Equal(("Kiralamaya kapalı", RowTone.Warning), (row.Status, row.StatusTone));
        Assert.StartsWith(car.Plate + " · ", row.Secondary, StringComparison.Ordinal);
    }

    /// <summary>On a rental card, a long car name wraps beside the stage pill, which keeps its whole text.</summary>
    [Fact]
    public void LongRentalNames_KeepTheStage() => Run(() =>
    {
        var rental = TestForms.LongRentals().Rentals[0];
        var (card, _, _) = RentalCards.Build(rental, underway: true, showRenter: true, () => Task.CompletedTask, () => Task.CompletedTask);
        using (card)
        {
            card.SetBounds(0, 0, Theme.Px(card, RentalCards.MinWidth), 300);
            card.Height = card.GetPreferredSize(new Size(card.Width, 0)).Height;
            LayoutAssert.CreateHandles(card);
            card.PerformLayout();

            var stage = Find<StatusPill>(card, "lblStage8");
            var name = Find<Control>(card, "lblRentalCar8");
            Assert.Equal("Devam ediyor", stage.Text);
            Assert.True(TextRenderer.MeasureText(stage.Text, stage.Font).Width <= stage.Width);
            Assert.False(name.Bounds.IntersectsWith(stage.Bounds));
            Assert.True(name.Height > name.Font.Height, "A long name wraps instead of running under the pill.");
        }
    });

    /// <summary>The pill keeps its whole width at the right of a narrow line; the name gets what is left, never the pill's room.</summary>
    [Theory]
    [InlineData(400, 90)]
    [InlineData(120, 90)]
    [InlineData(60, 90)]
    public void StatusPill_NeverGivesWayToTheName(int lineWidth, int pillWidth)
    {
        var line = new Rectangle(10, 5, lineWidth, 20);

        var (primary, status) = ThemedListBox.SplitLine(line, pillWidth, 8);

        Assert.Equal(line.Right, status.Right);
        Assert.Equal(Math.Min(pillWidth, lineWidth), status.Width);
        Assert.False(primary.IntersectsWith(status));
        Assert.True(primary.Width >= 0);
        Assert.Equal(line, ThemedListBox.SplitLine(line, 0, 8).Primary);
    }

    /// <summary>The cards of a list take as many columns as fit and the rows below the visible part scroll inside the list.</summary>
    [Fact]
    public void CardGrid_PutsCardsInColumnsAndScrollsTheRest() => Run(() =>
    {
        using var grid = new CardGrid();
        var cars = TestForms.LongCars().Cars;
        grid.Replace(cars.Select(car => (Control)CarCards.Build(car, () => Task.CompletedTask).Card));
        grid.Size = new Size(Theme.Px(grid, (2 * Sizes.CardMinWidth) + Spacing.L + 40), 200);
        LayoutAssert.CreateHandles(grid);
        grid.PerformLayout();

        var cards = grid.Controls.Cast<Control>().ToList();
        Assert.Equal(cards[0].Top, cards[1].Top);
        Assert.True(cards[2].Top > cards[0].Bottom);
        Assert.False(cards[0].Bounds.IntersectsWith(cards[1].Bounds));
        Assert.True(cards[1].Right <= grid.ClientSize.Width, "No card runs under the scroll bar.");
        Assert.Equal(cards.Max(card => card.Bottom), grid.AutoScrollMinSize.Height);
        Assert.True(grid.DisplayRectangle.Height >= grid.AutoScrollMinSize.Height);
    });
}
