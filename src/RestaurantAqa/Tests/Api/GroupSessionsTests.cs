using RestaurantAqa.Api;
using RestaurantAqa.Data;

namespace RestaurantAqa.Tests.Api;

/// <summary>Level 1: the Owner views the participants of the group pre-order (GET /guest/groups/{id}/sessions).</summary>
[TestFixture, Category("API"), Category("L1")]
public class GroupSessionsTests
{
    private static RestaurantApi Api => TestEnvironment.Api;

    private static ParticipantDto Participant(GroupSnapshotDto snapshot, Guest guest) =>
        snapshot.Participants.Single(p => p.Id == guest.SessionId);

    [Test]
    public async Task Owner_sees_participants_with_dishes_quantities_and_totals()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        Guest andrii = await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        Guest olena = await scenario.AddGuestAsync("Olena", [(catalog.DishB, 1), (catalog.DishC, 3)]);

        GroupSnapshotDto snapshot = await scenario.SnapshotAsync();

        Assert.That(snapshot.Participants, Has.Count.EqualTo(2));

        ParticipantDto first = Participant(snapshot, andrii);
        Assert.That(first.Name, Is.EqualTo("Andrii"));
        Assert.That(first.Type, Is.EqualTo("Guest"));
        Assert.That(first.IsSubmitted, Is.True);
        Assert.That(first.Items.Select(i => (i.Dish.Id, i.Quantity)), Is.EquivalentTo(new[] { (catalog.DishA.Id, 2) }));
        Assert.That(first.Total, Is.EqualTo(2 * catalog.DishA.Price));

        ParticipantDto second = Participant(snapshot, olena);
        Assert.That(second.IsSubmitted, Is.False);
        Assert.That(second.Items.Select(i => (i.Dish.Id, i.Quantity)),
            Is.EquivalentTo(new[] { (catalog.DishB.Id, 1), (catalog.DishC.Id, 3) }));
        Assert.That(second.Total, Is.EqualTo(catalog.DishB.Price + 3 * catalog.DishC.Price));
    }

    [Test]
    public async Task Grand_total_equals_the_sum_of_participants_including_unconfirmed_carts()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 2)], submit: true);
        await scenario.AddGuestAsync("Olena", [(catalog.DishB, 1)]);

        GroupSnapshotDto snapshot = await scenario.SnapshotAsync();

        Assert.That(snapshot.Total, Is.EqualTo(snapshot.Participants.Sum(p => p.Total)));
        Assert.That(snapshot.Total, Is.EqualTo(2 * catalog.DishA.Price + catalog.DishB.Price));
    }

    [Test]
    public async Task Grand_total_changes_when_a_guest_changes_the_cart()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        CatalogData catalog = scenario.Catalog;
        Guest guest = await scenario.AddGuestAsync("Andrii", [(catalog.DishA, 1)]);
        Assert.That((await scenario.SnapshotAsync()).Total, Is.EqualTo(catalog.DishA.Price));

        (await Api.AddDishAsync(guest.Token, guest.SessionId, catalog.DishB.Id, 2)).EnsureSuccess();
        Assert.That((await scenario.SnapshotAsync()).Total, Is.EqualTo(catalog.DishA.Price + 2 * catalog.DishB.Price));

        (await Api.RemoveDishAsync(guest.Token, guest.SessionId, catalog.DishA.Id, 1)).EnsureSuccess();
        GroupSnapshotDto afterRemoval = await scenario.SnapshotAsync();
        Assert.That(afterRemoval.Total, Is.EqualTo(2 * catalog.DishB.Price));
        Assert.That(Participant(afterRemoval, guest).Items.Select(i => i.Dish.Id), Is.EquivalentTo(new[] { catalog.DishB.Id }));
    }

    [Test]
    public async Task Participant_without_dishes_is_listed_with_empty_cart_and_zero_total()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest guest = await scenario.AddGuestAsync("Mariana");

        GroupSnapshotDto snapshot = await scenario.SnapshotAsync();

        ParticipantDto participant = Participant(snapshot, guest);
        Assert.That(participant.Items, Is.Empty);
        Assert.That(participant.Total, Is.EqualTo(0m));
        Assert.That(participant.IsSubmitted, Is.False);
        Assert.That(snapshot.Total, Is.EqualTo(0m));
    }

    [Test]
    public async Task Group_without_guests_returns_an_empty_list_and_zero_total()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.GroupSessionsAsync(scenario.Owner.Token, scenario.GroupId);

        Assert.That(response.Status, Is.EqualTo(200), response.Describe());
        GroupSnapshotDto snapshot = response.Json<GroupSnapshotDto>();
        Assert.That(snapshot.Participants, Is.Empty);
        Assert.That(snapshot.Total, Is.EqualTo(0m));
    }

    // ---------------------------------------------------------------- access and unknown group

    [Test]
    public async Task Another_customer_gets_403()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        TestUser stranger = await UserFactory.CreateCustomerAsync(Api, lastName: "Stranger");

        ApiResponse response = await Api.GroupSessionsAsync(stranger.Token, scenario.GroupId);

        Assert.That(response.Status, Is.EqualTo(403), response.Describe());
    }

    [Test]
    public async Task Guest_token_is_rejected_with_401()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);
        Guest guest = await scenario.AddGuestAsync("Andrii");

        ApiResponse response = await Api.GroupSessionsAsync(guest.Token, scenario.GroupId);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }

    [Test]
    public async Task Unauthenticated_request_gets_401()
    {
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.GroupSessionsAsync(null, scenario.GroupId);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }

    [Test, Category("Waiter")]
    public async Task Waiter_gets_403()
    {
        TestUser waiter = await UserFactory.SignInWaiterAsync(Api);
        await using GroupScenario scenario = await GroupScenario.CreateAsync(Api);

        ApiResponse response = await Api.GroupSessionsAsync(waiter.Token, scenario.GroupId);

        Assert.That(response.Status, Is.EqualTo(403), response.Describe());
    }

    [Test]
    public async Task Unknown_group_returns_404()
    {
        TestUser owner = await UserFactory.CreateCustomerAsync(Api);

        ApiResponse response = await Api.GroupSessionsAsync(owner.Token, Guid.NewGuid().ToString());

        Assert.That(response.Status, Is.EqualTo(404), response.Describe());
    }
}
