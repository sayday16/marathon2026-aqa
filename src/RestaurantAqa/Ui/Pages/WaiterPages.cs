using System.Globalization;
using Microsoft.Playwright;

namespace RestaurantAqa.Ui.Pages;

/// <summary>The waiter's list of reservations: /waiter-reservations.</summary>
public sealed class WaiterReservationsPage(IPage page)
{
    public async Task OpenAsync() => await page.GotoAsync("/waiter-reservations");

    /// <summary>
    /// The card of an active reservation by its date. The browser runs in UTC, so the date is the UTC date of the start.
    /// The tests book unique days, and cancelled leftovers of earlier runs are excluded.
    /// </summary>
    public ILocator ReservationOn(DateTime startUtc) =>
        page.Locator("app-card-reservation").Filter(new()
        {
            HasText = startUtc.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US")),
            HasNotText = "Cancelled",
        });

    public ILocator PreOrderButton(ILocator card) => card.GetByRole(AriaRole.Button, new() { Name = "Pre-order", Exact = true });

    /// <summary>
    /// Opens the list and returns the card of the reservation once it offers its pre-order. The list loads the pre-order of every
    /// reservation with a request of its own, and a request answered with a temporary error leaves "0 dishes" until the page
    /// is loaded again: a few reloads are part of the scenario, not a way to hide a failure.
    /// </summary>
    public async Task<ILocator> OpenWithPreOrderAsync(DateTime startUtc)
    {
        for (int attempt = 1; ; attempt++)
        {
            await OpenAsync();
            ILocator card = ReservationOn(startUtc);
            await Assertions.Expect(card).ToHaveCountAsync(1);

            try
            {
                await Assertions.Expect(PreOrderButton(card)).ToBeVisibleAsync(new() { Timeout = 10_000 });
                return card;
            }
            catch (PlaywrightException) when (attempt < 3)
            {
            }
        }
    }
}

/// <summary>The waiter's view of the pre-order of one reservation: /waiter-pre-order.</summary>
public sealed class WaiterPreOrderPage(IPage page)
{
    public ILocator Title => page.GetByRole(AriaRole.Heading, new() { Name = "Pre-order of" });

    public ILocator Dish(string name) => page.Locator("app-dish-card").Filter(new() { HasText = name });

    public ILocator Quantity(string dishName, int quantity) =>
        Dish(dishName).GetByText($"Quantity: {quantity}", new() { Exact = true });
}
