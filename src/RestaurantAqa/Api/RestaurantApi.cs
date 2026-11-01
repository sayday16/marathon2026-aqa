namespace RestaurantAqa.Api;

public sealed record OrderItemInput(string DishId, int Quantity);

/// <summary>
/// Endpoints of the restaurant backend used by the tests. Every method returns the raw
/// <see cref="ApiResponse"/> so that tests can assert both successful and failing calls.
/// </summary>
public sealed class RestaurantApi(ApiClient client)
{
    // ---------------------------------------------------------------- auth
    public Task<ApiResponse> RegisterAsync(string firstName, string lastName, string email, string password, string? confirmPassword = null) =>
        client.PostAsync("register", new { firstName, lastName, email, password, confirmPassword = confirmPassword ?? password });

    public Task<ApiResponse> SignInAsync(string email, string password) =>
        client.PostAsync("signin", new { email, password });

    public Task<ApiResponse> MeAsync(string token) => client.GetAsync("me", token);

    // ---------------------------------------------------------------- catalog
    public Task<ApiResponse> DishesAsync() => client.GetAsync("dishes");

    public Task<ApiResponse> AvailableTablesAsync(string token, DateTime startUtc, int capacity) =>
        client.GetAsync($"available-tables?startTime={Uri.EscapeDataString(startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"))}&capacity={capacity}", token);

    // ---------------------------------------------------------------- reservations and orders
    public Task<ApiResponse> CreateReservationAsync(string token, string tableId, DateTime startUtc, DateTime endUtc, int guestCount) =>
        client.PostAsync("reservations", new { tableId, startTime = startUtc, endTime = endUtc, guestCount }, token);

    public Task<ApiResponse> CancelReservationAsync(string token, string reservationId) =>
        client.DeleteAsync("reservations", new { id = reservationId }, token);

    public Task<ApiResponse> MyReservationsAsync(string token) => client.GetAsync("reservations", token);

    public Task<ApiResponse> InviteTokenAsync(string token, string reservationId) =>
        client.GetAsync($"reservations/{reservationId}/invite-token", token);

    public Task<ApiResponse> OwnerSessionIdAsync(string token, string reservationId) =>
        client.GetAsync($"reservations/{reservationId}/guest-session-id", token);

    public Task<ApiResponse> CreateOrderAsync(string token, string reservationId, IEnumerable<OrderItemInput> items) =>
        client.PostAsync("orders", new { reservationId, orderItems = items.Select(i => new { dishId = i.DishId, quantity = i.Quantity }) }, token);

    public Task<ApiResponse> GetOrderAsync(string token, string reservationId) =>
        client.GetAsync($"orders/{reservationId}", token);

    public Task<ApiResponse> CancelOrderAsync(string token, string reservationId) =>
        client.DeleteAsync($"orders/{reservationId}", null, token);

    // ---------------------------------------------------------------- guests (guest session token as bearer)
    public Task<ApiResponse> InviteAsync(string inviteToken) => client.GetAsync($"guest/invites/{inviteToken}");

    public Task<ApiResponse> JoinGroupAsync(string groupReservationId, string guestName) =>
        client.PostAsync("guest/sessions", new { groupReservationId, guestName });

    public Task<ApiResponse> AddDishAsync(string guestToken, string guestSessionId, string dishId, int quantity) =>
        client.PostAsync($"guest/sessions/{guestSessionId}/dishes", new { dishId, quantity }, guestToken);

    public Task<ApiResponse> RemoveDishAsync(string guestToken, string guestSessionId, string dishId, int quantity) =>
        client.DeleteAsync($"guest/sessions/{guestSessionId}/dishes", new { dishId, quantity }, guestToken);

    public Task<ApiResponse> SubmitCartAsync(string guestToken, string guestSessionId) =>
        client.PostAsync($"guest/sessions/{guestSessionId}/submit", null, guestToken);

    // ---------------------------------------------------------------- group pre-order (owner token as bearer)
    public Task<ApiResponse> GroupSessionsAsync(string? token, string groupReservationId) =>
        client.GetAsync($"guest/groups/{groupReservationId}/sessions", token);

    public Task<ApiResponse> GroupStatusAsync(string groupReservationId) =>
        client.GetAsync($"guest/groups/{groupReservationId}/status");

    public Task<ApiResponse> SubmitGroupAsync(string? token, string groupReservationId, string reservationId) =>
        client.PostAsync($"group-reservations/{groupReservationId}/submit", new { reservationId }, token);

    public Task<ApiResponse> SubmitGroupRawAsync(string? token, string groupReservationId, string rawBody) =>
        client.SendRawAsync(HttpMethod.Post, $"group-reservations/{groupReservationId}/submit", rawBody, token);

    public Task<ApiResponse> RemoveParticipantAsync(string? token, string groupReservationId, string guestSessionId) =>
        client.DeleteAsync($"guest/groups/{groupReservationId}/sessions/{guestSessionId}", null, token);
}
