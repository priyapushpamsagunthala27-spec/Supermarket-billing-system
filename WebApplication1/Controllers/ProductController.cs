using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class ProductController : Controller
    {
        private const string ConnectionStringName = "PmpSupermarketDb";
        private const int MaximumPhotoBytes = 5 * 1024 * 1024;
        private static readonly string[] AllowedCategories =
        {
            "Groceries", "Beverages", "Snacks", "Dairy and Eggs", "Personal Care",
            "Household", "Bakery", "Fruits", "Vegetables"
        };
        private static readonly string[] ProductCatalogCategories =
        {
            "Groceries", "Beverages", "Snacks", "Dairy and Eggs", "Fruits",
            "Vegetables", "Personal Care", "Household", "Bakery"
        };
        private static readonly string[] AllowedUnits = { "kg", "g", "litre", "packet", "piece" };

        [HttpGet]
        public ActionResult Index()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            return View(new ProductCatalogViewModel
            {
                Categories = ProductCatalogCategories,
                Products = GetProducts()
            });
        }

        [HttpGet]
        public ActionResult Add()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            return View(CreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Add(AddProductViewModel model)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            ValidateSelections(model);
            string productImagePath = null;
            if (model.ProductPhoto != null && model.ProductPhoto.ContentLength > 0 &&
                !TrySaveProductPhoto(model.ProductPhoto, out productImagePath))
            {
                ModelState.AddModelError("ProductPhoto", "Photo must be a JPG, JPEG, or PNG image up to 5 MB.");
            }

            if (!ModelState.IsValid)
            {
                return View(PrepareViewModel(model));
            }

            try
            {
                model.SavedProductId = InsertProduct(model, productImagePath);
                ViewBag.SuccessMessage = "Product added successfully.";
                ModelState.Clear();
                return View(PrepareViewModel(model));
            }
            catch (SqlException exception)
            {
                ModelState.AddModelError("", GetDatabaseErrorMessage(exception));
                return View(PrepareViewModel(model));
            }
        }

        [HttpGet]
        public ActionResult Update(int? id = null)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            ProductEditViewModel model = new ProductEditViewModel();
            if (id.HasValue && id.Value > 0)
            {
                Product product = FindProductById(id.Value);
                if (product != null)
                {
                    model.ProductId = product.ProductId;
                    model.ProductName = product.ProductName;
                    model.Category = product.Category;
                    model.Price = product.Price;
                    model.Quantity = product.Quantity;
                    model.QuantityDescription = product.QuantityDescription;
                    model.Unit = product.Unit;
                    model.CurrentImage = product.ProductImage;
                }
            }

            return View(PrepareEditViewModel(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Update(ProductEditViewModel model)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            ValidateSelections(model.Category, model.Unit);
            string productImagePath = model.CurrentImage;
            if (model.ProductPhoto != null && model.ProductPhoto.ContentLength > 0 &&
                !TrySaveProductPhoto(model.ProductPhoto, out productImagePath))
            {
                ModelState.AddModelError("ProductPhoto", "Photo must be a JPG, JPEG, or PNG image up to 5 MB.");
            }

            if (!ModelState.IsValid)
            {
                return View(PrepareEditViewModel(model));
            }

            try
            {
                if (!UpdateProduct(model, productImagePath))
                {
                    ModelState.AddModelError("", "The selected product could not be found.");
                    return View(PrepareEditViewModel(model));
                }

                model.CurrentImage = productImagePath;
                ViewBag.SuccessMessage = "Product updated successfully.";
                ModelState.Clear();
                return View(PrepareEditViewModel(model));
            }
            catch (SqlException exception)
            {
                ModelState.AddModelError("", GetDatabaseErrorMessage(exception));
                return View(PrepareEditViewModel(model));
            }
        }

        [HttpGet]
        public ActionResult Delete()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            return View(new ProductDeleteViewModel { Categories = AllowedCategories });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(ProductDeleteViewModel model)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            try
            {
                if (!DeleteProduct(model.ProductId))
                {
                    ModelState.AddModelError("", "The selected product could not be found.");
                    return View(PrepareDeleteViewModel(model));
                }

                ViewBag.SuccessMessage = "Product deleted successfully.";
                return View(new ProductDeleteViewModel { Categories = AllowedCategories });
            }
            catch (SqlException exception)
            {
                ModelState.AddModelError("", GetDatabaseErrorMessage(exception));
                return View(PrepareDeleteViewModel(model));
            }
        }

        [HttpGet]
        public JsonResult ProductNames(string category)
        {
            if (!IsAdmin())
            {
                return Json(new object[0], JsonRequestBehavior.AllowGet);
            }

            return Json(GetProductNames(category), JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ProductDetails(string category, string productName)
        {
            if (!IsAdmin())
            {
                return Json(null, JsonRequestBehavior.AllowGet);
            }

            return Json(FindProduct(category, productName), JsonRequestBehavior.AllowGet);
        }

        private AddProductViewModel CreateViewModel()
        {
            return PrepareViewModel(new AddProductViewModel());
        }

        private AddProductViewModel PrepareViewModel(AddProductViewModel model)
        {
            model.Categories = AllowedCategories;
            model.Units = AllowedUnits;
            return model;
        }

        private void ValidateSelections(AddProductViewModel model)
        {
            ValidateSelections(model.Category, model.Unit);
        }

        private void ValidateSelections(string category, string unit)
        {
            if (!string.IsNullOrWhiteSpace(category) && !AllowedCategories.Contains(category, StringComparer.Ordinal))
            {
                ModelState.AddModelError("Category", "Please select a valid category.");
            }

            if (!string.IsNullOrWhiteSpace(unit) && !AllowedUnits.Contains(unit, StringComparer.Ordinal))
            {
                ModelState.AddModelError("Unit", "Please select a valid unit.");
            }
        }

        private ProductEditViewModel PrepareEditViewModel(ProductEditViewModel model)
        {
            model.Categories = AllowedCategories;
            model.Units = AllowedUnits;
            ViewBag.OutOfStockProducts = GetOutOfStockProducts();
            return model;
        }

        private List<Product> GetOutOfStockProducts()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(GetConnectionString()))
                using (SqlCommand command = new SqlCommand(@"SELECT ProductId, ProductName, Category, Price, Quantity, QuantityDescription, Unit, ProductImage, CreatedDate
FROM dbo.Products
WHERE Quantity = 0
ORDER BY ProductName ASC;", connection))
                {
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        var products = new List<Product>();
                        while (reader.Read())
                        {
                            Product product = ReadProduct(reader, false);
                            product.ProductImage = ResolveProductImagePath(product.ProductImage);
                            products.Add(product);
                        }

                        return products;
                    }
                }
            }
            catch (SqlException)
            {
                return new List<Product>();
            }
        }

        private Product FindProductById(int productId)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(GetConnectionString()))
                using (SqlCommand command = new SqlCommand(@"SELECT TOP 1 ProductId, ProductName, Category, Price, Quantity, QuantityDescription, Unit, ProductImage, CreatedDate
FROM dbo.Products WHERE ProductId = @ProductId;", connection))
                {
                    command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        if (reader.Read())
                        {
                            Product product = ReadProduct(reader, false);
                            product.ProductImage = ResolveProductImagePath(product.ProductImage);
                            return product;
                        }

                        return null;
                    }
                }
            }
            catch (SqlException)
            {
                return null;
            }
        }

        private ProductDeleteViewModel PrepareDeleteViewModel(ProductDeleteViewModel model)
        {
            model.Categories = AllowedCategories;
            return model;
        }

        private string[] GetProductNames(string category)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT DISTINCT ProductName
