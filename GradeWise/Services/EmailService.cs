using System;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace GradeWise.Services
{
    public static class EmailService
    {
        private const string SmtpHost = "smtp.gmail.com";
        private const int SmtpPort = 587;
        private const string SmtpUser = "duayjerick@gmail.com";
        private const string SmtpPass = "dxhrhkpynjqdonkv";

        public static void Send(string toEmail, string subject, string body)
        {
            if (string.IsNullOrEmpty(toEmail)) return;

            Task.Run(() =>
            {
                try
                {
                    using (var message = new MailMessage())
                    {
                        message.From = new MailAddress(SmtpUser, "GradeWise");
                        message.To.Add(new MailAddress(toEmail));
                        message.Subject = subject;
                        message.Body = body;
                        message.IsBodyHtml = true;

                        using (var client = new SmtpClient(SmtpHost, SmtpPort))
                        {
                            client.Credentials = new NetworkCredential(SmtpUser, SmtpPass);
                            client.EnableSsl = true;
                            client.Send(message);
                            Trace.WriteLine("SUCCESS: Email sent to " + toEmail);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("EMAIL ERROR: " + ex.Message);
                }
            });
        }

        public static void SendTempPassword(string toEmail, string fullName, string tempPassword)
        {
            string subject = "Welcome to GradeWise - Your Temporary Password";

            // GradeWise Zine Theme (Paper, Grid, Ink, Lime)
            string body = $@"
                <div style='background-color: #fdfcf9; background-image: url(""https://www.transparenttextures.com/patterns/graphy.png""); padding: 40px 20px; font-family: ""Space Grotesk"", Helvetica, Arial, sans-serif; color: #111111;'>
                    <div style='max-width: 600px; margin: 0 auto; background-color: #fdfcf9; border: 2px solid #111111; border-radius: 0; box-shadow: 6px 6px 0 #111111;'>
                        
                        <!-- Header -->
                        <div style='border-bottom: 2px solid #111111; padding: 20px; text-align: center; background-color: #fdfcf9;'>
                            <h1 style='margin: 0; font-size: 28px; font-weight: bold; letter-spacing: -0.5px;'>
                                <span style='background-color: #a9d56e; padding: 0 4px;'>Grade</span>Wise
                            </h1>
                        </div>

                        <!-- Body -->
                        <div style='padding: 30px;'>
                            <h2 style='margin-top: 0; font-size: 22px;'>Hello {fullName},</h2>
                            <p style='font-size: 16px; line-height: 1.5;'>Your GradeWise account has been created. Here is your temporary password:</p>
                            
                            <!-- Password Box -->
                            <div style='text-align: center; margin: 30px 0;'>
                                <div style='display: inline-block; background-color: #f2e36b; border: 2px solid #111111; padding: 15px 30px; font-size: 26px; font-weight: bold; letter-spacing: 3px; font-family: Consolas, monospace; box-shadow: 4px 4px 0 #111111;'>
                                    {tempPassword}
                                </div>
                            </div>

                            <p style='font-size: 16px; line-height: 1.5;'>When you log in, you will be immediately prompted to create your own new password.</p>
                            
                            <!-- Button -->
                            <div style='text-align: center; margin-top: 30px;'>
                                <a href='https://localhost:44388/Account/Login' style='display: inline-block; background-color: #111111; color: #fdfcf9; text-decoration: none; padding: 12px 24px; font-weight: bold; font-size: 16px; border: 2px solid #111111;'>Sign In Now</a>
                            </div>
                        </div>

                        <!-- Footer -->
                        <div style='border-top: 2px dashed #111111; padding: 15px; text-align: center; font-family: ""Courier New"", monospace; font-size: 12px; color: #555555;'>
                            &copy; 2026 GradeWise &middot; Keep your password safe.
                        </div>
                    </div>
                </div>";

            Send(toEmail, subject, body);
        }

        public static void SendGradeUpdate(string toEmail, string fullName, string courseCode, string courseName)
        {
            string subject = $"Grade Update: {courseCode}";

            // GradeWise Zine Theme (Paper, Grid, Ink, Lime)
            string body = $@"
                <div style='background-color: #fdfcf9; background-image: url(""https://www.transparenttextures.com/patterns/graphy.png""); padding: 40px 20px; font-family: ""Space Grotesk"", Helvetica, Arial, sans-serif; color: #111111;'>
                    <div style='max-width: 600px; margin: 0 auto; background-color: #fdfcf9; border: 2px solid #111111; border-radius: 0; box-shadow: 6px 6px 0 #111111;'>
                        
                        <!-- Header -->
                        <div style='border-bottom: 2px solid #111111; padding: 20px; text-align: center; background-color: #fdfcf9;'>
                            <h1 style='margin: 0; font-size: 28px; font-weight: bold; letter-spacing: -0.5px;'>
                                <span style='background-color: #a9d56e; padding: 0 4px;'>Grade</span>Wise
                            </h1>
                        </div>

                        <!-- Body -->
                        <div style='padding: 30px;'>
                            <h2 style='margin-top: 0; font-size: 22px;'>Hello {fullName},</h2>
                            <p style='font-size: 16px; line-height: 1.5;'>Your instructor has updated your grades for:</p>
                            
                            <!-- Course Badge -->
                            <div style='margin: 20px 0;'>
                                <span style='display: inline-block; background-color: #a9d56e; border: 2px solid #111111; padding: 6px 14px; font-weight: bold; font-size: 16px;'>
                                    {courseCode}: {courseName}
                                </span>
                            </div>

                            <p style='font-size: 16px; line-height: 1.5;'>Please log in to your GradeWise dashboard to view your updated scores, assessment breakdown, and progress history.</p>
                            
                            <!-- Button -->
                            <div style='text-align: center; margin-top: 30px;'>
                                <a href='https://localhost:44388/Student' style='display: inline-block; background-color: #111111; color: #fdfcf9; text-decoration: none; padding: 12px 24px; font-weight: bold; font-size: 16px; border: 2px solid #111111;'>View My Grades</a>
                            </div>
                        </div>

                        <!-- Footer -->
                        <div style='border-top: 2px dashed #111111; padding: 15px; text-align: center; font-family: ""Courier New"", monospace; font-size: 12px; color: #555555;'>
                            &copy; 2026 GradeWise &middot; Only you can see your scores.
                        </div>
                    </div>
                </div>";

            Send(toEmail, subject, body);
        }
    }
}