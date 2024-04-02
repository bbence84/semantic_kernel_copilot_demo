using System.ComponentModel;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using System.Text.Json;
using System.Linq;
using MailKit.Net.Smtp;
using MailKit.Security;
using MailKit;
using MimeKit;

/* Custom Actions Plugin: can be used to add actions to the assistant
    Exposed functions:
    - AddEventToCalendar: Add an event to the calendar.
    - SendEMail: Send an email to a recipient with a specified subject and body.
*/

namespace SemanticKernelConsoleCopilotDemo
{
    internal sealed class CustomActionsPlugin
    {   

        [KernelFunction, Description("Add an event to the calendar. ")]
        public static string AddEventToCalendar(
            [Description("Title of the event")] string eventTitle,
            [Description("Description of the event")] string eventDescription, 
            [Description("Location of the event")]string eventLocation, 
            [Description("Start date and time of the event")]string eventStartDate, 
            [Description("End date and time of the event")]string eventEndDate) {
            
            // Add the event to the calendar: TODO, implement the logic here
                return "Event added to the calendar!";
        }

        [KernelFunction, Description("Send an email to a recipient with a specified subject and body. ")]
        public static string SendEMail(
            [Description("The email address of the recipient. Just the email address.")] string to, 
            [Description("Email subject")] string subject, 
            [Description("Email body")] string body) {

            var message = new MimeMessage ();
            message.From.Add (new MailboxAddress (ConfigurationSettings.GmailEmailSender, ConfigurationSettings.GmailEmailUsername));
            message.To.Add (new MailboxAddress (to, to));
            message.Subject = subject;

            message.Body = new TextPart ("plain") { Text = body };

            using (var client = new SmtpClient ()) {
                client.Connect("smtp.gmail.com", 465, SecureSocketOptions.SslOnConnect);
                client.Authenticate (ConfigurationSettings.GmailEmailUsername, ConfigurationSettings.GmailEmailAppPassword);
                //client.Send (message);
                client.Disconnect (true);
                return "Email sent successfully!";
            }
        }

        public CustomActionsPlugin()
        {
          
        }

    }
}