FROM dbo.Products WHERE Category = @Category ORDER BY ProductName;", connection))
            {
                command.Parameters.Add("@Category", SqlDbType.NVarChar, 50).Value = category ?? string.Empty;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    var names = new System.Collections.Generic.List<string>();
                    while (reader.Read())
                    {
                        names.Add(reader.GetString(0));
                    }

                    return names.ToArray();
                }
            }
        }

        private Product FindProduct(string category, string productName)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT TOP 1 ProductId, ProductName, Category, Price, Quantity, QuantityDescription, Unit, ProductImage, CreatedDate
FROM dbo.Products WHERE Category = @Category AND ProductName = @ProductName ORDER BY ProductId;", connection))
            {
                command.Parameters.Add("@Category", SqlDbType.NVarChar, 50).Value = category ?? string.Empty;
                command.Parameters.Add("@ProductName", SqlDbType.NVarChar, 150).Value = productName ?? string.Empty;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleRow))
                {
                    return reader.Read() ? ReadProduct(reader) : null;
                }
            }
        }

        private Product[] GetProducts()
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT p.ProductId, p.ProductName, p.Category, p.Price, p.Quantity, p.QuantityDescription, p.Unit, p.ProductImage, p.CreatedDate,
ISNULL((SELECT SUM(s.Amount) FROM dbo.SalesLog s WHERE s.ProductId = p.ProductId AND s.SaleDate = CAST(GETDATE() AS date)), 0) AS TodaySales
FROM dbo.Products p ORDER BY p.Category, p.ProductName, p.ProductId;", connection))
            {
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    var products = new System.Collections.Generic.List<Product>();
                    while (reader.Read())
                    {
                        Product product = ReadProduct(reader, true);
                        product.ProductImage = ResolveProductImagePath(product.ProductImage);
                        products.Add(product);
                    }

                    return products.ToArray();
                }
            }
        }

        private Product ReadProduct(SqlDataReader reader, bool includesTodaySales = false)
        {
            return new Product
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                Category = reader.GetString(2),
                Price = reader.GetDecimal(3),
                Quantity = reader.GetDecimal(4),
                QuantityDescription = reader.IsDBNull(5) ? null : reader.GetString(5),
                Unit = reader.GetString(6),
                ProductImage = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedDate = reader.GetDateTime(8),
                TodaySales = includesTodaySales ? reader.GetDecimal(9) : 0m
            };
        }

        private bool UpdateProduct(ProductEditViewModel model, string productImagePath)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"UPDATE dbo.Products
