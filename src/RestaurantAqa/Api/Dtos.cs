namespace RestaurantAqa.Api;

public sealed record SignInResult(string Message, string IdToken, string AccessToken);

public sealed record CurrentUser(string Id, string FirstName, string LastName, string Email, string Role);

public sealed record ReservationDto(string Id, string TableId, DateTime StartTime, DateTime EndTime, int GuestCount, int Status, string? CustomerId, string? WaiterId);

public sealed record DishDto(string Id, string Name, decimal Price, string State);

public sealed record TableDto(string Id, int Capacity, int TableNumber);

public sealed record InviteTokenDto(string Token);

/// <summary>Response of GET /guest/invites/{token}: the gateway serializes it with PascalCase names.</summary>
public sealed record InviteDto(string GroupReservationId);

/// <summary>Response of POST /guest/sessions (PascalCase on the wire).</summary>
public sealed record GuestSessionDto(string SessionId, string SessionToken);

public sealed record OwnerSessionDto(string GuestSessionId);

public sealed record GroupStatusDto(int CurrentParticipants, int MaxCapacity, string Status);

public sealed record DishLineDto(DishDto Dish, int Quantity);

public sealed record ParticipantDto(string Id, string Type, string Name, List<DishLineDto> Items, decimal Total, bool IsSubmitted);

public sealed record GroupSnapshotDto(List<ParticipantDto> Participants, decimal Total);

public sealed record SubmittedDishDto(string DishId, int Quantity);

public sealed record OrderDto(string OrderId, string ReservationId, int Status, List<DishLineDto> OrderItems);

/// <summary>Order status values of the backend (Created = 0, Submitted = 1, Cancelled = 2, SubmittedByCustomer = 3).</summary>
public static class OrderStatus
{
    public const int Created = 0;
    public const int Submitted = 1;
    public const int Cancelled = 2;
    public const int SubmittedByCustomer = 3;
}

public static class ReservationStatus
{
    public const int Reserved = 0;
    public const int Cancelled = 1;
}
