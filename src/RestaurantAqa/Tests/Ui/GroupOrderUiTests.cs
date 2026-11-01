using System.Text.RegularExpressions;
using RestaurantAqa.Api;
using RestaurantAqa.Data;
using RestaurantAqa.Ui;
using RestaurantAqa.Ui.Pages;
using static Microsoft.Playwright.Assertions;

namespace RestaurantAqa.Tests.Ui;

/// <summary>The Owner's group pre-order screen, driven through the browser. Guests and carts are prepared through the API.</summary>
[TestFixture, Category("UI")]
public class GroupOrderUiTests : UiTestBase
{
    private static RestaurantApi Api => TestEnvironment.Api;

    private async Task<GroupOrderPage> OpenAsOwnerAsync(GroupScenario scenario)
    {
        await UiAuth.SignInAsync(Context, scenario.Owner);
        var screen = new GroupOrderPage(Page);
        await screen.OpenAsync(scenario.ReservationId);
        await Expect(screen.Heading).ToBeVisibleAsync();
        await Expect(screen.Loading).ToHaveCountAsync(0);
        return screen;
    }

    // ---------------------------------------------------------------- level 1: viewing

    [Test, Category("L1")]
    public async Task Owner_sees_every_participant_with_dishes_status_and_totals()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        await scenario.AddGuestAsync("Olena", [(catalog.DishB, 1)]);
        await scenario.AddGuestAsync("Mariana");

        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);

        await Expect(screen.Card("My Pre-Order")).ToBeVisibleAsync();
        await Expect(screen.Cards).ToHaveCountAsync(4);

        await Expect(screen.Card("Guest: Andrii")).ToContainTextAsync(catalog.DishA.Name);
        await Expect(screen.Card("Guest: Andrii")).ToContainTextAsync("× 2");
        await Expect(screen.CartStatus("Guest: Andrii", "Submitted")).ToBeVisibleAsync();
        await Expect(screen.Subtotal("Guest: Andrii")).ToContainTextAsync(GroupOrderPage.Money(2 * catalog.DishA.Price));

        await Expect(screen.Card("Guest: Olena")).ToContainTextAsync(catalog.DishB.Name);
        await Expect(screen.CartStatus("Guest: Olena", "Still choosing")).ToBeVisibleAsync();
        await Expect(screen.Subtotal("Guest: Olena")).ToContainTextAsync(GroupOrderPage.Money(catalog.DishB.Price));

        await Expect(screen.Card("Guest: Mariana")).ToContainTextAsync("No dishes yet");
        await Expect(screen.Subtotal("Guest: Mariana")).ToContainTextAsync(GroupOrderPage.Money(0));
    }

    [Test, Category("L1")]
    public async Task Grand_total_counts_only_confirmed_carts_and_a_notice_warns_about_the_others()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        await scenario.AddGuestAsync("Olena", [(catalog.DishB, 1)]);

        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);

        await Expect(screen.GrandTotal).ToContainTextAsync(GroupOrderPage.Money(2 * catalog.DishA.Price));
        await Expect(screen.GrandTotal).Not.ToContainTextAsync(GroupOrderPage.Money(2 * catalog.DishA.Price + catalog.DishB.Price));
        await Expect(screen.ConsolidatedOrder).ToContainTextAsync($"{catalog.DishA.Name} × 2");
        await Expect(screen.ConsolidatedOrder).Not.ToContainTextAsync(catalog.DishB.Name);
        await Expect(screen.UnconfirmedNotice).ToBeVisibleAsync();
    }

    [Test, Category("L1")]
    public async Task A_group_without_guests_shows_only_the_Owner_and_a_disabled_submit()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);

        await Expect(screen.Cards).ToHaveCountAsync(1);
        await Expect(screen.Card("My Pre-Order")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Participants haven’t added any dishes yet")).ToBeVisibleAsync();
        await Expect(screen.SubmitButton).ToBeDisabledAsync();
    }

    [Test, Category("L1"), Category("Slow")]
    public async Task The_screen_picks_up_a_cart_confirmed_by_a_guest_without_a_reload()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        Guest olena = await scenario.AddGuestAsync("Olena", [(catalog.DishB, 2)]);
        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);
        await Expect(screen.CartStatus("Guest: Olena", "Still choosing")).ToBeVisibleAsync();

        (await Api.SubmitCartAsync(olena.Token, olena.SessionId)).EnsureSuccess();

        // The screen refreshes itself every 15 seconds while the group is open.
        await Expect(screen.CartStatus("Guest: Olena", "Submitted")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(screen.GrandTotal).ToContainTextAsync(GroupOrderPage.Money(2 * catalog.DishB.Price));
    }

    [Test, Category("L1")]
    public async Task Another_customer_can_not_see_the_group()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)], submit: true);
        TestUser stranger = await UserFactory.CreateCustomerAsync(Api, lastName: "Stranger");
        await UiAuth.SignInAsync(Context, stranger);

        var screen = new GroupOrderPage(Page);
        await screen.OpenAsync(scenario.ReservationId);

        await Expect(screen.LoadError).ToBeVisibleAsync();
        await Expect(screen.Cards).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Andrii")).ToHaveCountAsync(0);
    }

    [Test, Category("L1")]
    public async Task An_unauthenticated_visitor_does_not_get_the_screen()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        await Page.GotoAsync($"/reservations/{scenario.ReservationId}/group-order");

        await Expect(Page).Not.ToHaveURLAsync(new Regex("group-order"));
        await Expect(new GroupOrderPage(Page).Heading).ToHaveCountAsync(0);
    }

    // ---------------------------------------------------------------- level 2: confirmation

    [Test, Category("L2")]
    public async Task Owner_confirms_the_consolidated_pre_order_and_the_screen_becomes_read_only()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        await scenario.AddGuestAsync("Kateryna", [(catalog.DishA, 1), (catalog.DishB, 2)], submit: true);
        await scenario.AddGuestAsync("Olena", [(catalog.DishC, 5)]);
        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);
        await Expect(screen.ConsolidatedOrder).ToContainTextAsync($"{catalog.DishA.Name} × 3");

        await screen.SubmitAsync();

        await Expect(screen.StatusBadge("Sent to Restaurant")).ToBeVisibleAsync();
        await Expect(screen.SubmitButton).ToHaveCountAsync(0);
        await Expect(screen.RemoveButton("Andrii")).ToHaveCountAsync(0);

        OrderDto order = (await Api.GetOrderAsync(scenario.Owner.Token, scenario.ReservationId)).EnsureSuccess().Json<OrderDto>();
        Assert.That(order.Status, Is.EqualTo(OrderStatus.SubmittedByCustomer));
        Assert.That(order.OrderItems.ToDictionary(i => i.Dish.Id, i => i.Quantity),
            Is.EquivalentTo(new Dictionary<string, int> { [catalog.DishA.Id] = 4, [catalog.DishB.Id] = 2 }));
        Assert.That((await Api.GroupStatusAsync(scenario.GroupId)).Json<GroupStatusDto>().Status, Is.EqualTo("Submitted"));
    }

    [Test, Category("L2")]
    public async Task Cancelling_the_confirmation_dialog_sends_nothing()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)], submit: true);
        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);

        await screen.SubmitButton.ClickAsync();
        await Expect(screen.Dialog).ToBeVisibleAsync();
        await screen.Dialog.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        await Expect(screen.Dialog).ToHaveCountAsync(0);
        await Expect(screen.StatusBadge("Collecting pre-orders")).ToBeVisibleAsync();
        Assert.That((await Api.GroupStatusAsync(scenario.GroupId)).Json<GroupStatusDto>().Status, Is.EqualTo("Open"));
    }

    [Test, Category("L2")]
    public async Task Submit_is_disabled_while_nobody_has_confirmed_a_cart()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Olena", [(scenario.Catalog.DishB, 1)]);

        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);

        await Expect(screen.SubmitButton).ToBeDisabledAsync();
    }

    // ---------------------------------------------------------------- level 3: removing a participant

    [Test, Category("L3")]
    public async Task Owner_removes_a_guest_and_the_list_and_totals_are_updated()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        Guest olena = await scenario.AddGuestAsync("Olena", [(catalog.DishC, 1)], submit: true);
        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);
        await Expect(screen.GrandTotal).ToContainTextAsync(GroupOrderPage.Money(2 * catalog.DishA.Price + catalog.DishC.Price));

        await screen.RemoveGuestAsync("Olena");

        await Expect(screen.Card("Guest: Olena")).ToHaveCountAsync(0);
        await Expect(screen.Card("Guest: Andrii")).ToBeVisibleAsync();
        await Expect(screen.GrandTotal).ToContainTextAsync(GroupOrderPage.Money(2 * catalog.DishA.Price));
        await Expect(screen.ConsolidatedOrder).Not.ToContainTextAsync(catalog.DishC.Name);

        GroupSnapshotDto snapshot = await scenario.SnapshotAsync();
        Assert.That(snapshot.Participants.Select(p => p.Id), Does.Not.Contain(olena.SessionId));
    }

    [Test, Category("L3")]
    public async Task Cancelling_the_removal_dialog_keeps_the_guest()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest andrii = await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)]);
        GroupOrderPage screen = await OpenAsOwnerAsync(scenario);

        await screen.RemoveButton("Andrii").ClickAsync();
        await Expect(screen.Dialog).ToContainTextAsync("Are you sure you want to remove");
        await screen.Dialog.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        await Expect(screen.Card("Guest: Andrii")).ToBeVisibleAsync();
        Assert.That((await scenario.SnapshotAsync()).Participants.Select(p => p.Id), Does.Contain(andrii.SessionId));
    }
}
