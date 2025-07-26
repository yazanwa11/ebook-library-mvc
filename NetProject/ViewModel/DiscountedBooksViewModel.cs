using NetProject.Models;

namespace NetProject.ViewModel
{
    public class DiscountedBooksViewModel
    {
        public User LoggedInUser { get; set; }
        public List<Book> DiscountedBooks { get; set; }
    }
}
