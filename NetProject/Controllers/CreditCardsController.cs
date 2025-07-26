using Microsoft.AspNetCore.Mvc;
using NetProject.Models;
using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public class CreditCardsController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly string connectionString;

    public CreditCardsController(IConfiguration configuration)
    {
        _configuration = configuration;
        connectionString = _configuration.GetConnectionString("myConnect");
    }

    [HttpGet]
    public IActionResult Add()
    {
        return View(); // will find Views/CreditCards/Add.cshtml
    }

    [HttpPost]
    public IActionResult Add(CreditCard card)
    {
        Console.WriteLine("💡 POST hit. Incoming data:");
        Console.WriteLine($"FirstName: {card.FirstName}, LastName: {card.LastName}, ID: {card.ID}");

        if (!ModelState.IsValid)
        {
            Console.WriteLine("❌ Model invalid:");
            foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                Console.WriteLine($" - {error.ErrorMessage}");

            return View(card);
        }

        try
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "INSERT INTO CreditCards (FirstName, LastName, ID, CardNumber, ValidDate, CVC) VALUES (@FirstName, @LastName, @ID, @CardNumber, @ValidDate, @CVC)";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FirstName", card.FirstName);
                    command.Parameters.AddWithValue("@LastName", card.LastName);
                    command.Parameters.AddWithValue("@ID", card.ID);
                    command.Parameters.AddWithValue("@CardNumber", card.CardNumber);
                    command.Parameters.AddWithValue("@ValidDate", card.ValidDate);
                    command.Parameters.AddWithValue("@CVC", card.CVC);

                    int rows = command.ExecuteNonQuery();
                    Console.WriteLine($"✅ Inserted {rows} row(s)");
                }
            }

            TempData["Message"] = "✅ Credit card added!";
            ModelState.Clear();
            return View();
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ SQL ERROR: " + ex.Message);
            TempData["Message"] = "❌ Something went wrong.";
            return View(card);
        }
    }
}



