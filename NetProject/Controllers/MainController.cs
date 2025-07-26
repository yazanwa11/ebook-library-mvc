using Microsoft.AspNetCore.Mvc;
using NetProject.Models;
using Newtonsoft.Json;
using System.Data.SqlClient;
using NetProject.ViewModel;
using System.Net;
using System.Net.Mail;
using System.Data;

namespace NetProject.Controllers
{
    public class MainController : Controller
    {
        private readonly IConfiguration _configuration;
        public string connectionString = "";

        public MainController(IConfiguration configuration)
        {

            _configuration = configuration;
            connectionString = _configuration.GetConnectionString("myConnect");
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult MainView(User user)
        {
            List<string> feedbacks = new List<string>();
            List<int> ratings = new List<int>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("SELECT Text, Rating FROM Feedbacks", connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            feedbacks.Add(reader.GetString(0));
                            ratings.Add(reader.GetInt32(1));
                        }
                    }
                }
            }

            var viewModel = new MainViewModel
            {
                User = user,
                Feedbacks = feedbacks,
                Ratings = ratings
            };

            return View(viewModel);
        }


        public IActionResult BooksView()
        {
            return View("BooksView");
        }

        public IActionResult AddBooks()
        {
            return View("AddBooks");
        }



        public IActionResult ProfileView()
        {
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                return RedirectToAction("UsersView", "Users");
            }

            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Fetch purchased books for the user
                string purchasedBooksQuery = @"
        SELECT Books.title, Books.author, Books.publishYear, Books.PurchasePrice, Books.publisher, Books.genre, Books.CoverImage, 
               Books.PdfFilePath, Books.EpubFilePath, Books.MobiFilePath, Books.F2bFilePath
        FROM Books 
        JOIN UserBooks ON UserBooks.bookTitle = Books.title 
        WHERE UserBooks.username = @Username AND UserBooks.type = 'purchase'";

                using (SqlCommand command = new SqlCommand(purchasedBooksQuery, connection))
                {
                    command.Parameters.AddWithValue("@Username", loggedInUser.username);

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        loggedInUser.OwneddBooks = new List<Book>();
                        while (reader.Read())
                        {
                            var book = new Book
                            {
                                title = reader.GetString(reader.GetOrdinal("title")),
                                author = reader.GetString(reader.GetOrdinal("author")),
                                publishYear = reader.GetInt32(reader.GetOrdinal("publishYear")),
                                publisher = reader.GetString(reader.GetOrdinal("publisher")),
                                genre = reader.GetString(reader.GetOrdinal("genre")),
                                PurchasePrice = reader.GetInt32(reader.GetOrdinal("PurchasePrice")),
                                CoverImage = reader.GetString(reader.GetOrdinal("CoverImage")),
                                PdfFilePath = reader.IsDBNull(reader.GetOrdinal("PdfFilePath")) ? null : reader.GetString(reader.GetOrdinal("PdfFilePath")),
                                EpubFilePath = reader.IsDBNull(reader.GetOrdinal("EpubFilePath")) ? null : reader.GetString(reader.GetOrdinal("EpubFilePath")),
                                MobiFilePath = reader.IsDBNull(reader.GetOrdinal("MobiFilePath")) ? null : reader.GetString(reader.GetOrdinal("MobiFilePath")),
                                F2bFilePath = reader.IsDBNull(reader.GetOrdinal("F2bFilePath")) ? null : reader.GetString(reader.GetOrdinal("F2bFilePath"))
                            };

                            loggedInUser.OwneddBooks.Add(book);
                        }
                    }
                }

                // Fetch loaned books for the user
                string loanedBooksQuery = @"
        SELECT Books.title, Books.author, Books.publishYear, Books.LoanPrice, Books.publisher, Books.genre, Books.CoverImage, 
               Books.PdfFilePath, Books.EpubFilePath, Books.MobiFilePath, Books.F2bFilePath,
               Loans.loanDate, Loans.dueDate 
        FROM Loans 
        JOIN Books ON Loans.bookTitle = Books.title 
        WHERE Loans.username = @Username";

                using (SqlCommand command = new SqlCommand(loanedBooksQuery, connection))
                {
                    command.Parameters.AddWithValue("@Username", loggedInUser.username);

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        loggedInUser.LoanedBooks = new List<Book>();
                        List<(string Title, DateTime DueDate)> expiredLoans = new List<(string, DateTime)>();
                        List<(string Title, DateTime DueDate)> nearExpiryLoans = new List<(string, DateTime)>(); // Store near expiry books

                        while (reader.Read())
                        {
                            var dueDate = reader.GetDateTime(reader.GetOrdinal("dueDate"));
                            if (dueDate < DateTime.Now) // Check if the loan is expired
                            {
                                expiredLoans.Add((
                                    reader.GetString(reader.GetOrdinal("title")),
                                    dueDate
                                ));
                            }
                            else if (dueDate <= DateTime.Now.AddDays(5)) // Check if the loan is about to expire in 5 days
                            {
                                nearExpiryLoans.Add((
                                    reader.GetString(reader.GetOrdinal("title")),
                                    dueDate
                                ));
                            }
                            else
                            {
                                var loanedBook = new Book
                                {
                                    title = reader.GetString(reader.GetOrdinal("title")),
                                    author = reader.GetString(reader.GetOrdinal("author")),
                                    publishYear = reader.GetInt32(reader.GetOrdinal("publishYear")),
                                    publisher = reader.GetString(reader.GetOrdinal("publisher")),
                                    genre = reader.GetString(reader.GetOrdinal("genre")),
                                    LoanPrice = reader.GetInt32(reader.GetOrdinal("LoanPrice")),
                                    LoanDate = reader.GetDateTime(reader.GetOrdinal("loanDate")),
                                    CoverImage = reader.GetString(reader.GetOrdinal("CoverImage")),
                                    DueDate = dueDate,
                                    PdfFilePath = reader.IsDBNull(reader.GetOrdinal("PdfFilePath")) ? null : reader.GetString(reader.GetOrdinal("PdfFilePath")),
                                    EpubFilePath = reader.IsDBNull(reader.GetOrdinal("EpubFilePath")) ? null : reader.GetString(reader.GetOrdinal("EpubFilePath")),
                                    MobiFilePath = reader.IsDBNull(reader.GetOrdinal("MobiFilePath")) ? null : reader.GetString(reader.GetOrdinal("MobiFilePath")),
                                    F2bFilePath = reader.IsDBNull(reader.GetOrdinal("F2bFilePath")) ? null : reader.GetString(reader.GetOrdinal("F2bFilePath"))
                                };

                                loggedInUser.LoanedBooks.Add(loanedBook);
                            }
                        }

                        reader.Close(); // Close the reader for loaned books

                        // Remove expired loans after the reader is closed
                        foreach (var loan in expiredLoans)
                        {
                            string removeExpiredLoanQuery = @"
                    DELETE FROM Loans 
                    WHERE username = @Username AND bookTitle = @BookTitle AND dueDate = @DueDate";
                            using (SqlCommand deleteCommand = new SqlCommand(removeExpiredLoanQuery, connection))
                            {
                                deleteCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                                deleteCommand.Parameters.AddWithValue("@BookTitle", loan.Title);
                                deleteCommand.Parameters.AddWithValue("@DueDate", loan.DueDate);
                                deleteCommand.ExecuteNonQuery();
                            }
                        }

                        // Send email notifications for books with 5 days left
                        foreach (var loan in nearExpiryLoans)
                        {
                            string emailSubject = "Reminder: Your Loan is About to Expire";
                            string emailBody = $"Your loan for the book '{loan.Title}' will expire on {loan.DueDate.ToShortDateString()}.";
                            SendMail(loggedInUser.email, emailSubject, emailBody);
                        }
                    }
                }
            }

            return View(loggedInUser);
        }


        public void SendMail(string toEmail, string subject, string body)
        {
            try
            {
                using (MailMessage mail = new MailMessage())
                {
                    // Sender's email address (your Gmail account)
                    mail.From = new MailAddress("yazanwatt@gmail.com");
                    mail.To.Add(toEmail);
                    mail.Subject = subject;
                    mail.Body = body;
                    mail.IsBodyHtml = true;

                    using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) // Gmail SMTP server and port
                    {
                        // Use your Gmail address and app password (if 2FA is enabled)
                        smtp.Credentials = new NetworkCredential("yazanwatt@gmail.com", "xnad bpvs fiqn yano");
                        smtp.EnableSsl = true; // Enable SSL for a secure connection

                        Console.WriteLine("Attempting to send email...");
                        smtp.Send(mail); // Send the email
                        Console.WriteLine("Email sent successfully.");
                    }
                }
            }
            catch (SmtpException smtpEx)
            {
                Console.WriteLine($"SMTP Error: {smtpEx.StatusCode} - {smtpEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send email. Error: {ex.Message}");
            }
        }









        public IActionResult LoggedinView()
        {
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                // User is not logged in, redirect to login
                return RedirectToAction("LoginView", "Users");
            }

            // Deserialize the user object
            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            // Fetch books on sale
            List<Book> discountedBooks = new List<Book>();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT * FROM Books WHERE DiscountPercentage > 0";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            discountedBooks.Add(new Book
                            {
                                title = reader.GetString(0),
                                publishYear = reader.GetInt32(1),
                                author = reader.GetString(2),
                                quantity = reader.GetInt32(3),
                                PurchasePrice = reader.GetInt32(4),
                                LoanPrice = reader.GetInt32(5),
                                publisher = reader.GetString(6),
                                genre = reader.GetString(7),
                                DiscountPercentage = reader.GetInt32(8),
                                CoverImage = reader.GetString(9)
                            });
                        }
                    }
                }
                connection.Close();
            }

            // Pass discounted books to the view
            var model = new DiscountedBooksViewModel
            {
                LoggedInUser = loggedInUser,
                DiscountedBooks = discountedBooks
            };

            return View("LoggedinView", model);
        }



        public IActionResult DropPurchasedBook(string bookTitle)
        {
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                return RedirectToAction("UsersView", "Users");
            }

            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string deleteQuery = "DELETE FROM UserBooks WHERE username = @Username AND bookTitle = @Title AND type = 'purchase'";
                using (SqlCommand deleteCommand = new SqlCommand(deleteQuery, connection))
                {
                    deleteCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                    deleteCommand.Parameters.AddWithValue("@Title", bookTitle);
                    deleteCommand.ExecuteNonQuery();
                }

                // Update session data
                loggedInUser.OwneddBooks.RemoveAll(b => b.title == bookTitle);
                HttpContext.Session.SetString("User", JsonConvert.SerializeObject(loggedInUser));
            }

            return RedirectToAction("ProfileView");
        }


        [HttpPost]
        public IActionResult SubmitFeedback(string feedback, int rating)
        {
            if (string.IsNullOrWhiteSpace(feedback) || rating == 0)
            {
                TempData["ErrorMessage"] = "Please provide both feedback and a rating!";
                return RedirectToAction("LoggedInView");
            }

            // SQL query to insert feedback
            string query = "INSERT INTO Feedbacks (Text, Rating, CreatedAt) VALUES (@p0, @p1, @p2)";

            // Open SQL connection and execute the query
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                SqlCommand command = new SqlCommand(query, connection);

                // Add parameters to prevent SQL injection
                command.Parameters.Add(new SqlParameter("@p0", SqlDbType.NVarChar)).Value = feedback;
                command.Parameters.Add(new SqlParameter("@p1", SqlDbType.Int)).Value = rating;
                command.Parameters.Add(new SqlParameter("@p2", SqlDbType.DateTime)).Value = DateTime.Now;

                connection.Open(); // Open the connection
                command.ExecuteNonQuery(); // Execute the insert query
            }

            TempData["Message"] = "Thank you for your feedback!";
            return RedirectToAction("LoggedInView");
        }




        public IActionResult OpenBook(string bookTitle)
        {
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                return RedirectToAction("UsersView", "Users");
            }

            // Fetch the book from the database
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string bookQuery = "SELECT * FROM Books WHERE title = @Title";
                using (SqlCommand bookCommand = new SqlCommand(bookQuery, connection))
                {
                    bookCommand.Parameters.AddWithValue("@Title", bookTitle);
                    SqlDataReader reader = bookCommand.ExecuteReader();

                    if (reader.Read())
                    {
                        string filePath = reader.GetString(reader.GetOrdinal("FilePath")); // Assuming FilePath contains the URL
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            // Redirect to the external URL (the book file)
                            return Redirect(filePath);
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "The book file is not available.";
                        }
                    }
                }
            }

            return RedirectToAction("ProfileView"); // Or wherever you want to redirect on failure
        }



        public IActionResult SubmitRating(string BookTitle, int Rating)
        {
            // Get the logged-in user's username from the session
            var userSession = HttpContext.Session.GetString("User");
            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            // Debug: Check if the user session is retrieved correctly
            Console.WriteLine("User Session: " + (loggedInUser != null ? loggedInUser.username : "No user logged in"));

            // If user is not logged in, redirect them to login
            if (loggedInUser == null)
            {
                Console.WriteLine("User not logged in, redirecting to login...");
                return RedirectToAction("Login", "Account");
            }

            // Debug: Log the incoming rating data
            Console.WriteLine($"Received Rating Submission: BookTitle = {BookTitle}, Rating = {Rating}, CustomerUsername = {loggedInUser.username}");

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    Console.WriteLine("Database connection established.");

                    // Check if the user has already rated the book
                    string checkRatingQuery = @"
                SELECT COUNT(*) 
                FROM Ratings 
                WHERE CustomerUsername = @CustomerUsername AND BookTitle = @BookTitle";

                    using (SqlCommand command = new SqlCommand(checkRatingQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CustomerUsername", loggedInUser.username);
                        command.Parameters.AddWithValue("@BookTitle", BookTitle);

                        int ratingCount = (int)command.ExecuteScalar();

                        // If the user has already rated the book, show a message and don't insert
                        if (ratingCount > 0)
                        {
                            TempData["Message"] = "You have already rated this book.";
                            Console.WriteLine("User has already rated this book.");
                            return RedirectToAction("BooksView", "Books");
                        }
                    }

                    // Insert the rating into the database
                    string insertRatingQuery = @"
                INSERT INTO Ratings (CustomerUsername, BookTitle, Rating)
                VALUES (@CustomerUsername, @BookTitle, @Rating)";

                    using (SqlCommand command = new SqlCommand(insertRatingQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CustomerUsername", loggedInUser.username);
                        command.Parameters.AddWithValue("@BookTitle", BookTitle);
                        command.Parameters.AddWithValue("@Rating", Rating);

                        // Debug: Log SQL parameters
                        Console.WriteLine($"SQL Parameters: @CustomerUsername = {loggedInUser.username}, @BookTitle = {BookTitle}, @Rating = {Rating}");

                        command.ExecuteNonQuery();
                        Console.WriteLine("Rating inserted into database successfully.");
                    }
                }

                // Show success message
                TempData["Message"] = "Your rating has been submitted successfully!";
                Console.WriteLine("Redirecting to BooksView after successful submission.");

                return RedirectToAction("BooksView", "Books");  // Redirect back to books view or any other page you prefer
            }
            catch (Exception ex)
            {
                // Debug: Catch and log any exceptions
                Console.WriteLine("Error occurred while submitting rating: " + ex.Message);
                TempData["Message"] = "There was an error submitting your rating. Please try again later.";
                return RedirectToAction("BooksView", "Books");
            }
        }



        public IActionResult DownloadBook(string title, string format)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(format))
            {
                return BadRequest("Invalid title or format.");
            }

            string filePath = null;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT PdfFilePath, MobiFilePath, EpubFilePath, F2bFilePath FROM Books WHERE title = @Title";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Title", title);

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            switch (format.ToLower())
                            {
                                case "pdf":
                                    filePath = reader["PdfFilePath"].ToString();
                                    break;
                                case "mobi":
                                    filePath = reader["MobiFilePath"].ToString();
                                    break;
                                case "epub":
                                    filePath = reader["EpubFilePath"].ToString();
                                    break;
                                case "fb2":
                                    filePath = reader["F2bFilePath"].ToString();
                                    break;
                                default:
                                    return NotFound("Invalid format.");
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                // Ensure we have the correct path format (removes leading slashes from db entry)
                string serverFilePath = Path.Combine(Directory.GetCurrentDirectory(), filePath.TrimStart('/'));

                // Debugging output: log the final file path for troubleshooting
                Console.WriteLine($"Attempting to access file: {serverFilePath}");

                // Check if the file exists
                if (System.IO.File.Exists(serverFilePath))
                {
                    var fileBytes = System.IO.File.ReadAllBytes(serverFilePath);
                    var fileName = Path.GetFileName(serverFilePath);
                    return File(fileBytes, "application/octet-stream", fileName);
                }
                else
                {
                    return NotFound($"File not found on the server at {serverFilePath}");
                }
            }
            else
            {
                return NotFound("File path not found in the database.");
            }
        }








        // OpenBook action to open the book in the selected format
        public IActionResult OpenBook(string bookTitle, string format)
        {
            // Logic to fetch the file path for the selected book and format
            string filePath = GetBookFilePath(bookTitle, format);

            if (filePath != null)
            {
                // For certain formats like Epub, you may need special handling or a viewer
                if (format.ToLower() == "epub")
                {
                    // Render an Epub viewer or display logic for Epub files
                    return File(filePath, "application/epub+zip");
                }
                else
                {
                    return File(filePath, "application/octet-stream");
                }
            }

            return NotFound();
        }

        // Helper method to get the file path for the selected book and format
        private string GetBookFilePath(string bookTitle, string format)
        {
            // Assuming there's a Books table with file information. Adjust as necessary.
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT FilePath FROM BookFiles WHERE BookTitle = @bookTitle AND FileFormat = @format";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@bookTitle", bookTitle);
                    command.Parameters.AddWithValue("@format", format);

                    var result = command.ExecuteScalar();
                    return result?.ToString();
                }
            }
        }














    }
}
