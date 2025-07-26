using System.ComponentModel.DataAnnotations;

namespace NetProject.Models
{
    public class CreditCard
    {
        [Required(ErrorMessage = "First name is required")]
        [RegularExpression("^[A-Za-zא-ת]{2,50}$", ErrorMessage = "First name must contain only letters")]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [RegularExpression("^[A-Za-zא-ת]{2,50}$", ErrorMessage = "Last name must contain only letters")]
        public string LastName { get; set; }

        [Required(ErrorMessage = "ID is required")]
        [RegularExpression(@"^\d{9}$", ErrorMessage = "ID must be exactly 9 digits")]
        public string ID { get; set; }

        [Required(ErrorMessage = "Card number is required")]
        [RegularExpression(@"^\d{4} \d{4} \d{4} \d{4}$", ErrorMessage = "Card number must be in format: 1234 5678 9012 3456")]
        public string CardNumber { get; set; }

        [Required(ErrorMessage = "Valid date is required")]
        [RegularExpression(@"^(0[1-9]|1[0-2])\/\d{2}$", ErrorMessage = "Date must be in MM/YY format")]
        public string ValidDate { get; set; }

        [Required(ErrorMessage = "CVC is required")]
        [RegularExpression(@"^\d{3}$", ErrorMessage = "CVC must be exactly 3 digits")]
        public string CVC { get; set; }
    }
}
