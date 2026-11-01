using RestaurantAqa.Data;

// Fixtures run in parallel; every test builds its own users, reservation and group, so tests are independent.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]

namespace RestaurantAqa;

[SetUpFixture]
public class GlobalSetup
{
    [OneTimeTearDown]
    public void ReleaseSharedClient() => TestEnvironment.Shutdown();
}
