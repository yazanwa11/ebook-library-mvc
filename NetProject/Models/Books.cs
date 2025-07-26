using System.ComponentModel.DataAnnotations;

namespace NetProject.Models
{
    public class Book
    {
        [Key]
        public string title { get; set; }


        public int publishYear { get; set; }

        public int PurchasePrice { get; set; }

        public bool isBuyOnly{get; set;}

        public int LoanPrice { get; set; }

        public string author { get; set; }

        public string publisher { get; set; }

        public string genre { get; set; }

        public DateTime LoanDate { get; set; }
        public DateTime DueDate { get; set; }

        public int quantity { get; set; }

        public int AvailableToBorrow { get; set; }


        public int DiscountPercentage { get; set; }

        public decimal DiscountedPrice
        {
            get
            {
                return (int)(PurchasePrice - (PurchasePrice * ((decimal)DiscountPercentage / 100)));
            }
        }

        public decimal DiscountedLoanPrice
        {
            get
            {
                return (int)(LoanPrice - (LoanPrice * ((decimal)DiscountPercentage / 100)));
            }
        }
        public string CoverImage { get; set; }

        public DateTime? DiscountEndDate { get; set; }

        public double AverageRating { get; set; }

        public int RatingCount { get; set; }

        public string PdfFilePath { get; set; }
        public string EpubFilePath { get; set; }
        public string MobiFilePath { get; set; }
        public string F2bFilePath { get; set; }

    }
}
