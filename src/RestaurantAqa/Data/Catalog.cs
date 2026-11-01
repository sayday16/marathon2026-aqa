using RestaurantAqa.Api;
using RestaurantAqa.Config;

namespace RestaurantAqa.Data;

/// <summary>Reference data of the tested environment: a table to book and dishes to order.</summary>
public sealed record CatalogData(string TableId, DishDto DishA, DishDto DishB, DishDto DishC);

public static class CatalogResolver
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static CatalogData? _cached;

    /// <summary>Resolved once per run. Needs three available dishes with different prices and a table for 6+ guests.</summary>
    public static async Task<CatalogData> ResolveAsync(RestaurantApi api, string token)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await Gate.WaitAsync();
        try
        {
            return _cached ??= await DiscoverAsync(api, token);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<CatalogData> DiscoverAsync(RestaurantApi api, string token)
    {
        List<DishDto> dishes = (await api.DishesAsync()).EnsureSuccess().Json<List<DishDto>>()
            .Where(d => string.Equals(d.State, "Available", StringComparison.OrdinalIgnoreCase) && d.Price > 0)
            .GroupBy(d => d.Price)
            .Select(g => g.First())
            .Take(3)
            .ToList();

        if (dishes.Count < 3)
        {
            throw new InvalidOperationException(
                "The tested environment needs at least three available dishes with different prices (found " + dishes.Count + ").");
        }

        string? tableId = TestSettings.Current.TableId;
        if (tableId is null)
        {
            DateTime probe = SlotAllocator.RandomSlotStart();
            List<TableDto> tables = (await api.AvailableTablesAsync(token, probe, SlotAllocator.GuestCount)).EnsureSuccess().Json<List<TableDto>>();
            tableId = tables.OrderByDescending(t => t.Capacity).FirstOrDefault()?.Id
                      ?? throw new InvalidOperationException($"No table for {SlotAllocator.GuestCount} guests is available in the tested environment.");
        }

        return new CatalogData(tableId, dishes[0], dishes[1], dishes[2]);
    }
}
