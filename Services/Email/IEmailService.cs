namespace AppointmentChatbot.Services;

public interface IEmailService
{
    Task SendAppointmentConfirmationAsync(string customerEmail, string customerName, 
        string providerEmail, DateTime start, DateTime end);
    
    Task SendRescheduleNoticeAsync(string customerEmail, string customerName, 
        string providerEmail, DateTime oldStart, DateTime newStart, DateTime newEnd);
    
    Task SendCancellationNoticeAsync(string customerEmail, string customerName, 
        string providerEmail, DateTime start);
}