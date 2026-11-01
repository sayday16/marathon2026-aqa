using System.Globalization;
using Microsoft.Playwright;

namespace RestaurantAqa.Ui.Pages;

/// <summary>The Owner's group pre-order screen: /reservations/{reservationId}/group-order.</summary>
public sealed class GroupOrderPage(IPage page)
{
    public static string Money(decimal amount) => amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));

    public async Task OpenAsync(string reservationId) => await page.GotoAsync($"/reservations/{reservationId}/group-order");

    public ILocator Heading => page.GetByRole(AriaRole.Heading, new() { Name = "Group Pre-Order", Exact = true });

    public ILocator Loading => page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading group pre-order" });

    public ILocator StatusBadge(string text) => page.GetByText(text, new() { Exact = true });

    /// <summary>A participant card by its title: "Guest: Andrii", "My Pre-Order" for the Owner.</summary>
    public ILocator Card(string title) =>
        page.GetByRole(AriaRole.Article).Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true }) });

    public ILocator Cards => page.GetByRole(AriaRole.Article);

    public ILocator Subtotal(string title) => Card(title).Locator("p").Filter(new() { HasText = "Subtotal" });

    public ILocator CartStatus(string title, string status) => Card(title).GetByText(status, new() { Exact = true });

    public ILocator GrandTotal => page.Locator("p").Filter(new() { HasText = "Grand Total" });

    public ILocator ConsolidatedOrder => page.GetByRole(AriaRole.Region, new() { Name = "Consolidated pre-order" });

    public ILocator UnconfirmedNotice => page.GetByRole(AriaRole.Status).Filter(new() { HasText = "submitted their cart yet" });

    public ILocator SubmitButton => page.GetByRole(AriaRole.Button, new() { Name = "Submit Pre-Order", Exact = true });

    public ILocator Dialog => page.GetByRole(AriaRole.Dialog);

    public ILocator RemoveButton(string guestName) => page.GetByRole(AriaRole.Button, new() { Name = $"Remove {guestName}", Exact = true });

    public ILocator RefreshButton => page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true });

    public ILocator LoadError => page.GetByText("Couldn’t load the group pre-order");

    public async Task SubmitAsync()
    {
        await SubmitButton.ClickAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
    }

    public async Task RemoveGuestAsync(string guestName)
    {
        await RemoveButton(guestName).ClickAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Remove Guest", Exact = true }).ClickAsync();
    }
}
