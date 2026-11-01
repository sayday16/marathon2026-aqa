using RestaurantAqa.Api;
using RestaurantAqa.Data;

namespace RestaurantAqa.Tests.Api;

/// <summary>Level 3: the Owner removes a participant (DELETE /guest/groups/{id}/sessions/{guestSessionId}).</summary>
[TestFixture, Category("API"), Category("L3")]
public class RemoveParticipantTests
{
    private static RestaurantApi Api => TestEnvironment.Api;

    private static async Task<GroupSnapshotDto> Remove(GroupScenario scenario, Guest guest)
    {
        ApiResponse response = await Api.RemoveParticipantAsync(scenario.Owner.Token, scenario.GroupId, guest.SessionId);
        Assert.That(response.Status, Is.EqualTo(200), response.Describe());
        GroupSnapshotDto snapshot = response.Json<GroupSnapshotDto>();

        // The list is read from a secondary index of DynamoDB, which is eventually consistent:
        // right after the delete the response can still contain the removed guest. Wait for the index to catch up.
        for (int attempt = 0; attempt < 10 && snapshot.Participants.Any(p => p.Id == guest.SessionId); attempt++)
        {
            TestContext.Progress.WriteLine($"The DELETE response still lists the removed guest {guest.Name} (stale index), reading the list again.");
            await Task.Delay(1000);
            snapshot = await scenario.SnapshotAsync();
        }

        return snapshot;
    }

