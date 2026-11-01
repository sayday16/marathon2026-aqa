using RestaurantAqa.Api;
using RestaurantAqa.Data;
using RestaurantAqa.Ui;
using RestaurantAqa.Ui.Pages;
using static Microsoft.Playwright.Assertions;

namespace RestaurantAqa.Tests.Ui;

/// <summary>Signing in through the real form (everywhere else the session is prepared through the API).</summary>
[TestFixture, Category("UI"), Category("Auth")]
public class LoginUiTests : UiTestBase
{
    private static RestaurantApi Api => TestEnvironment.Api;

    [Test]
    public async Task A_registered_customer_signs_in_through_the_form()
    {
        TestUser user = await UserFactory.CreateCustomerAsync(Api);
        var login = new LoginPage(Page);
        await login.OpenAsync();

        await login.SignInAsync(user.Email, user.Password);

        await Page.WaitForFunctionAsync("() => localStorage.getItem('auth') !== null");
        Assert.That(await login.HasSessionAsync(), Is.True);
    }

    [Test]
    public async Task A_wrong_password_is_rejected_with_a_message_and_no_session()
    {
        TestUser user = await UserFactory.CreateCustomerAsync(Api);
        var login = new LoginPage(Page);
        await login.OpenAsync();

        await login.SignInAsync(user.Email, user.Password + "x");

        await Expect(login.IncorrectCredentialsError).ToBeVisibleAsync();
        Assert.That(await login.HasSessionAsync(), Is.False);
    }
}

/// <summary>What the waiter sees in the interface after the Owner confirmed the group pre-order.</summary>
[TestFixture, Category("UI"), Category("Waiter")]
public class WaiterUiTests : UiTestBase
{
    private static RestaurantApi Api => TestEnvironment.Api;

    [Test, Category("L2")]
    public async Task Waiter_sees_the_pre_order_with_the_dishes_confirmed_by_the_Owner()
    {
        TestUser waiter = await UserFactory.SignInWaiterAsync(Api);
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishB, 2)], submit: true);
        await scenario.AddGuestAsync("Olena", [(catalog.DishC, 4)]);
        (await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId)).EnsureSuccess();
        await UiAuth.SignInAsync(Context, waiter);

        var reservations = new WaiterReservationsPage(Page);
        await reservations.OpenAsync();
        Microsoft.Playwright.ILocator card = reservations.ReservationOn(scenario.Reservation.StartTime);
        await Expect(card).ToHaveCountAsync(1);
        await reservations.PreOrderButton(card).ClickAsync();

        var preOrder = new WaiterPreOrderPage(Page);
        await Expect(preOrder.Title).ToBeVisibleAsync();
        await Expect(preOrder.Quantity(catalog.DishA.Name, 1)).ToBeVisibleAsync();
        await Expect(preOrder.Quantity(catalog.DishB.Name, 2)).ToBeVisibleAsync();
        await Expect(preOrder.Dish(catalog.DishC.Name)).ToHaveCountAsync(0);
    }
}
