using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web;

namespace WebApplication1.Models
{
    public class Product
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; }

        public string Category { get; set; }

        public decimal Price { get; set; }

        public decimal Quantity { get; set; }

        public string QuantityDescription { get; set; }

        public string Unit { get; set; }

        public string ProductImage { get; set; }

        public DateTime CreatedDate { get; set; }

        public decimal TodaySales { get; set; }
    }

    public class ProductCatalogViewModel
    {
        public IEnumerable<string> Categories { get; set; }

        public IEnumerable<Product> Products { get; set; }
    }

    public class AddProductViewModel
    {
        [Required(ErrorMessage = "Product name is required.")]
        [StringLength(150, ErrorMessage = "Product name cannot exceed 150 characters.")]
        [Display(Name = "Product name")]
        public string ProductName { get; set; }

        [Required(ErrorMessage = "Please select a category.")]
        public string Category { get; set; }

        [Required(ErrorMessage = "Price is required.")]
        [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Price must be greater than zero.")]
        [Display(Name = "Price (INR)")]
        public decimal? Price { get; set; }

        [Required(ErrorMessage = "Quantity is required.")]
        [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Quantity must be greater than zero.")]
        [Display(Name = "Quantity / stock")]
        public decimal? Quantity { get; set; }

        [StringLength(50, ErrorMessage = "Quantity description cannot exceed 50 characters.")]
        [Display(Name = "Quantity description")]
        public string QuantityDescription { get; set; }

        [Required(ErrorMessage = "Please select a unit.")]
        public string Unit { get; set; }

        [Display(Name = "Product photo")]
        public HttpPostedFileBase ProductPhoto { get; set; }

        public int? SavedProductId { get; set; }

        public IEnumerable<string> Categories { get; set; }

        public IEnumerable<string> Units { get; set; }
    }

    public class ProductEditViewModel
    {
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Product name is required.")]
        [StringLength(150, ErrorMessage = "Product name cannot exceed 150 characters.")]
        public string ProductName { get; set; }

        [Required(ErrorMessage = "Please select a category.")]
        public string Category { get; set; }

        [Required(ErrorMessage = "Price is required.")]
        [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Price must be greater than zero.")]
        public decimal? Price { get; set; }

        [Required(ErrorMessage = "Quantity is required.")]
        [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Quantity must be greater than zero.")]
        public decimal? Quantity { get; set; }

        [StringLength(50, ErrorMessage = "Quantity description cannot exceed 50 characters.")]
        [Display(Name = "Quantity description")]
        public string QuantityDescription { get; set; }

        [Required(ErrorMessage = "Please select a unit.")]
        public string Unit { get; set; }

        [Display(Name = "Product photo")]
        public HttpPostedFileBase ProductPhoto { get; set; }

        public string CurrentImage { get; set; }

        public string LookupProductName { get; set; }

        public IEnumerable<string> Categories { get; set; }

        public IEnumerable<string> Units { get; set; }
    }

    public class ProductDeleteViewModel
    {
        public int ProductId { get; set; }

        public string Category { get; set; }

        public string ProductName { get; set; }

        public string QuantityDescription { get; set; }

        public Product ProductDetails { get; set; }

        public IEnumerable<string> Categories { get; set; }
    }
}