using System;
using System.Collections.Generic;

namespace WebApplication1.Models
{
    public class SalesCategoryViewModel
    {
        public string Category { get; set; }

        public decimal Amount { get; set; }

        public decimal Percentage { get; set; }
    }

    public class SalesDayViewModel
    {
        public DateTime SaleDate { get; set; }

        public decimal Amount { get; set; }
    }

    public class TodaySalesStatisticsViewModel
    {
        public DateTime SaleDate { get; set; }

        public IEnumerable<SalesCategoryViewModel> Categories { get; set; }
    }

    public class MonthlySalesStatisticsViewModel
    {
        public int Month { get; set; }

        public int Year { get; set; }

        public IEnumerable<SalesDayViewModel> Days { get; set; }
    }
}
