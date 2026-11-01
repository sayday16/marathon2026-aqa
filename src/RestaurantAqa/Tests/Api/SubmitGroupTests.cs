using RestaurantAqa.Api;
using RestaurantAqa.Data;

namespace RestaurantAqa.Tests.Api;

/// <summary>
/// Level 2: the Owner's own session and the confirmation of the consolidated pre-order
/// (GET /reservations/{id}/guest-session-id and POST /group-reservations/{id}/submit).
/// </summary>
[TestFixture, Category("API"), Category("L2")]
public class SubmitGroupTests
{
    private static RestaurantApi Api => TestEnvironment.Api;

    private static async Task<OrderDto> OrderOf(TestUser who, string reservationId) =>
        (await Api.GetOrderAsync(who.Token, reservationId)).EnsureSuccess().Json<OrderDto>();

    private static Dictionary<string, int> Quantities(IEnumerable<SubmittedDishDto> lines) =>
        lines.ToDictionary(l => l.DishId, l => l.Quantity);

    // ---------------------------------------------------------------- the Owner's session

    [Test]
    public async Task Owner_gets_the_id_of_the_own_session_and_it_is_stable()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse first = await Api.OwnerSessionIdAsync(scenario.Owner.Token, scenario.ReservationId);
        ApiResponse second = await Api.OwnerSessionIdAsync(scenario.Owner.Token, scenario.ReservationId);

