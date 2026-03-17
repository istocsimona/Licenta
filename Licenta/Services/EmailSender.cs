using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using MimeKit;

namespace Licenta.Services
{
    public class EmailSender : IEmailSender
    {
        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var emailMessage = new MimeMessage();

            // Put your actual Gmail address here
            emailMessage.From.Add(new MailboxAddress("My App", "istocsimona23@gmail.com"));
            emailMessage.To.Add(new MailboxAddress("", email));
            emailMessage.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlMessage };
            emailMessage.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);

            // Put your actual Gmail address and your 16-character App Password here
            await client.AuthenticateAsync("istocsimona23@gmail.com", "liev hfvo wkzg fenz");

            await client.SendAsync(emailMessage);
            await client.DisconnectAsync(true);
        }
    }
}