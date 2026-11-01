using RestaurantAqa.Api;

namespace RestaurantAqa.Data;

/// <summary>
/// A ready group pre-order: an Owner with a reservation for several guests (and, by default, a pre-order),
/// ready for guests to join. Disposing cancels the order and the reservation, so a run leaves no active data.
/// </summary>
public sealed class GroupScenario : IAsyncDisposable
{
    private readonly RestaurantApi _api;

    private GroupScenario(RestaurantApi api, TestUser owner, CatalogData catalog, ReservationDto reservation, string groupId, bool hasOrder)
    {
        _api = api;
        Owner = owner;
        Catalog = catalog;
        Reservation = reservation;
        GroupId = groupId;
        HasOrder = hasOrder;
    }

    public TestUser Owner { get; }

    public CatalogData Catalog { get; }

    public ReservationDto Reservation { get; }

    public string GroupId { get; }

    public bool HasOrder { get; }

    public string ReservationId => Reservation.Id;

    public List<Guest> Guests { get; } = [];

    /// <param name="withOrder">The reservation needs an order for the group to be submittable; the Owner puts one dish A into it.</param>
    public static async Task<GroupScenario> CreateAsync(RestaurantApi api, bool withOrder = true, TestUser? owner = null)
    {
        owner ??= await UserFactory.CreateCustomerAsync(api);
        CatalogData catalog = await CatalogResolver.ResolveAsync(api, owner.Token);

        ReservationDto reservation = await CreateReservationAsync(api, owner, catalog);

        if (withOrder)
        {
            await CreateOrderAsync(api, owner, reservation.Id, catalog.DishA);
        }

        string inviteToken = (await api.InviteTokenAsync(owner.Token, reservation.Id)).EnsureSuccess().Json<InviteTokenDto>().Token;
        string groupId = (await api.InviteAsync(inviteToken)).EnsureSuccess().Json<InviteDto>().GroupReservationId;

        return new GroupScenario(api, owner, catalog, reservation, groupId, withOrder);
    }

    /// <summary>A guest joins by the invitation, puts dishes into the cart and optionally confirms the cart.</summary>
    public async Task<Guest> AddGuestAsync(string name, IEnumerable<(DishDto Dish, int Quantity)>? dishes = null, bool submit = false)
    {
        GuestSessionDto session = (await _api.JoinGroupAsync(GroupId, name)).EnsureSuccess().Json<GuestSessionDto>();
        var guest = new Guest(name, session.SessionId, session.SessionToken);

        foreach ((DishDto dish, int quantity) in dishes ?? [])
        {
            (await _api.AddDishAsync(guest.Token, guest.SessionId, dish.Id, quantity)).EnsureSuccess();
        }

        if (submit)
        {
            (await _api.SubmitCartAsync(guest.Token, guest.SessionId)).EnsureSuccess();
        }

        Guests.Add(guest);
        return guest;
    }

    public async Task<GroupSnapshotDto> SnapshotAsync() =>
        (await _api.GroupSessionsAsync(Owner.Token, GroupId)).EnsureSuccess().Json<GroupSnapshotDto>();

    public async ValueTask DisposeAsync()
    {
        // Best effort: cleanup problems must not hide the result of the test itself.
        try
        {
            if (HasOrder)
            {
                Report("order", await _api.CancelOrderAsync(Owner.Token, ReservationId));
            }

            Report("reservation", await _api.CancelReservationAsync(Owner.Token, ReservationId));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            TestContext.Progress.WriteLine($"Cleanup of reservation {ReservationId} failed: {exception.Message}");
        }
    }

    // A cleanup that does not work leaves reservations behind and fills the table, so it must never fail silently.
    private void Report(string what, ApiResponse response)
    {
        if (response.Status is < 200 or >= 300)
        {
            TestContext.Progress.WriteLine($"Cleanup of the {what} of reservation {ReservationId} failed: {response.Describe()}");
        }
    }

    private static async Task CreateOrderAsync(RestaurantApi api, TestUser owner, string reservationId, DishDto dish)
    {
        OrderItemInput[] items = [new OrderItemInput(dish.Id, 1)];
        ApiResponse response = await api.CreateOrderAsync(owner.Token, reservationId, items);

        if (response.Status >= 500)
        {
            // A gateway timeout does not prove the order was not saved: look before creating a second one.
            if ((await api.GetOrderAsync(owner.Token, reservationId)).Status == 200)
            {
                return;
            }

            response = await api.CreateOrderAsync(owner.Token, reservationId, items);
        }

        response.EnsureSuccess();
    }

    private static async Task<ReservationDto> CreateReservationAsync(RestaurantApi api, TestUser owner, CatalogData catalog)
    {
        ApiResponse? last = null;
        int gatewayErrors = 0;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            DateTime start = SlotAllocator.RandomSlotStart();
            last = await api.CreateReservationAsync(owner.Token, catalog.TableId, start, start + SlotAllocator.Duration, SlotAllocator.GuestCount);

            if (last.Status == 201)
            {
                return last.Json<ReservationDto>();
            }

            // A gateway timeout (504) of a cold Lambda is transient: try again, in another slot, because the first request may have saved a reservation.
            if (last.Status >= 500 && ++gatewayErrors <= 4)
            {
                TestContext.Progress.WriteLine($"POST /reservations -> HTTP {last.Status}, trying another slot.");
                await Task.Delay(TimeSpan.FromSeconds(2 * gatewayErrors));
                continue;
            }

            // The only other expected failure is a collision with another reservation of the same table: try another slot.
            if (last.Status != 400 || !last.ErrorText.Contains("overlap", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        throw new AssertionException($"Could not create a reservation: {last?.Describe()}");
    }
}
