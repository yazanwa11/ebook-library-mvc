using Microsoft.AspNetCore.Mvc;
using NetProject.Models;
using System.Data.SqlClient;
using Newtonsoft.Json;


namespace NetProject.Controllers
{
    public class UsersController : Controller
    {
        private readonly IConfiguration _configuration;
        public string connectionString = "";

        public UsersController(IConfiguration configuration)
        {
            _configuration = configuration;
            connectionString = _configuration.GetConnectionString("myConnect");
        }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult ShowDetails(User user)
        {
            if (ModelState.IsValid)
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = "insert into Users values(@value1,@value2,@value3,@value4)";
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@value1", user.username);
                        string hashedPassword = HashPassword(user.password);
                        command.Parameters.AddWithValue("@value2", hashedPassword);

                        command.Parameters.AddWithValue("@value3", user.email);
                        command.Parameters.AddWithValue("@value4", "customer");
                        int RowsEffected = command.ExecuteNonQuery();
                        if (RowsEffected > 0)
                        {
                            TempData["Message"] = "Registration successful! Please log in.";
                            return View("UsersView", user);
                        }
                        else
                        {
                            return View("RegisterView", new User());
                        }
                    }
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Registration Failed!";
                return View("RegisterView", new User());
            }
        }

        public IActionResult RegisterView()
        {
            User user = new User();
            return View("RegisterView", user);
        }
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("LoginView", "Users");
        }


        public IActionResult LoginView()
        {
            return View("UsersView", new User());
        }


        [HttpPost]
        public IActionResult CheckUser(User user)
        {
            if (string.IsNullOrEmpty(user.username) || string.IsNullOrEmpty(user.password))
            {
                ModelState.AddModelError("", "Please provide both username and password.");
                return View("UsersView", new User());
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // ✅ Use parameterized query to prevent SQL injection
                string SQLquery = "SELECT * FROM Users WHERE username = @Username AND password = @Password";
                using (SqlCommand command = new SqlCommand(SQLquery, connection))
                {
                    string hashedInput = HashPassword(user.password);
                    command.Parameters.AddWithValue("@Username", user.username);
                    command.Parameters.AddWithValue("@Password", hashedInput);

                    SqlDataReader reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        User loggedInUser = new User
                        {
                            username = reader["username"].ToString(),
                            email = reader["email"].ToString(),
                            type = reader["type"].ToString()
                        };

                        HttpContext.Session.SetString("User", JsonConvert.SerializeObject(loggedInUser));

                        if (loggedInUser.type == "admin")
                            return RedirectToAction("ManageBooks", "Books", loggedInUser);
                        else
                            return RedirectToAction("LoggedinView", "Main", loggedInUser);
                    }
                }
            }

            TempData["ErrorMessage"] = "One of the Login field is wrong!!";
            return View("UsersView", user);
        }





        public IActionResult ForgotPasswordView()
        {
            return View("ForgotPassword");
        }

        private string HashPassword(string password)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        [Route("Users/ResetPassword")]
        [HttpPost]
        public IActionResult ResetPassword(string username, string email, string currentPassword, string newPassword)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Step 1: Verify identity
                string query = "SELECT password FROM Users WHERE username = @Username AND email = @Email";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Username", username);
                    command.Parameters.AddWithValue("@Email", email);

                    var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        string storedHashedPassword = reader.GetString(0);
                        string inputHashed = HashPassword(currentPassword);

                        if (storedHashedPassword != inputHashed)
                        {
                            TempData["ErrorMessage"] = "Current password is incorrect.";
                            return RedirectToAction("ForgotPasswordView");
                        }
                        reader.Close();
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Username/email combination not found.";
                        return RedirectToAction("ForgotPasswordView");
                    }
                }

                // Step 2: If verified, update to new password
                string hashedNew = HashPassword(newPassword);
                string updateQuery = "UPDATE Users SET password = @Password WHERE username = @Username AND email = @Email";
                using (SqlCommand updateCommand = new SqlCommand(updateQuery, connection))
                {
                    updateCommand.Parameters.AddWithValue("@Password", hashedNew);
                    updateCommand.Parameters.AddWithValue("@Username", username);
                    updateCommand.Parameters.AddWithValue("@Email", email);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    if (rowsAffected > 0)
                    {
                        TempData["Message"] = "Password reset successfully.";
                        return RedirectToAction("LoginView");
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Password reset failed.";
                        return RedirectToAction("ForgotPasswordView");
                    }
                }
            }
        }







    }

}

