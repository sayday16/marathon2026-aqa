using RestaurantAqa.Api;
using RestaurantAqa.Data;

namespace RestaurantAqa.Tests.Api;

/// <summary>
/// Quick checks that need no accounts and create no data: the environment is reachable and the group endpoints
/// are published and protected. A run that fails here has a configuration problem, not a product problem.
/// </summary>
[TestFixture, Category("API"), Category("Smoke")]
public class SmokeTests
{
    private static RestaurantApi Api => TestEnvironment.Api;

    private static string Unknown => Guid.NewGuid().ToString();

    [Test]
    public async Task The_public_group_status_of_an_unknown_group_is_404_with_a_json_error()
    {
        ApiResponse response = await Api.GroupStatusAsync(Unknown);

        Assert.That(response.Status, Is.EqualTo(404), response.Describe());
        Assert.That(response.ErrorText, Is.Not.Empty);
    }

    [Test]
    public async Task The_dish_catalog_is_public_and_has_dishes_to_order()
    {
        ApiResponse response = await Api.DishesAsync();

        Assert.That(response.Status, Is.EqualTo(200), response.Describe());
        Assert.That(response.Json<List<DishDto>>(), Is.Not.Empty);
    }

    [TestCase("GET", "guest/groups/{id}/sessions")]
    [TestCase("GET", "reservations/{id}/guest-session-id")]
    [TestCase("POST", "group-reservations/{id}/submit")]
    [TestCase("DELETE", "guest/groups/{id}/sessions/{id}")]
    public async Task Owner_endpoints_reject_anonymous_requests_with_401(string method, string pathTemplate)
    {
        string path = pathTemplate.Replace("{id}", Unknown);
        using var client = new ApiClient(Config.TestSettings.Current.ApiBaseUrl);

        ApiResponse response = await client.SendAsync(new HttpMethod(method), path, method == "POST" ? new { reservationId = Unknown } : null, bearer: null);

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }

    [Test]
    public async Task A_bearer_token_that_is_not_a_valid_jwt_is_rejected_with_401()
    {
        using var client = new ApiClient(Config.TestSettings.Current.ApiBaseUrl);

        ApiResponse response = await client.GetAsync($"guest/groups/{Unknown}/sessions", bearer: "not-a-valid-token");

        Assert.That(response.Status, Is.EqualTo(401), response.Describe());
    }
}
