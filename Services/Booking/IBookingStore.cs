namespace AppointmentChatbot.Services;

public record Booking(
    string EventId,
    string CustomerName,
    string CustomerEmail,
    string ProviderEmail,
    DateTime Start,
    DateTime End);

public interface IBookingStore
{
    void Save(Booking booking);
    Booking? FindByCustomerEmail(string customerEmail);
    void Remove(string customerEmail);
}