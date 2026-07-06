using System.Collections.Concurrent;

namespace AppointmentChatbot.Services;

/// <summary>
/// In-memory booking store for Step 1.
/// Swap this for SqlBookingStore (Dapper) in a later step
/// without touching any agent or controller code.
/// </summary>
public class InMemoryBookingStore : IBookingStore
{
    private readonly ConcurrentDictionary<string, Booking> _bookings = new();

    public void Save(Booking booking) =>
        _bookings[booking.CustomerEmail.ToLowerInvariant()] = booking;

    public Booking? FindByCustomerEmail(string customerEmail) =>
        _bookings.TryGetValue(customerEmail.ToLowerInvariant(), out var b) ? b : null;

    public void Remove(string customerEmail) =>
        _bookings.TryRemove(customerEmail.ToLowerInvariant(), out _);
}