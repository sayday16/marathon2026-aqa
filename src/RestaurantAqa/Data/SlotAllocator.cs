namespace RestaurantAqa.Data;

/// <summary>
/// Picks reservation slots that do not collide with other tests running in parallel (or with leftovers of earlier
/// runs): a random day 3-400 days ahead and one of six two-hour slots. A collision is retried by the caller.
/// The wide range keeps the table free even if the environment still holds reservations left by earlier runs.
/// </summary>
public static class SlotAllocator
{
    public const int GuestCount = 6;

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(90);

    private static readonly int[] StartHours = [10, 12, 14, 16, 18, 20];

    public static DateTime RandomSlotStart()
    {
        DateTime day = DateTime.UtcNow.Date.AddDays(Random.Shared.Next(3, 401));
        return DateTime.SpecifyKind(day.AddHours(StartHours[Random.Shared.Next(StartHours.Length)]), DateTimeKind.Utc);
    }
}
