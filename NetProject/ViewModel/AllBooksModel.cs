using NetProject.Models;
namespace NetProject.ViewModel{
    public class AllBooksModel{

        public Book book{get;set;}

        public List<Book> allBooks{get;set;}=new List<Book>();
    }
}