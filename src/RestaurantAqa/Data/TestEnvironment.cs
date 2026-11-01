using RestaurantAqa.Api;
using RestaurantAqa.Config;

namespace RestaurantAqa.Data;

public static class ApiResponseExtensions
{
    /// <summary>Test setup helper: fails the test with the full response when a precondition call did not succeed.</summary>
    public static ApiResponse EnsureSuccess(this ApiResponse response)
    {
        if (!response.IsSuccess)
        {
            throw new AssertionException($"Test data setup failed: {response.Describe()}");
        }

        return response;
    }
}

/// <summary>Shared, thread-safe access to the backend for all fixtures (one HttpClient for the whole run).</summary>
public static class TestEnvironment
{
    private static readonly Lazy<ApiClient> Client = new(() => new ApiClient(TestSettings.Current.ApiBaseUrl));
    private static readonly Lazy<RestaurantApi> SharedApi = new(() => new RestaurantApi(Client.Value));

    public static RestaurantApi Api => SharedApi.Value;

    public static void Shutdown()
    {
        if (Client.IsValueCreated)
        {
            Client.Value.Dispose();
        }
    }
}

public sealed record TestUser(string Id, string FirstName, string LastName, string Email, string Password, string Token, string Role);

public sealed record Guest(string Name, string SessionId, string Token);

/// <summary>Creates and signs in users with unique emails: the backend has no API to delete accounts.</summary>
public static class UserFactory
{
    public const string EmailPrefix = "aqa-";

    public static async Task<TestUser> CreateCustomerAsync(RestaurantApi api, string firstName = "Aqa", string lastName = "Owner")
    {
        string email = $"{EmailPrefix}{Guid.NewGuid():N}@example.com";
        // "Aqa#1" guarantees an upper case letter, lower case letters, a digit and a special character; 12 characters in total.
        string password = $"Aqa#1{Guid.NewGuid():N}"[..12];

        (await api.RegisterAsync(firstName, lastName, email, password)).EnsureSuccess();
        return await SignInAsync(api, email, password);
    }

    public static async Task<TestUser> SignInAsync(RestaurantApi api, string email, string password)
    {
        SignInResult tokens = (await api.SignInAsync(email, password)).EnsureSuccess().Json<SignInResult>();
        CurrentUser me = (await api.MeAsync(tokens.IdToken)).EnsureSuccess().Json<CurrentUser>();
        return new TestUser(me.Id, me.FirstName, me.LastName, me.Email, password, tokens.IdToken, me.Role);
    }

    /// <summary>The waiter of the tested location; only available when its credentials are configured.</summary>
    public static Task<TestUser> SignInWaiterAsync(RestaurantApi api)
    {
        TestSettings settings = TestSettings.Current;
        if (!settings.HasWaiter)
        {
            Assert.Ignore("WAITER_EMAIL and WAITER_PASSWORD are not configured: waiter scenarios are skipped.");
        }

        return SignInAsync(api, settings.WaiterEmail!, settings.WaiterPassword!);
    }
}