    [Test]
    public async Task Removing_one_of_several_participants_keeps_the_others_and_recalculates_the_total()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        Guest andrii = await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)]);
        Guest olena = await scenario.AddGuestAsync("Olena", [(catalog.DishB, 1)]);
        Guest ivan = await scenario.AddGuestAsync("Ivan", [(catalog.DishC, 3)]);

        GroupSnapshotDto afterRemoval = await Remove(scenario, olena);

        Assert.That(afterRemoval.Participants.Select(p => p.Id), Is.EquivalentTo(new[] { andrii.SessionId, ivan.SessionId }));
        Assert.That(afterRemoval.Total, Is.EqualTo(2 * catalog.DishA.Price + 3 * catalog.DishC.Price));
        Assert.That(afterRemoval.Total, Is.EqualTo(afterRemoval.Participants.Sum(p => p.Total)));

        GroupSnapshotDto reloaded = await scenario.SnapshotAsync();
        Assert.That(reloaded.Participants.Select(p => p.Id), Is.EquivalentTo(afterRemoval.Participants.Select(p => p.Id)));
        Assert.That(reloaded.Total, Is.EqualTo(afterRemoval.Total));
    }

    [Test]
    public async Task A_participant_who_already_confirmed_the_cart_can_be_removed()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        Guest confirmed = await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        Guest other = await scenario.AddGuestAsync("Olena", [(catalog.DishB, 1)]);

        GroupSnapshotDto afterRemoval = await Remove(scenario, confirmed);

        Assert.That(afterRemoval.Participants.Select(p => p.Id), Is.EquivalentTo(new[] { other.SessionId }));
        Assert.That(afterRemoval.Total, Is.EqualTo(catalog.DishB.Price));
    }

    [Test]
    public async Task The_removed_participant_does_not_affect_the_confirmed_pre_order()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        Guest removed = await scenario.AddGuestAsync("Andrii", [(catalog.DishB, 4)], submit: true);
        await scenario.AddGuestAsync("Olena", [(catalog.DishC, 1)], submit: true);
        await Remove(scenario, removed);

        ApiResponse submit = await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId);

        Assert.That(submit.Status, Is.EqualTo(200), submit.Describe());
        Assert.That(submit.Json<List<SubmittedDishDto>>().ToDictionary(l => l.DishId, l => l.Quantity),
            Is.EquivalentTo(new Dictionary<string, int> { [catalog.DishC.Id] = 1 }));
    }

    [Test]
    public async Task Removing_the_last_participant_leaves_an_empty_group()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest only = await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)]);

        GroupSnapshotDto afterRemoval = await Remove(scenario, only);

        Assert.That(afterRemoval.Participants, Is.Empty);
        Assert.That(afterRemoval.Total, Is.EqualTo(0m));
    }

    [Test]
    public async Task The_removed_guest_loses_access_to_the_own_session()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest guest = await scenario.AddGuestAsync("Andrii", [(scenario.Catalog.DishA, 1)]);
        await Remove(scenario, guest);

        ApiResponse response = await Api.AddDishAsync(guest.Token, guest.SessionId, scenario.Catalog.DishB.Id, 1);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }

    [Test]
    public async Task Removing_the_same_participant_twice_returns_404_the_second_time()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest guest = await scenario.AddGuestAsync("Andrii");
        await Remove(scenario, guest);

        ApiResponse second = await Api.RemoveParticipantAsync(scenario.Owner.Token, scenario.GroupId, guest.SessionId);

        Assert.That(second.Status, Is.EqualTo(404), second.Describe());
    }

    // ---------------------------------------------------------------- what must be refused

    [Test]
    public async Task A_participant_of_another_group_is_400_and_both_groups_stay_unchanged()
    {
        await using GroupScenario groupOne = await GroupScenario.CreateAsync(Api);
        await using GroupScenario groupTwo = await GroupScenario.CreateAsync(Api, owner: groupOne.Owner);
        Guest inOne = await groupOne.AddGuestAsync("Andrii", [(groupOne.Catalog.DishA, 1)]);
        Guest inTwo = await groupTwo.AddGuestAsync("Olena", [(groupTwo.Catalog.DishB, 1)]);

        // The Owner owns both groups, so only the "participant belongs to this group" rule can refuse it.
        ApiResponse response = await Api.RemoveParticipantAsync(groupOne.Owner.Token, groupOne.GroupId, inTwo.SessionId);

        Assert.That(response.Status, Is.EqualTo(400), response.Describe());
        Assert.That((await groupOne.SnapshotAsync()).Participants.Select(p => p.Id), Is.EquivalentTo(new[] { inOne.SessionId }));
        Assert.That((await groupTwo.SnapshotAsync()).Participants.Select(p => p.Id), Is.EquivalentTo(new[] { inTwo.SessionId }));
    }

    [Test]
    public async Task An_unknown_participant_is_404()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.RemoveParticipantAsync(scenario.Owner.Token, scenario.GroupId, Guid.NewGuid().ToString());

        Assert.That(response.Status, Is.EqualTo(404), response.Describe());
    }

    [Test]
    public async Task An_unknown_group_is_404()
    {
        TestUser owner = await UserFactory.CreateCustomerAsync(Api);

        ApiResponse response = await Api.RemoveParticipantAsync(owner.Token, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        Assert.That(response.Status, Is.EqualTo(404), response.Describe());
    }

    [Test]
    public async Task Another_customer_can_not_remove_a_participant_403()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest guest = await scenario.AddGuestAsync("Andrii");
        TestUser stranger = await UserFactory.CreateCustomerAsync(Api, lastName: "Stranger");

        ApiResponse response = await Api.RemoveParticipantAsync(stranger.Token, scenario.GroupId, guest.SessionId);

        Assert.That(response.Status, Is.EqualTo(403), response.Describe());
        Assert.That((await scenario.SnapshotAsync()).Participants, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_guest_can_not_remove_a_participant_401()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest first = await scenario.AddGuestAsync("Andrii");
        Guest second = await scenario.AddGuestAsync("Olena");

        ApiResponse response = await Api.RemoveParticipantAsync(first.Token, scenario.GroupId, second.SessionId);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
        Assert.That((await scenario.SnapshotAsync()).Participants, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Unauthenticated_removal_is_401()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest guest = await scenario.AddGuestAsync("Andrii");

        ApiResponse response = await Api.RemoveParticipantAsync(null, scenario.GroupId, guest.SessionId);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }

    [Test, Category("Waiter")]
    public async Task Waiter_can_not_remove_a_participant_403()
    {
        TestUser waiter = await UserFactory.SignInWaiterAsync(Api);
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest guest = await scenario.AddGuestAsync("Andrii");

        ApiResponse response = await Api.RemoveParticipantAsync(waiter.Token, scenario.GroupId, guest.SessionId);

        Assert.That(response.Status, Is.EqualTo(403), response.Describe());
    }

    // ---------------------------------------------------------------- end to end

    [Test, Category("E2E"), Category("Waiter")]
    public async Task Guests_order_Owner_reviews_removes_one_confirms_and_the_waiter_sees_the_order_without_the_removed_guest()
    {
        TestUser waiter = await UserFactory.SignInWaiterAsync(Api);
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        Guest andrii = await scenario.AddGuestAsync("Andrii", [(catalog.DishB, 2)], submit: true);
        Guest olena = await scenario.AddGuestAsync("Olena", [(catalog.DishC, 3)], submit: true);
        await scenario.AddGuestAsync("Ivan", [(catalog.DishB, 1)], submit: true);

        GroupSnapshotDto overview = await scenario.SnapshotAsync();
        Assert.That(overview.Participants, Has.Count.EqualTo(3));
        Assert.That(overview.Total, Is.EqualTo(3 * catalog.DishB.Price + 3 * catalog.DishC.Price));

        GroupSnapshotDto afterRemoval = await Remove(scenario, olena);
        Assert.That(afterRemoval.Participants.Select(p => p.Id), Does.Not.Contain(olena.SessionId));
        Assert.That(afterRemoval.Participants.Select(p => p.Id), Does.Contain(andrii.SessionId));
        Assert.That(afterRemoval.Total, Is.EqualTo(3 * catalog.DishB.Price));

        ApiResponse submit = await Api.SubmitGroupAsync(scenario.Owner.Token, scenario.GroupId, scenario.ReservationId);
        Assert.That(submit.Status, Is.EqualTo(200), submit.Describe());

        OrderDto order = (await Api.GetOrderAsync(waiter.Token, scenario.ReservationId)).EnsureSuccess().Json<OrderDto>();
        Assert.That(order.Status, Is.EqualTo(OrderStatus.SubmittedByCustomer));
        Assert.That(order.OrderItems.ToDictionary(i => i.Dish.Id, i => i.Quantity),
            Is.EquivalentTo(new Dictionary<string, int> { [catalog.DishA.Id] = 1, [catalog.DishB.Id] = 3 }));
        Assert.That(order.OrderItems.Select(i => i.Dish.Id), Does.Not.Contain(catalog.DishC.Id));
    }
}
