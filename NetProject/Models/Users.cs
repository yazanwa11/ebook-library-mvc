using NetProject.ViewModel;
using System.ComponentModel.DataAnnotations;
namespace NetProject.Models
{
    public class User
    {

        [Key]
        [Required]
        [StringLength(8,MinimumLength=4,ErrorMessage ="username must be between 4-8 charecters")]
        public string username{get;set;}


        [Required]
        [StringLength(100,MinimumLength=8,ErrorMessage ="password must be between 8-10 charecters")]
        public string password{get;set;}
        

        public string email{get;set;}


        [Compare("password",ErrorMessage ="Password confirmation isn't valid")]
        public string ConfirmPassword{get;set;}
        


        public string type{get;set;} = "customer";

        public List<Book> OwneddBooks{get;set;}= new List<Book>();

        public List<Book> LoanedBooks{get;set;}=new List<Book>(); 

        public List<Book> ShoppingCart { get; set; } = new List<Book>();

    }
}