        Assert.That(first.Status, Is.EqualTo(200), first.Describe());
        string id = first.Json<OwnerSessionDto>().GuestSessionId;
        Assert.That(id, Is.Not.Empty);
        Assert.That(second.Json<OwnerSessionDto>().GuestSessionId, Is.EqualTo(id), "the Owner session must not be duplicated");
    }

    [Test]
    public async Task Owner_session_is_listed_as_Owner_in_the_group()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)]);
        string ownerSessionId = (await Api.OwnerSessionIdAsync(scenario.Owner.Token, scenario.ReservationId))
            .EnsureSuccess().Json<OwnerSessionDto>().GuestSessionId;

        GroupSnapshotDto snapshot = await scenario.SnapshotAsync();

        ParticipantDto owner = snapshot.Participants.Single(p => p.Id == ownerSessionId);
        Assert.That(owner.Type, Is.EqualTo("Owner"));
        Assert.That(snapshot.Participants, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Owner_session_id_of_another_customers_reservation_is_403()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        TestUser stranger = await UserFactory.CreateCustomerAsync(Api, lastName: "Stranger");

        ApiResponse response = await Api.OwnerSessionIdAsync(stranger.Token, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(403), response.Describe());
    }

    [Test]
    public async Task Owner_session_id_of_an_unknown_reservation_is_404()
    {
        TestUser owner = await UserFactory.CreateCustomerAsync(Api);

        ApiResponse response = await Api.OwnerSessionIdAsync(owner.Token, Guid.NewGuid().ToString());

        Assert.That(response.Status, Is.EqualTo(404), response.Describe());
    }

    [Test]
    public async Task Owner_session_id_without_a_token_is_401()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.OwnerSessionIdAsync("", scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }

    // ---------------------------------------------------------------- successful confirmation

    [Test]
    public async Task Submit_merges_equal_dishes_of_confirmed_carts_and_ignores_unconfirmed_ones()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        await scenario.AddGuestAsync("Kateryna", [(catalog.DishA, 1), (catalog.DishB, 2)], submit: true);
        await scenario.AddGuestAsync("Olena", [(catalog.DishC, 5)]);

        ApiResponse response = await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(200), response.Describe());
        Dictionary<string, int> merged = Quantities(response.Json<List<SubmittedDishDto>>());
        Assert.That(merged, Is.EquivalentTo(new Dictionary<string, int> { [catalog.DishA.Id] = 3, [catalog.DishB.Id] = 2 }));
        Assert.That(merged.ContainsKey(catalog.DishC.Id), Is.False, "the unconfirmed cart must not be included");
    }

    [Test]
    public async Task Submit_puts_the_dishes_into_the_order_and_changes_both_statuses()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        await scenario.AddGuestAsync("Kateryna", [(catalog.DishB, 2)], submit: true);

        (await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId)).EnsureSuccess();

        OrderDto order = await OrderOf(scenario.Owner, scenario.ReservationId);
        Assert.That(order.Status, Is.EqualTo(OrderStatus.SubmittedByCustomer));
        // The Owner's own pre-order had dish A x1; the group dishes are added to it.
        Assert.That(order.OrderItems.ToDictionary(i => i.Dish.Id, i => i.Quantity),
            Is.EquivalentTo(new Dictionary<string, int> { [catalog.DishA.Id] = 3, [catalog.DishB.Id] = 2 }));

        GroupStatusDto status = (await Api.GroupStatusAsync(scenario.GroupId)).EnsureSuccess().Json<GroupStatusDto>();
        Assert.That(status.Status, Is.EqualTo("Submitted"));
    }

    [Test, Category("Waiter")]
    public async Task Waiter_sees_the_confirmed_order_with_the_composition_confirmed_by_the_Owner()
    {
        TestUser waiter = await UserFactory.SignInWaiterAsync(Api);
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishB, 2)], submit: true);
        await scenario.AddGuestAsync("Olena", [(catalog.DishC, 4)]);
        (await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId)).EnsureSuccess();

        OrderDto order = await OrderOf(waiter, scenario.ReservationId);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.SubmittedByCustomer));
        Assert.That(order.OrderItems.ToDictionary(i => i.Dish.Id, i => i.Quantity),
            Is.EquivalentTo(new Dictionary<string, int> { [catalog.DishA.Id] = 1, [catalog.DishB.Id] = 2 }));
    }

    // ---------------------------------------------------------------- conditions that forbid the confirmation

    [Test]
    public async Task Submit_without_any_confirmed_cart_is_400_and_changes_nothing()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Olena", [(scenario.Catalog.DishB, 1)]);

        ApiResponse response = await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
        Assert.That(response.ErrorText, Does.Contain("submitted").IgnoreCase);
        Assert.That((await OrderOf(scenario.Owner, scenario.ReservationId)).Status, Is.EqualTo(OrderStatus.Created));
        Assert.That((await Api.GroupStatusAsync(scenario.GroupId)).Json<GroupStatusDto>().Status, Is.EqualTo("Open"));
    }

    [Test]
    public async Task Submit_of_a_group_without_guests_is_400()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
    }

    [Test]
    public async Task A_guest_can_not_confirm_an_empty_cart_so_confirmed_carts_always_contain_dishes()
    {
        // The rule "confirmed carts without any dish -> 400" is not reachable through the public API:
        // the guest endpoint refuses to confirm an empty cart (covered by the backend unit tests).
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        GuestSessionDto session = (await Api.JoinGroupAsync(scenario.GroupId, "Mariana")).EnsureSuccess().Json<GuestSessionDto>();

        ApiResponse response = await Api.SubmitCartAsync(session.SessionToken, session.SessionId);

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
    }

    [Test]
    public async Task Submit_without_an_order_is_400()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api, withOrder: false);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)], submit: true);

        ApiResponse response = await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
        Assert.That(response.ErrorText, Does.Contain("order").IgnoreCase);
    }

    [Test]
    public async Task Submit_of_an_unknown_group_is_404()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.SubmitGroupAsync(scenario.Owner.Token, Guid.NewGuid().ToString(), scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(404), response.Describe());
    }

    [Test]
    public async Task Submit_for_an_unknown_reservation_is_404()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)], submit: true);

        ApiResponse response = await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, Guid.NewGuid().ToString());

        Assert.That(response.Status, Is.EqualTo(404), response.Describe());
    }

    [Test]
    public async Task Submit_with_a_reservation_that_belongs_to_another_group_is_400()
    {
        await using GroupScenario first = await GroupScenario.CreateAsync(Api);
        await using GroupScenario second = await GroupScenario.CreateAsync(Api, owner: first.Owner);
        await first.AddGuestAsync("Andrii", [(first.Catalog.DishA, 1)], submit: true);

        ApiResponse response = await Api.SubmitGroupAsync(first.Owner.Token, first.GroupId, second.ReservationId);

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
        Assert.That((await Api.GroupStatusAsync(first.GroupId)).Json<GroupStatusDto>().Status, Is.EqualTo("Open"));
    }

    // ---------------------------------------------------------------- who may confirm

    [Test]
    public async Task Another_customer_can_not_submit_403()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)], submit: true);
        TestUser stranger = await UserFactory.CreateCustomerAsync(Api, lastName: "Stranger");

        ApiResponse response = await Api.SubmitGroupAsync(stranger.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(403), response.Describe());
        Assert.That((await Api.GroupStatusAsync(scenario.GroupId)).Json<GroupStatusDto>().Status, Is.EqualTo("Open"));
    }

    [Test]
    public async Task Guest_token_can_not_submit_401()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest guest = await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)], submit: true);

        ApiResponse response = await Api.SubmitGroupAsync(guest.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }

    [Test]
    public async Task Unauthenticated_submit_is_401()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.SubmitGroupAsync(null, scenario.GroupId, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }

    [Test, Category("Waiter")]
    public async Task Waiter_can_not_submit_403()
    {
        TestUser waiter = await UserFactory.SignInWaiterAsync(Api);
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)], submit: true);

        ApiResponse response = await Api.SubmitGroupAsync(waiter.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(response.Status, Is.EqualTo(403), response.Describe());
    }

    // ---------------------------------------------------------------- repeated submit and what follows it

    [Test]
    public async Task Second_submit_of_a_confirmed_group_is_rejected_and_does_not_change_the_order()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 2)], submit: true);
        (await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId)).EnsureSuccess();
        OrderDto afterFirst = await OrderOf(scenario.Owner, scenario.ReservationId);

        ApiResponse second = await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(second.Status, Is.EqualTo(400), second.Describe());
        Assert.That(second.ErrorCode, Is.EqualTo("GroupReservationSubmitted"));
        OrderDto afterSecond = await OrderOf(scenario.Owner, scenario.ReservationId);
        Assert.That(afterSecond.OrderItems.ToDictionary(i => i.Dish.Id, i => i.Quantity),
            Is.EquivalentTo(afterFirst.OrderItems.ToDictionary(i => i.Dish.Id, i => i.Quantity)));
    }

    [Test]
    public async Task A_guest_can_not_join_a_group_that_is_already_confirmed()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)], submit: true);
        (await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId)).EnsureSuccess();

        ApiResponse response = await Api.JoinGroupAsync(scenario.GroupId, "Late");

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
        Assert.That(response.ErrorCode, Is.EqualTo("GroupReservationSubmitted"));
    }

    // ---------------------------------------------------------------- request validation

    [Test]
    public async Task Submit_without_a_body_is_400()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.SubmitGroupRawAsync(scenario.Owner.Token, scenario.GroupId, "");

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
    }

    [Test]
    public async Task Submit_without_reservationId_is_400()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.SubmitGroupRawAsync(scenario.Owner.Token, scenario.GroupId, "{}");

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
        Assert.That(response.Body, Does.Contain("ReservationId"));
    }

    [Test]
    public async Task Submit_with_malformed_json_is_400()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.SubmitGroupRawAsync(scenario.Owner.Token, scenario.GroupId, "{ not json");

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
    }
}
