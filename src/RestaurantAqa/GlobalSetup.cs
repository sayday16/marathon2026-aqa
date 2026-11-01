using RestaurantAqa.Data;

// Fixtures run in parallel; every test builds its own users, reservation and group, so tests are independent.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]

namespace RestaurantAqa;

[SetUpFixture]
public class GlobalSetup
{
    /// <summary>
    /// The backend runs on Lambda: the first requests after a pause start new instances and can hit the 29 second limit of the gateway (HTTP 504).
    /// A few parallel requests before the run warm up as many instances as the tests use at once.
    /// </summary>
    [OneTimeSetUp]
    public async Task WarmUpTheBackend()
    {
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => TestEnvironment.Api.DishesAsync()));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            TestContext.Progress.WriteLine($"Warm-up request failed, the tests go on: {exception.Message}");
        }
    }

    [OneTimeTearDown]
    public void ReleaseSharedClient() => TestEnvironment.Shutdown();
}
