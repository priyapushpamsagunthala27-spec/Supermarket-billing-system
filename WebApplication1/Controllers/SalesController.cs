using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class SalesController : Controller
    {
        private const string ConnectionStringName = "PmpSupermarketDb";
        private static readonly int[] ReportYears = { 2026, 2027 };

        [HttpGet]
        public ActionResult Today()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            return View(new TodaySalesStatisticsViewModel
            {
                SaleDate = DateTime.Today,
                Categories = GetTodayCategorySales()
            });
        }

        [HttpGet]
        public ActionResult Monthly(int? month, int? year)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            int selectedMonth = month.HasValue && month.Value >= 1 && month.Value <= 12
                ? month.Value
                : DateTime.Today.Month;
            int selectedYear = year.HasValue && ReportYears.Contains(year.Value)
                ? year.Value
                : (ReportYears.Contains(DateTime.Today.Year) ? DateTime.Today.Year : ReportYears[0]);

            return View(new MonthlySalesStatisticsViewModel
            {
                Month = selectedMonth,
                Year = selectedYear,
                Days = GetMonthlySales(selectedMonth, selectedYear)
            });
        }

        private IEnumerable<SalesCategoryViewModel> GetTodayCategorySales()
        {
            var results = new List<SalesCategoryViewModel>();
            const string query = @"SELECT p.Category, SUM(s.Amount) AS TotalAmount
FROM dbo.SalesLog s
INNER JOIN dbo.Products p ON p.ProductId = s.ProductId
WHERE s.SaleDate = CAST(GETDATE() AS date)
GROUP BY p.Category
ORDER BY TotalAmount DESC;";

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    decimal total = 0m;
                    while (reader.Read())
                    {
                        results.Add(new SalesCategoryViewModel
                        {
                            Category = reader.GetString(0),
                            Amount = reader.GetDecimal(1)
                        });
                        total += reader.GetDecimal(1);
                    }

                    foreach (SalesCategoryViewModel result in results)
                    {
                        result.Percentage = total == 0m ? 0m : Math.Round(result.Amount * 100m / total, 2);
                    }
                }
            }

            return results;
        }

        private IEnumerable<SalesDayViewModel> GetMonthlySales(int month, int year)
        {
            var totals = new Dictionary<DateTime, decimal>();
            const string query = @"SELECT s.SaleDate, SUM(s.Amount) AS Amount
FROM dbo.SalesLog s
WHERE s.SaleDate >= @StartDate AND s.SaleDate < @EndDate
GROUP BY s.SaleDate
ORDER BY s.SaleDate;";
            DateTime startDate = new DateTime(year, month, 1);
            DateTime endDate = startDate.AddMonths(1);

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.Add("@StartDate", SqlDbType.Date).Value = startDate;
                command.Parameters.Add("@EndDate", SqlDbType.Date).Value = endDate;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        totals[reader.GetDateTime(0).Date] = reader.GetDecimal(1);
                    }
                }
            }

            return totals.OrderBy(item => item.Key).Select(item => new SalesDayViewModel
            {
                SaleDate = item.Key,
                Amount = item.Value
            }).ToList();
        }

        private bool IsAdmin()
        {
            return Session["Role"] != null && string.Equals(Convert.ToString(Session["Role"]), "Admin", StringComparison.OrdinalIgnoreCase);
        }

        private string GetConnectionString()
        {
            ConnectionStringSettings setting = ConfigurationManager.ConnectionStrings[ConnectionStringName];
            return setting == null ? string.Empty : setting.ConnectionString;
        }
    }
}