SET ProductName = @ProductName, Category = @Category, Price = @Price,
    Quantity = @Quantity, QuantityDescription = @QuantityDescription,
    Unit = @Unit, ProductImage = @ProductImage
WHERE ProductId = @ProductId;", connection))
            {
                AddProductParameters(command, model.ProductName, model.Category, model.Price.Value, model.Quantity.Value, model.QuantityDescription, model.Unit, productImagePath);
                command.Parameters.Add("@ProductId", SqlDbType.Int).Value = model.ProductId;
                connection.Open();
                return command.ExecuteNonQuery() == 1;
            }
        }

        private bool DeleteProduct(int productId)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand("DELETE FROM dbo.Products WHERE ProductId = @ProductId;", connection))
            {
                command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;
                connection.Open();
                return command.ExecuteNonQuery() == 1;
            }
        }

        private void AddProductParameters(SqlCommand command, string productName, string category, decimal price, decimal quantity, string quantityDescription, string unit, string productImagePath)
        {
            command.Parameters.Add("@ProductName", SqlDbType.NVarChar, 150).Value = productName.Trim();
            command.Parameters.Add("@Category", SqlDbType.NVarChar, 50).Value = category;
            command.Parameters.Add("@Price", SqlDbType.Decimal).Value = price;
            command.Parameters["@Price"].Precision = 10;
            command.Parameters["@Price"].Scale = 2;
            command.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = quantity;
            command.Parameters["@Quantity"].Precision = 10;
            command.Parameters["@Quantity"].Scale = 2;
            command.Parameters.Add("@QuantityDescription", SqlDbType.NVarChar, 50).Value = (object)quantityDescription ?? DBNull.Value;
            command.Parameters.Add("@Unit", SqlDbType.NVarChar, 20).Value = unit;
            command.Parameters.Add("@ProductImage", SqlDbType.NVarChar, 500).Value = (object)productImagePath ?? DBNull.Value;
        }

        private bool TrySaveProductPhoto(HttpPostedFileBase photo, out string productImagePath)
        {
            productImagePath = null;
            string extension = Path.GetExtension(photo.FileName);
            if (photo.ContentLength > MaximumPhotoBytes ||
                !new[] { ".jpg", ".jpeg", ".png" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            byte[] bytes;
            using (MemoryStream stream = new MemoryStream())
            {
                photo.InputStream.CopyTo(stream);
                bytes = stream.ToArray();
            }

            bool isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            bool isPng = bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
            if (!isJpeg && !isPng)
            {
                return false;
            }

            string folderPath = Server.MapPath("~/Content/ProductImages");
            Directory.CreateDirectory(folderPath);
            string fileName = Guid.NewGuid().ToString("N") + extension.ToLowerInvariant();
            photo.SaveAs(Path.Combine(folderPath, fileName));
            productImagePath = "~/Content/ProductImages/" + fileName;
            return true;
        }

        private string ResolveProductImagePath(string storedImagePath)
        {
            if (string.IsNullOrWhiteSpace(storedImagePath))
            {
                return null;
            }

            string fileName = Path.GetFileName(storedImagePath.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            string contentFolder = Server.MapPath("~/Content/ProductImages");
            string contentFilePath = Path.Combine(contentFolder, fileName);
            if (!System.IO.File.Exists(contentFilePath))
            {
                string legacyFilePath = Server.MapPath("~/App_Data/ProductImages/" + fileName);
                if (!System.IO.File.Exists(legacyFilePath))
                {
                    return null;
                }

                Directory.CreateDirectory(contentFolder);
                System.IO.File.Copy(legacyFilePath, contentFilePath, true);
            }

            return "~/Content/ProductImages/" + fileName;
        }

        private int InsertProduct(AddProductViewModel model, string productImagePath)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"INSERT INTO dbo.Products
(ProductName, Category, Price, Quantity, QuantityDescription, Unit, ProductImage)
OUTPUT INSERTED.ProductId
VALUES (@ProductName, @Category, @Price, @Quantity, @QuantityDescription, @Unit, @ProductImage);", connection))
            {
                command.Parameters.Add("@ProductName", SqlDbType.NVarChar, 150).Value = model.ProductName.Trim();
                command.Parameters.Add("@Category", SqlDbType.NVarChar, 50).Value = model.Category;
                command.Parameters.Add("@Price", SqlDbType.Decimal).Value = model.Price.Value;
                command.Parameters["@Price"].Precision = 10;
                command.Parameters["@Price"].Scale = 2;
                command.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = model.Quantity.Value;
                command.Parameters["@Quantity"].Precision = 10;
                command.Parameters["@Quantity"].Scale = 2;
                command.Parameters.Add("@QuantityDescription", SqlDbType.NVarChar, 50).Value = (object)model.QuantityDescription ?? DBNull.Value;
                command.Parameters.Add("@Unit", SqlDbType.NVarChar, 20).Value = model.Unit;
                command.Parameters.Add("@ProductImage", SqlDbType.NVarChar, 500).Value = (object)productImagePath ?? DBNull.Value;
                connection.Open();
                return (int)command.ExecuteScalar();
            }
        }

        private string GetDatabaseErrorMessage(SqlException exception)
        {
            if (HttpContext.IsDebuggingEnabled)
            {
                return "Database error: " + exception.ToString();
            }

            return "The product could not be saved. Please check the database configuration.";
        }

        private string GetConnectionString()
        {
            ConnectionStringSettings settings = ConfigurationManager.ConnectionStrings[ConnectionStringName];
            return settings.ConnectionString;
        }

        private bool IsAdmin()
        {
            return string.Equals(Session["Role"] as string, "Admin", StringComparison.Ordinal);
        }
    }
}