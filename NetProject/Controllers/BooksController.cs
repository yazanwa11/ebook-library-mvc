using Microsoft.AspNetCore.Mvc;
using NetProject.Models;
using NetProject.ViewModel;
using System.Data;
using System.Data.SqlClient;
using Newtonsoft.Json;
using System.Net;
using System.Net.Mail;

namespace NetProject.Controllers
{
    public class BooksController : Controller
    {
        private readonly IConfiguration _configuration;
        public string connectionString = "";

        public BooksController(IConfiguration configuration)
        {
            _configuration = configuration;
            connectionString = _configuration.GetConnectionString("myConnect");
        }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult ManageBooks()
        {
            var userSession = HttpContext.Session.GetString("User");
            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);



            AllBooksModel allBooks = new AllBooksModel();
            allBooks.book = new Book();
            allBooks.allBooks = new List<Book>();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string SQLquery = "select * from Books";
                using (SqlCommand command = new SqlCommand(SQLquery, connection))
                {
                    SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Book book = new Book();
                        book.title = reader.GetString(0);
                        book.publishYear = reader.GetInt32(1);
                        book.author = reader.GetString(2);
                        book.PurchasePrice = reader.GetInt32(4);
                        book.LoanPrice = reader.GetInt32(5);
                        book.quantity = reader.GetInt32(3);
                        book.publisher = reader.GetString(6);
                        book.genre = reader.GetString(7);
                        book.DiscountPercentage = reader.GetInt32(8);

                        allBooks.allBooks.Add(book);
                    }
                    reader.Close();
                }
                connection.Close();
            }
            return View("ManageBooks", allBooks);
        }


        //FOR THE ADMIN !!!!

        public IActionResult BooksView()
        {
            // Get the logged-in user
            var userSession = HttpContext.Session.GetString("User");
            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            AllBooksModel allBooks = new AllBooksModel
            {
                book = new Book(),
                allBooks = new List<Book>()
            };

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Query to delete expired loans from the Loans table
                string deleteExpiredLoansQuery = @"
            DELETE FROM Loans
            WHERE DueDate < @CurrentDate";

                using (SqlCommand command = new SqlCommand(deleteExpiredLoansQuery, connection))
                {
                    command.Parameters.AddWithValue("@CurrentDate", DateTime.Now);
                    command.ExecuteNonQuery();
                }

                // Query to update the quantity for books based on the number of loans
                string updateQuantityQuery = @"
            UPDATE Books
            SET quantity = 3 - ISNULL(( 
                SELECT COUNT(*) 
                FROM Loans 
                WHERE Loans.BookTitle = Books.title AND Loans.DueDate >= @CurrentDate
            ), 0)";

                using (SqlCommand command = new SqlCommand(updateQuantityQuery, connection))
                {
                    command.Parameters.AddWithValue("@CurrentDate", DateTime.Now);
                    command.ExecuteNonQuery();
                }

                // Query to update discount percentage to 0 for expired discounts
                string updateDiscountQuery = @"
            UPDATE Books
            SET DiscountPercentage = 0
            WHERE DiscountEndDate IS NOT NULL AND DiscountEndDate <= @CurrentDate";

                using (SqlCommand command = new SqlCommand(updateDiscountQuery, connection))
                {
                    command.Parameters.AddWithValue("@CurrentDate", DateTime.Now);
                    command.ExecuteNonQuery();
                }

                // Notify customers for books now available
                NotifyWaitingList(connection);


                string SQLquery = @"
            SELECT 
                b.*,
                ISNULL(( 
                    SELECT AVG(r.Rating) 
                    FROM Ratings r 
                    WHERE r.BookTitle = b.title
                ), 0) AS AverageRating,
                ISNULL(( 
                    SELECT COUNT(r.Rating) 
                    FROM Ratings r 
                    WHERE r.BookTitle = b.title
                ), 0) AS RatingCount
            FROM Books b
            WHERE b.title NOT IN (
                SELECT BookTitle 
                FROM WaitingList 
                WHERE CustomerUsername = @CustomerUsername
            )";

                using (SqlCommand command = new SqlCommand(SQLquery, connection))
                {
                    command.Parameters.AddWithValue("@CustomerUsername", loggedInUser.username);

                    SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Book book = new Book
                        {
                            title = reader.GetString(0),
                            publishYear = reader.GetInt32(1),
                            author = reader.GetString(2),
                            PurchasePrice = reader.GetInt32(4),
                            LoanPrice = reader.GetInt32(5),
                            quantity = reader.GetInt32(3),
                            publisher = reader.GetString(6),
                            genre = reader.GetString(7),
                            DiscountPercentage = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                            CoverImage = reader.GetString(9),
                            isBuyOnly = reader.GetBoolean(15),
                            AverageRating = reader.IsDBNull(16) ? 0 : reader.GetInt32(16),
                            RatingCount = reader.IsDBNull(17) ? 0 : reader.GetInt32(17),

                        };

                        allBooks.allBooks.Add(book);
                    }
                    reader.Close();
                }

                connection.Close();
            }

            return View("BooksView", allBooks);
        }

        private void NotifyWaitingList(SqlConnection connection)
        {
            // Query to get books with quantity > 0
            string availableBooksQuery = @"
        SELECT title, quantity
        FROM Books
        WHERE quantity > 0";

            using (SqlCommand command = new SqlCommand(availableBooksQuery, connection))
            {
                SqlDataReader reader = command.ExecuteReader();
                List<(string Title, int Quantity)> availableBooks = new List<(string Title, int Quantity)>();

                while (reader.Read())
                {
                    availableBooks.Add((reader.GetString(0), reader.GetInt32(1))); // Title, Quantity
                }
                reader.Close();

                foreach (var (title, quantity) in availableBooks)
                {
                    // Query to get the first 'quantity' customers from the waiting list
                    string waitingListQuery = @"
                SELECT TOP(@Quantity) CustomerUsername, CustomerEmail
                FROM WaitingList
                WHERE BookTitle = @BookTitle"; // Assuming WaitingList has an 'Id' column to maintain order

                    using (SqlCommand waitingListCommand = new SqlCommand(waitingListQuery, connection))
                    {
                        waitingListCommand.Parameters.AddWithValue("@Quantity", quantity);
                        waitingListCommand.Parameters.AddWithValue("@BookTitle", title);

                        SqlDataReader waitingListReader = waitingListCommand.ExecuteReader();
                        List<string> notifiedCustomers = new List<string>();

                        while (waitingListReader.Read())
                        {
                            string customerEmail = waitingListReader.GetString(1);
                            string subject = $"The Book '{title}' Is Now Available!";
                            string body = $"Dear Customer, <br><br>The book '{title}' is now available for borrowing. <br><br>Regards, <br>Your Library Team";

                            // Send notification email
                            SendMail(customerEmail, subject, body);

                            // Add username to remove them later
                            notifiedCustomers.Add(waitingListReader.GetString(0)); // CustomerUsername
                        }
                        waitingListReader.Close();

                        // Remove notified customers from the waiting list
                        foreach (var username in notifiedCustomers)
                        {
                            string removeCustomerQuery = @"
                        DELETE FROM WaitingList
                        WHERE CustomerUsername = @CustomerUsername AND BookTitle = @BookTitle";

                            using (SqlCommand removeCommand = new SqlCommand(removeCustomerQuery, connection))
                            {
                                removeCommand.Parameters.AddWithValue("@CustomerUsername", username);
                                removeCommand.Parameters.AddWithValue("@BookTitle", title);
                                removeCommand.ExecuteNonQuery();
                            }
                        }
                    }
                }
            }
        }


        public IActionResult MainBooks()
        {
            AllBooksModel allBooks = new AllBooksModel();
            allBooks.book = new Book();
            allBooks.allBooks = new List<Book>();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string SQLquery = "select * from Books";
                using (SqlCommand command = new SqlCommand(SQLquery, connection))
                {
                    SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Book book = new Book();
                        book.title = reader.GetString(0);
                        book.publishYear = reader.GetInt32(1);
                        book.author = reader.GetString(2);
                        book.PurchasePrice = reader.GetInt32(4);
                        book.LoanPrice = reader.GetInt32(5);
                        book.quantity = reader.GetInt32(3);
                        book.publisher = reader.GetString(6);
                        book.genre = reader.GetString(7);
                        book.DiscountPercentage = reader.GetInt32(8);
                        book.CoverImage = reader.GetString(9);

                        allBooks.allBooks.Add(book);
                    }
                    reader.Close();
                }
                connection.Close();
            }
            return View("MainBooks", allBooks);
        }


        //FOR CUSTOMERS
        [HttpPost]
        public IActionResult AddBooks(Book book, IFormFile CoverImage, IFormFile PdfFile, IFormFile EpubFile, IFormFile MobiFile, IFormFile Fb2File)
        {
            if (book == null)
            {
                ModelState.AddModelError("", "Invalid book details.");
                return View("~/Views/Main/AddBooks.cshtml", book);
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Check if book already exists
                string checkQuery = "SELECT COUNT(*) FROM Books WHERE title = @title";
                using (SqlCommand checkCmd = new SqlCommand(checkQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@title", book.title);
                    if ((int)checkCmd.ExecuteScalar() > 0)
                    {
                        ModelState.AddModelError("", "A book with this title already exists.");
                        return View("~/Views/Main/AddBooks.cshtml", book);
                    }
                }

                // Save cover image if uploaded
                string coverImagePath = null;
                if (CoverImage != null && CoverImage.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(CoverImage.FileName);
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        CoverImage.CopyTo(stream);
                    }

                    coverImagePath = "/images/" + fileName;
                }

                // Save each book file if uploaded
                string pdfFilePath = null;
                string epubFilePath = null;
                string mobiFilePath = null;
                string f2bFilePath = null;

                // Helper method to save files
                Func<IFormFile, string, string> saveFile = (file, folder) =>
                {
                    if (file != null && file.Length > 0)
                    {
                        var fileFolder = Path.Combine(Directory.GetCurrentDirectory(), folder);
                        if (!Directory.Exists(fileFolder))
                        {
                            Directory.CreateDirectory(fileFolder);
                        }

                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                        var fileSavePath = Path.Combine(fileFolder, fileName);

                        using (var stream = new FileStream(fileSavePath, FileMode.Create))
                        {
                            file.CopyTo(stream);
                        }

                        return "/" + folder + "/" + fileName;
                    }
                    return null;
                };

                pdfFilePath = saveFile(PdfFile, "wwwroot/pdf");
                epubFilePath = saveFile(EpubFile, "wwwroot/epub");
                mobiFilePath = saveFile(MobiFile, "wwwroot/mobi");
                f2bFilePath = saveFile(Fb2File, "wwwroot/fb2");

                // Insert into Books table
                string insertBookQuery = @"
        INSERT INTO Books (title, publishyear, author, quantity, PurchasePrice, LoanPrice, publisher, genre, PdfFilePath, EpubFilePath, MobiFilePath, F2bFilePath, CoverImage, DiscountPercentage,isBuyOnly)
        VALUES (@title, @publishYear, @author, @quantity, @purchasePrice, @loanPrice, @publisher, @genre, @pdfFilePath, @epubFilePath, @mobiFilePath, @f2bFilePath, @coverImage, @discountPercentage,@isBuyOnly)";

                using (SqlCommand bookCmd = new SqlCommand(insertBookQuery, connection))
                {
                    bookCmd.Parameters.AddWithValue("@title", book.title);
                    bookCmd.Parameters.AddWithValue("@publishYear", book.publishYear);
                    bookCmd.Parameters.AddWithValue("@author", book.author);
                    bookCmd.Parameters.AddWithValue("@quantity", 3); // Default quantity
                    bookCmd.Parameters.AddWithValue("@purchasePrice", book.PurchasePrice);
                    bookCmd.Parameters.AddWithValue("@loanPrice", book.LoanPrice);
                    bookCmd.Parameters.AddWithValue("@publisher", book.publisher);
                    bookCmd.Parameters.AddWithValue("@genre", book.genre);
                    bookCmd.Parameters.AddWithValue("@pdfFilePath", pdfFilePath ?? (object)DBNull.Value);
                    bookCmd.Parameters.AddWithValue("@epubFilePath", epubFilePath ?? (object)DBNull.Value);
                    bookCmd.Parameters.AddWithValue("@mobiFilePath", mobiFilePath ?? (object)DBNull.Value);
                    bookCmd.Parameters.AddWithValue("@f2bFilePath", f2bFilePath ?? (object)DBNull.Value);
                    bookCmd.Parameters.AddWithValue("@coverImage", coverImagePath ?? (object)DBNull.Value);
                    bookCmd.Parameters.AddWithValue("@discountPercentage", 0);
                    bookCmd.Parameters.AddWithValue("@isBuyOnly", book.isBuyOnly);


                    bookCmd.ExecuteNonQuery();
                }

                TempData["SuccessMessage"] = "Book added successfully!";
                return RedirectToAction("ManageBooks", "Books");
            }
        }



        // DELETE BOOK

        public IActionResult DeleteBook(string bookTitle)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Delete related ratings first
                string deleteRatingsQuery = "DELETE FROM Ratings WHERE BookTitle = @Title";
                using (SqlCommand deleteRatingsCommand = new SqlCommand(deleteRatingsQuery, connection))
                {
                    deleteRatingsCommand.Parameters.AddWithValue("@Title", bookTitle);
                    deleteRatingsCommand.ExecuteNonQuery();
                }

                // Check if the book exists before attempting to delete
                string bookQuery = "SELECT * FROM Books WHERE title = @Title";
                using (SqlCommand bookCommand = new SqlCommand(bookQuery, connection))
                {
                    bookCommand.Parameters.AddWithValue("@Title", bookTitle);
                    SqlDataReader reader = bookCommand.ExecuteReader();

                    if (reader.Read())
                    {
                        reader.Close();

                        // Delete the book from the database
                        string deleteBookQuery = "DELETE FROM Books WHERE title = @Title";
                        using (SqlCommand deleteBookCommand = new SqlCommand(deleteBookQuery, connection))
                        {
                            deleteBookCommand.Parameters.AddWithValue("@Title", bookTitle);
                            deleteBookCommand.ExecuteNonQuery();
                        }
                    }
                }
            }

            return RedirectToAction("ManageBooks");
        }






        public IActionResult PurchaseBook(string bookTitle)
        {
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                Console.WriteLine("User session is empty, redirecting to UsersView.");
                return RedirectToAction("UsersView", "Users");
            }

            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);
            Console.WriteLine("Logged in user: " + loggedInUser.username);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                Console.WriteLine("Database connection opened.");

                // Check if the book has been purchased or loaned
                string checkQuery = @"
            SELECT 1 
            FROM UserBooks 
            WHERE username = @Username AND bookTitle = @Title AND type = 'purchase'
            UNION
            SELECT 1 
            FROM Loans 
            WHERE username = @Username AND bookTitle = @Title";

                using (SqlCommand checkCommand = new SqlCommand(checkQuery, connection))
                {
                    checkCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                    checkCommand.Parameters.AddWithValue("@Title", bookTitle);

                    var result = checkCommand.ExecuteScalar();
                    Console.WriteLine("Check query result: " + result);

                    if (result != null) // If there's any result, the book has already been purchased or loaned
                    {
                        TempData["ErrorMessage"] = "You have already purchased or loaned this book.";
                        Console.WriteLine("Book already purchased or loaned, redirecting.");
                        return RedirectToAction("BooksView");
                    }
                }

                // Fetch the book details
                string bookQuery = "SELECT * FROM Books WHERE title = @Title";
                using (SqlCommand bookCommand = new SqlCommand(bookQuery, connection))
                {
                    bookCommand.Parameters.AddWithValue("@Title", bookTitle);
                    SqlDataReader reader = bookCommand.ExecuteReader();

                    if (reader.Read())
                    {
                        int quantity = reader.GetInt32(reader.GetOrdinal("quantity"));
                        Console.WriteLine("Book quantity: " + quantity);

                        if (quantity <= 0)
                        {
                            // Book out of stock
                            TempData["ErrorMessage"] = "This book is currently out of stock.";
                            Console.WriteLine("Book out of stock.");
                            return RedirectToAction("BooksView");
                        }

                        // Create book object and add the file paths
                        Book book = new Book
                        {
                            title = reader.GetString(reader.GetOrdinal("title")),
                            publishYear = reader.GetInt32(reader.GetOrdinal("publishYear")),
                            PurchasePrice = reader.GetInt32(reader.GetOrdinal("PurchasePrice")),
                            LoanPrice = reader.GetInt32(reader.GetOrdinal("LoanPrice")),
                            author = reader.GetString(reader.GetOrdinal("author")),
                            genre = reader.GetString(reader.GetOrdinal("genre")),
                            PdfFilePath = reader.IsDBNull(reader.GetOrdinal("PdfFilePath")) ? null : reader.GetString(reader.GetOrdinal("PdfFilePath")),
                            MobiFilePath = reader.IsDBNull(reader.GetOrdinal("MobiFilePath")) ? null : reader.GetString(reader.GetOrdinal("MobiFilePath")),
                            EpubFilePath = reader.IsDBNull(reader.GetOrdinal("EpubFilePath")) ? null : reader.GetString(reader.GetOrdinal("EpubFilePath")),
                            F2bFilePath = reader.IsDBNull(reader.GetOrdinal("F2bFilePath")) ? null : reader.GetString(reader.GetOrdinal("F2bFilePath"))
                        };

                        Console.WriteLine("Book details fetched: " + book.title);

                        reader.Close();

                        // Add the book to the user's ownedBooks
                        loggedInUser.OwneddBooks.Add(book);
                        Console.WriteLine("Book added to user's owned books.");

                        // Update the UserBooks table
                        string updateQuery = "INSERT INTO UserBooks (Username, BookTitle, type) VALUES (@Username, @Title, 'purchase')";
                        using (SqlCommand updateCommand = new SqlCommand(updateQuery, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                            updateCommand.Parameters.AddWithValue("@Title", book.title);

                            try
                            {
                                updateCommand.ExecuteNonQuery();
                                Console.WriteLine("Book purchase inserted into UserBooks.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Error inserting into UserBooks: " + ex.Message);
                                TempData["ErrorMessage"] = "An error occurred while processing your purchase.";
                                return RedirectToAction("BooksView");
                            }
                        }

                        // Update session data
                        HttpContext.Session.SetString("User", JsonConvert.SerializeObject(loggedInUser));
                        Console.WriteLine("Session updated.");

                        // Send a confirmation email to the user
                        string emailSubject = "Book Purchase Confirmation";
                        string emailBody = $"Dear {loggedInUser.username},\n\nYou have successfully purchased the book '{book.title}'.\n\nThank you for your purchase!";
                        SendMail(loggedInUser.email, emailSubject, emailBody);
                        Console.WriteLine("Purchase confirmation email sent.");
                    }
                    else
                    {
                        Console.WriteLine("Book not found in database.");
                    }
                }
            }

            TempData["Message"] = "The Book was purchased successfully!";
            Console.WriteLine("Redirecting to BooksView.");
            return RedirectToAction("BooksView");
        }













        public IActionResult LoanBook(string bookTitle)
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

                // Check if the customer already owns or has loaned the book
                string checkQuery = @"
        SELECT 1 
        FROM UserBooks 
        WHERE username = @Username AND bookTitle = @Title AND type = 'purchase'
        UNION
        SELECT 1 
        FROM Loans 
        WHERE username = @Username AND bookTitle = @Title";

                using (SqlCommand checkCommand = new SqlCommand(checkQuery, connection))
                {
                    checkCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                    checkCommand.Parameters.AddWithValue("@Title", bookTitle);

                    var result = checkCommand.ExecuteScalar();
                    if (result != null) // If there's any result, the book is already purchased or loaned
                    {
                        TempData["ErrorMessage"] = "You have already purchased or loaned this book.";
                        return RedirectToAction("BooksView");
                    }
                }

                // Check if the customer already has 3 active loans
                string loanCountQuery = "SELECT COUNT(*) FROM Loans WHERE username = @Username";
                using (SqlCommand loanCountCommand = new SqlCommand(loanCountQuery, connection))
                {
                    loanCountCommand.Parameters.AddWithValue("@Username", loggedInUser.username);

                    int loanCount = (int)loanCountCommand.ExecuteScalar();
                    if (loanCount >= 3)
                    {
                        TempData["ErrorMessage"] = "You cannot loan more than 3 books at the same time.";
                        return RedirectToAction("BooksView");
                    }
                }

                // Fetch the book details
                string bookQuery = "SELECT * FROM Books WHERE title = @Title";
                using (SqlCommand bookCommand = new SqlCommand(bookQuery, connection))
                {
                    bookCommand.Parameters.AddWithValue("@Title", bookTitle);
                    SqlDataReader reader = bookCommand.ExecuteReader();

                    if (reader.Read())
                    {
                        int quantity = reader.GetInt32(reader.GetOrdinal("quantity"));
                        if (quantity <= 0)
                        {
                            // Book out of stock
                            TempData["ErrorMessage"] = "This book is currently out of stock.";
                            return RedirectToAction("BooksView");
                        }

                        Book book = new Book
                        {
                            title = reader.GetString(reader.GetOrdinal("title")),
                            publishYear = reader.GetInt32(reader.GetOrdinal("publishYear")),
                            PurchasePrice = reader.GetInt32(reader.GetOrdinal("PurchasePrice")),
                            LoanPrice = reader.GetInt32(reader.GetOrdinal("LoanPrice")),
                            author = reader.GetString(reader.GetOrdinal("author")),
                            genre = reader.GetString(reader.GetOrdinal("genre")),

                        };
                        reader.Close();

                        // Calculate loan and due dates
                        DateTime loanDate = DateTime.Now;
                        DateTime dueDate = loanDate.AddDays(30);

                        // Update the quantity in the database
                        string updateQuantityQuery = "UPDATE Books SET quantity = quantity - 1 WHERE title = @Title";
                        using (SqlCommand updateCommand = new SqlCommand(updateQuantityQuery, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@Title", book.title);
                            updateCommand.ExecuteNonQuery();
                        }

                        // Add the book to the Loans table
                        string updateQuery = "INSERT INTO Loans (username, bookTitle, loanDate, dueDate) VALUES (@Username, @Title, @LoanDate, @DueDate)";
                        using (SqlCommand updateCommand = new SqlCommand(updateQuery, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                            updateCommand.Parameters.AddWithValue("@Title", book.title);
                            updateCommand.Parameters.AddWithValue("@LoanDate", loanDate);
                            updateCommand.Parameters.AddWithValue("@DueDate", dueDate);

                            updateCommand.ExecuteNonQuery();
                        }

                        // Optionally, update session data
                        string emailSubject = "Book Loan Confirmation";
                        string emailBody = $"Dear {loggedInUser.username},\n\nYou have successfully loaned the book '{book.title}'. It is due back by {dueDate.ToShortDateString()}.\n\nThank you for using our service.";
                        SendMail(loggedInUser.email, emailSubject, emailBody);
                        TempData["Message"] = $"You have successfully loaned '{book.title}'. It is due back by {dueDate.ToShortDateString()}.";
                    }
                }
            }
            return RedirectToAction("BooksView");
        }

        public IActionResult AddToCart(string bookTitle)
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

                // Check if the user has already purchased or loaned the book
                string checkPurchaseOrLoanQuery = @"
            SELECT COUNT(1) 
            FROM UserBooks 
            WHERE Username = @Username AND BookTitle = @BookTitle
            UNION ALL
            SELECT COUNT(1) 
            FROM Loans 
            WHERE username = @Username AND BookTitle = @BookTitle";

                using (SqlCommand checkCommand = new SqlCommand(checkPurchaseOrLoanQuery, connection))
                {
                    checkCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                    checkCommand.Parameters.AddWithValue("@BookTitle", bookTitle);

                    int totalCount = 0;
                    using (SqlDataReader reader = checkCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            totalCount += reader.GetInt32(0);
                        }
                    }

                    if (totalCount > 0)
                    {
                        TempData["Message"] = "You cannot add this book to your cart as you have already purchased or loaned it.";
                        return RedirectToAction("BooksView");
                    }
                }

                // Get book details
                string bookQuery = "SELECT * FROM Books WHERE title = @Title";
                using (SqlCommand bookCommand = new SqlCommand(bookQuery, connection))
                {
                    bookCommand.Parameters.AddWithValue("@Title", bookTitle);

                    using (SqlDataReader reader = bookCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string bookTitleFromDb = reader.GetString(reader.GetOrdinal("title"));
                            string bookAuthor = reader.GetString(reader.GetOrdinal("author"));
                            int publishYear = reader.GetInt32(reader.GetOrdinal("publishYear"));
                            int purchasePrice = reader.GetInt32(reader.GetOrdinal("PurchasePrice"));
                            int loanPrice = reader.GetInt32(reader.GetOrdinal("LoanPrice"));
                            string publisher = reader.GetString(reader.GetOrdinal("publisher"));
                            string genre = reader.GetString(reader.GetOrdinal("genre"));
                            int DiscountPercentage = reader.GetInt32(reader.GetOrdinal("DiscountPercentage"));

                            reader.Close();

                            // Check if the book is already in the cart
                            string checkCartQuery = "SELECT COUNT(1) FROM ShoppingCart WHERE Username = @Username AND BookTitle = @BookTitle";
                            using (SqlCommand checkCartCommand = new SqlCommand(checkCartQuery, connection))
                            {
                                checkCartCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                                checkCartCommand.Parameters.AddWithValue("@BookTitle", bookTitleFromDb);

                                int count = (int)checkCartCommand.ExecuteScalar();

                                if (count == 0)
                                {
                                    // Add the book to the cart
                                    string insertQuery = @"
                                INSERT INTO ShoppingCart 
                                (Username, BookTitle, BookAuthor, PublishYear, PurchasePrice, LoanPrice, DiscountPercentage) 
                                VALUES 
                                (@Username, @BookTitle, @BookAuthor, @PublishYear, @PurchasePrice, @LoanPrice, @DiscountPercentage)";

                                    using (SqlCommand insertCommand = new SqlCommand(insertQuery, connection))
                                    {
                                        insertCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                                        insertCommand.Parameters.AddWithValue("@BookTitle", bookTitleFromDb);
                                        insertCommand.Parameters.AddWithValue("@BookAuthor", bookAuthor);
                                        insertCommand.Parameters.AddWithValue("@PublishYear", publishYear);
                                        insertCommand.Parameters.AddWithValue("@PurchasePrice", purchasePrice);
                                        insertCommand.Parameters.AddWithValue("@LoanPrice", loanPrice);
                                        insertCommand.Parameters.AddWithValue("@DiscountPercentage", DiscountPercentage);

                                        insertCommand.ExecuteNonQuery();
                                    }

                                    TempData["Message"] = "Book added to cart successfully!";
                                }
                                else
                                {
                                    TempData["Message"] = "This book is already in your cart!";
                                }
                            }
                        }
                    }
                }
            }

            return RedirectToAction("BooksView");
        }







        public IActionResult ViewCart()
        {
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                return RedirectToAction("UsersView", "Users");
            }

            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            List<Book> cartBooks = new List<Book>();

            // Fetch the shopping cart items from the database
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string cartQuery = "SELECT * FROM ShoppingCart WHERE Username = @Username";
                using (SqlCommand cartCommand = new SqlCommand(cartQuery, connection))
                {
                    cartCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                    SqlDataReader reader = cartCommand.ExecuteReader();

                    while (reader.Read())
                    {
                        cartBooks.Add(new Book
                        {
                            title = reader.GetString(reader.GetOrdinal("BookTitle")),
                            author = reader.GetString(reader.GetOrdinal("BookAuthor")),
                            publishYear = reader.GetInt32(reader.GetOrdinal("PublishYear")),
                            PurchasePrice = reader.GetInt32(reader.GetOrdinal("PurchasePrice")),
                            LoanPrice = reader.GetInt32(reader.GetOrdinal("LoanPrice")),
                            DiscountPercentage = reader.GetInt32(reader.GetOrdinal("DiscountPercentage"))

                        });
                    }
                }
            }

            // Pass the cart items to the partial view
            loggedInUser.ShoppingCart = cartBooks;
            return View("_CartPartial", loggedInUser);
        }




        public void DeleteExpiredLoans()
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Query to delete loans that are past their due date
                string deleteQuery = "DELETE FROM Loans WHERE dueDate < @CurrentDate";
                using (SqlCommand deleteCommand = new SqlCommand(deleteQuery, connection))
                {
                    deleteCommand.Parameters.AddWithValue("@CurrentDate", DateTime.Now);
                    deleteCommand.ExecuteNonQuery();
                }
            }
        }


        public IActionResult RemoveFromCart(string bookTitle)
        {
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                return RedirectToAction("UsersView", "Users");
            }

            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            // Remove the book from the database
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string deleteQuery = "DELETE FROM ShoppingCart WHERE Username = @Username AND BookTitle = @BookTitle";
                using (SqlCommand deleteCommand = new SqlCommand(deleteQuery, connection))
                {
                    deleteCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                    deleteCommand.Parameters.AddWithValue("@BookTitle", bookTitle);
                    deleteCommand.ExecuteNonQuery();
                }
            }

            // Update the session by reloading the cart from the database
            List<Book> updatedCart = new List<Book>();
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string cartQuery = "SELECT * FROM ShoppingCart WHERE Username = @Username";
                using (SqlCommand cartCommand = new SqlCommand(cartQuery, connection))
                {
                    cartCommand.Parameters.AddWithValue("@Username", loggedInUser.username);
                    SqlDataReader reader = cartCommand.ExecuteReader();

                    while (reader.Read())
                    {
                        updatedCart.Add(new Book
                        {
                            title = reader.GetString(reader.GetOrdinal("BookTitle")),
                            author = reader.GetString(reader.GetOrdinal("BookAuthor")),
                            publishYear = reader.GetInt32(reader.GetOrdinal("PublishYear")),
                            PurchasePrice = reader.GetInt32(reader.GetOrdinal("PurchasePrice")),
                            LoanPrice = reader.GetInt32(reader.GetOrdinal("LoanPrice")),
                        });
                    }
                }
            }

            // Update the user's shopping cart in session
            loggedInUser.ShoppingCart = updatedCart;
            HttpContext.Session.SetString("User", JsonConvert.SerializeObject(loggedInUser));

            // Redirect to the cart view
            return RedirectToAction("ViewCart");
        }


        [HttpPost]
        public IActionResult EditBook(string bookTitle, string author, string genre, int quantity, decimal purchasePrice, decimal loanPrice, string publisher, int publishYear, int discountPercentage, DateTime? discountEndDate)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // If no discount end date is provided, set the discount end date to null
                if (discountPercentage > 0 && discountEndDate.HasValue)
                {
                    // Use the admin-provided discount end date
                }
                else
                {
                    discountEndDate = null;
                }

                // Update the book details, including discount and discount end date
                var query = @"UPDATE Books 
                      SET author = @Author, genre = @Genre, quantity = @Quantity, 
                          PurchasePrice = @PurchasePrice, LoanPrice = @LoanPrice, 
                          publisher = @Publisher, publishYear = @PublishYear, 
                          DiscountPercentage = @DiscountPercentage, DiscountEndDate = @DiscountEndDate
                      WHERE Title = @Title";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Title", bookTitle);
                    command.Parameters.AddWithValue("@Author", author);
                    command.Parameters.AddWithValue("@Genre", genre);
                    command.Parameters.AddWithValue("@Quantity", quantity);
                    command.Parameters.AddWithValue("@PurchasePrice", purchasePrice);
                    command.Parameters.AddWithValue("@LoanPrice", loanPrice);
                    command.Parameters.AddWithValue("@Publisher", publisher);
                    command.Parameters.AddWithValue("@PublishYear", publishYear);
                    command.Parameters.AddWithValue("@DiscountPercentage", discountPercentage);
                    command.Parameters.AddWithValue("@DiscountEndDate", (object)discountEndDate ?? DBNull.Value);

                    command.ExecuteNonQuery();
                }

                // Handle waiting list notifications (same as before, if needed)
            }

            TempData["Message"] = "Book updated successfully.";
            return RedirectToAction("ManageBooks");
        }








        [HttpPost]
        public IActionResult EnterWaitingList(string bookTitle)
        {
            // Retrieve the logged-in user from the session
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                TempData["ErrorMessage"] = "You must be logged in to join the waiting list.";
                return RedirectToAction("BooksView");
            }

            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            // Ensure the user object is valid
            if (loggedInUser == null || string.IsNullOrEmpty(loggedInUser.username) || string.IsNullOrEmpty(loggedInUser.email))
            {
                TempData["ErrorMessage"] = "Invalid user session. Please log in again.";
                return RedirectToAction("BooksView");
            }

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Check if the user already purchased or loaned the book
                var checkQuery = @"
            SELECT COUNT(*) FROM UserBooks 
            WHERE Username = @CustomerUsername AND BookTitle = @BookTitle
            UNION ALL
            SELECT COUNT(*) FROM Loans 
            WHERE username = @CustomerUsername AND bookTitle = @BookTitle";

                using (var command = new SqlCommand(checkQuery, connection))
                {
                    command.Parameters.AddWithValue("@CustomerUsername", loggedInUser.username);
                    command.Parameters.AddWithValue("@BookTitle", bookTitle);

                    int alreadyOwnedOrLoaned = 0;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            alreadyOwnedOrLoaned += reader.GetInt32(0);
                        }
                    }

                    if (alreadyOwnedOrLoaned > 0)
                    {
                        TempData["ErrorMessage"] = $"You already own or have loaned '{bookTitle}'. You cannot join the waiting list.";
                        return RedirectToAction("BooksView");
                    }
                }

                // Add to the waiting list
                var insertQuery = @"
            INSERT INTO WaitingList (CustomerUsername, BookTitle, CustomerEmail) 
            VALUES (@CustomerUsername, @BookTitle, @CustomerEmail)";

                using (var command = new SqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@CustomerUsername", loggedInUser.username);
                    command.Parameters.AddWithValue("@BookTitle", bookTitle);
                    command.Parameters.AddWithValue("@CustomerEmail", loggedInUser.email);

                    try
                    {
                        command.ExecuteNonQuery();
                        TempData["Message"] = $"You have been added to the waiting list for '{bookTitle}'.";
                    }
                    catch (SqlException ex)
                    {
                        TempData["ErrorMessage"] = $"Error while adding to the waiting list: {ex.Message}";
                    }
                }
            }

            return RedirectToAction("BooksView");
        }


        [HttpGet]
        public IActionResult NotifyWaitingList(string bookTitle)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Check if the book's quantity is greater than 0
                var bookQuery = "SELECT Quantity FROM Books WHERE Title = @BookTitle";
                int bookQuantity = 0;

                using (var command = new SqlCommand(bookQuery, connection))
                {
                    command.Parameters.AddWithValue("@BookTitle", bookTitle);
                    var result = command.ExecuteScalar();
                    if (result != null)
                    {
                        bookQuantity = Convert.ToInt32(result);
                    }
                }

                if (bookQuantity <= 0)
                {
                    //TempData["ErrorMessage"] = "The book is not available yet.";
                    return RedirectToAction("ManageBooks");
                }

                // Fetch the first three users in the waiting list for this book
                var waitingListQuery = @"
            SELECT TOP 3 CustomerUsername, CustomerEmail 
            FROM WaitingList 
            WHERE BookTitle = @BookTitle"; // Assuming Id determines order in the waiting list

                var recipients = new List<(string Username, string Email)>();

                using (var command = new SqlCommand(waitingListQuery, connection))
                {
                    command.Parameters.AddWithValue("@BookTitle", bookTitle);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            recipients.Add((reader["CustomerUsername"].ToString(), reader["CustomerEmail"].ToString()));
                        }
                    }
                }

                if (!recipients.Any())
                {
                    TempData["ErrorMessage"] = "No users are waiting for this book.";
                    return RedirectToAction("ManageBooks");
                }

                // Send email notifications
                foreach (var recipient in recipients)
                {
                    try
                    {
                        SendMail(recipient.Email, "Book Available Notification",
                            $"Dear {recipient.Username},\n\nThe book '{bookTitle}' is now available. Hurry and get it while it lasts!\n\nBest regards,\nYour Library Team");
                    }
                    catch (Exception ex)
                    {
                        // Log email failure
                        TempData["ErrorMessage"] = $"Failed to send email to {recipient.Email}. Error: {ex.Message}";
                    }
                }

                // Remove the notified users from the waiting list
                foreach (var recipient in recipients)
                {
                    var deleteQuery = "DELETE FROM WaitingList WHERE CustomerUsername = @Username AND BookTitle = @BookTitle";
                    using (var command = new SqlCommand(deleteQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Username", recipient.Username);
                        command.Parameters.AddWithValue("@BookTitle", bookTitle);
                        command.ExecuteNonQuery();
                    }
                }
            }

            TempData["Message"] = "The first three users in the waiting list have been notified.";
            return RedirectToAction("ManageBooks");
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

        public IActionResult GetWaitingListCount(string bookTitle)
        {
            int waitingCount = 0;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT COUNT(*) FROM WaitingList WHERE BookTitle = @BookTitle";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@BookTitle", bookTitle);
                    waitingCount = (int)command.ExecuteScalar();
                }
            }

            // Return the count directly as a plain integer
            return Content(waitingCount.ToString());
        }













        [HttpPost]
        public IActionResult ProcessPayment(string actionType, string BookTitle)
        {
            // Debugging: Log the received bookTitle and actionType
            Console.WriteLine($"Received actionType: {actionType}, bookTitle: {BookTitle}");

            // If actionType is "loan", process the loan
            if (actionType == "loan")
            {
                Console.WriteLine($"Processing loan for book: {BookTitle}"); // Debugging the loan process
                LoanBook(BookTitle);
            }
            // If actionType is "purchase", process the purchase
            else if (actionType == "purchase")
            {
                Console.WriteLine($"Processing purchase for book: {BookTitle}"); // Debugging the purchase process
                PurchaseBook(BookTitle);
            }

            // Process payment logic here...

            return RedirectToAction("BooksView"); // Redirect to books view or any other page
        }

        public IActionResult Payment()
        {
            return View("Payment");
        }


        [HttpPost]
        public IActionResult BuyAllBooks()
        {
            var userSession = HttpContext.Session.GetString("User");
            if (string.IsNullOrEmpty(userSession))
            {
                return RedirectToAction("UsersView", "Users");
            }

            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            // Iterate through the shopping cart
            foreach (var item in loggedInUser.ShoppingCart)
            {
                // Redirect to the PurchaseBook action for each item in the cart
                RedirectToAction("PurchaseBook", new { bookTitle = item.title });
            }

            // After processing all items, redirect back to the cart view or a confirmation page
            return RedirectToAction("ViewCart"); // Replace with your actual cart view name
        }



        [HttpPost]
        public IActionResult SendEmail()
        {
            // Get the logged-in user's email (you can retrieve this from session or your database)
            var userSession = HttpContext.Session.GetString("User");
            User loggedInUser = JsonConvert.DeserializeObject<User>(userSession);

            if (loggedInUser != null)
            {
                string toEmail = loggedInUser.email; // Assuming 'email' is a property of your User object
                string subject = "Subject of the email";
                string body = "This is the body of the email.";

                try
                {
                    // Call SendMail to send the email
                    SendMail(toEmail, subject, body);
                    TempData["Message"] = "Email sent successfully!";
                }
                catch (Exception ex)
                {
                    TempData["Message"] = $"Failed to send email. Error: {ex.Message}";
                }
            }
            else
            {
                TempData["Message"] = "User not logged in.";
            }

            // Redirect back to the same page or another page as needed
            return RedirectToAction("ProfileView", "Main"); // Example: Redirect to the ProfileView
        }

    }
}
