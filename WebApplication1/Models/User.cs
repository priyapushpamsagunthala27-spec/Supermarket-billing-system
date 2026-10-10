using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models
{
    public class User
    {
        public int UserId { get; set; }

        public string Username { get; set; }

        public string PasswordHash { get; set; }

        public string Role { get; set; }

        public DateTime CreatedDate { get; set; }
    }

    public class LoginViewModel
    {
        [Required(ErrorMessage = "Username is required.")]
        [Display(Name = "Username")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Username is required.")]
        [StringLength(50, MinimumLength = 4, ErrorMessage = "Username must be at least 4 characters.")]
        [RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "Username can contain only letters, numbers, dots, underscores, and hyphens.")]
        [Display(Name = "Username")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).+$", ErrorMessage = "Password needs an uppercase letter, lowercase letter, number, and special character.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Please confirm your password.")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; }
    }

    public class UserDashboardViewModel
    {
        public string Username { get; set; }

        public IEnumerable<string> Categories { get; set; }

        public IEnumerable<Product> Products { get; set; }

        public IEnumerable<BillHistoryViewModel> Bills { get; set; }

        public string BillNumberFilter { get; set; }

        public string BillDateFilter { get; set; }

        public int? BillMonthFilter { get; set; }

        public int? BillYearFilter { get; set; }

        public BillingHistoryDetailViewModel SelectedBill { get; set; }
    }

    public class BillHistoryViewModel
    {
        public int BillId { get; set; }

        public string BillNumber { get; set; }

        public DateTime BillDate { get; set; }

        public decimal TotalAmount { get; set; }
    }

    public class BillingHistoryDetailViewModel
    {
        public int BillId { get; set; }

        public string BillNumber { get; set; }

        public DateTime BillDate { get; set; }

        public decimal TotalAmount { get; set; }

        public IEnumerable<BillingItemViewModel> Items { get; set; }
    }

    public class AdminBillHistoryViewModel
    {
        public int BillId { get; set; }

        public int UserId { get; set; }

        public string Username { get; set; }

        public decimal TotalAmount { get; set; }
    }

    public class AdminBillDetailsViewModel
    {
        public int BillId { get; set; }

        public int UserId { get; set; }

        public string Username { get; set; }

        public string BillNumber { get; set; }

        public DateTime BillDate { get; set; }

        public decimal TotalAmount { get; set; }

        public IEnumerable<BillingItemViewModel> Items { get; set; }
    }

    public class CartItemViewModel
    {
        public int CartItemId { get; set; }

        public int ProductId { get; set; }

        public string ProductName { get; set; }

        public string ProductImage { get; set; }

        public string QuantityDescription { get; set; }

        public decimal NeededQuantity { get; set; }

        public string Unit { get; set; }

        public decimal CurrentQuantity { get; set; }
    }

    public class CartViewModel
    {
        public string Username { get; set; }

        public IEnumerable<CartItemViewModel> Items { get; set; }

        public string ErrorMessage { get; set; }

        public string SuccessMessage { get; set; }
    }

    public class OrderItemViewModel
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; }

        public decimal NeededQuantity { get; set; }

        public string Unit { get; set; }

        public string QuantityDescription { get; set; }
    }

    public class YourOrderViewModel
    {
        public string Username { get; set; }

        public IEnumerable<OrderItemViewModel> Items { get; set; }
    }

    public class BillingItemViewModel
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; }

        public decimal QuantityPurchased { get; set; }

        public string Unit { get; set; }

        public decimal Price { get; set; }

        public decimal Amount { get; set; }
    }

    public class BillingViewModel
    {
        public string BillNumber { get; set; }

        public IEnumerable<BillingItemViewModel> Items { get; set; }

        public decimal TotalAmount { get; set; }
    }
}
