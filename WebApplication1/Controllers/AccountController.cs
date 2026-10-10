using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Globalization;
using System.Diagnostics;
using System.Web.Mvc;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class AccountController : Controller
    {
        private const string AdminUsername = "admin";
        private const string AdminPassword = "admin@123";
        private const string ConnectionStringName = "PmpSupermarketDb";
        private const int HashIterations = 100000;
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private static readonly string[] ProductCatalogCategories =
        {
            "Groceries", "Beverages", "Snacks", "Dairy and Eggs", "Fruits",
            "Vegetables", "Personal Care", "Household", "Bakery"
        };
        private static readonly string[] AllowedUnits = { "kg", "g", "litre", "packet", "piece" };

        [HttpGet]
        public ActionResult Login()
        {
            if (IsLoggedIn())
            {
                return RedirectToDashboard();
            }

            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (string.Equals(model.Username, AdminUsername, StringComparison.OrdinalIgnoreCase) && model.Password == AdminPassword)
            {
                SetSession(0, AdminUsername, "Admin");
                return RedirectToAction("AdminDashboard");
            }

            try
            {
                User user = FindUser(model.Username);
                if (user != null && VerifyPassword(model.Password, user.PasswordHash))
                {
                    SetSession(user.UserId, user.Username, user.Role);
                    return RedirectToAction("UserDashboard");
                }
            }
            catch (SqlException)
            {
                ModelState.AddModelError("", "The database is unavailable. Check the SQL Server connection configured for this application.");
                return View(model);
            }

            ModelState.AddModelError("", "Invalid username or password.");
            return View(model);
        }

        [HttpGet]
        public ActionResult Register()
        {
            if (IsAdmin())
            {
                return RedirectToAction("AdminDashboard");
            }

            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(RegisterViewModel model)
        {
            if (string.Equals(model.Username, AdminUsername, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("Username", "The admin account cannot be registered.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                if (FindUser(model.Username) != null)
                {
                    ModelState.AddModelError("Username", "That username is already registered.");
                    return View(model);
                }

                string passwordHash = HashPassword(model.Password);
                using (SqlConnection connection = new SqlConnection(GetConnectionString()))
                using (SqlCommand command = new SqlCommand(@"INSERT INTO dbo.Users (Username, PasswordHash, Role, CreatedDate)
VALUES (@Username, @PasswordHash, @Role, @CreatedDate);", connection))
                {
                    command.Parameters.Add("@Username", SqlDbType.NVarChar, 50).Value = model.Username;
                    command.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 256).Value = passwordHash;
                    command.Parameters.Add("@Role", SqlDbType.NVarChar, 20).Value = "User";
                    command.Parameters.Add("@CreatedDate", SqlDbType.DateTime2).Value = DateTime.UtcNow;
                    connection.Open();
                    command.ExecuteNonQuery();
                }

                TempData["RegistrationMessage"] = "Registration complete. Sign in with your new account.";
                return RedirectToAction("Login");
            }
            catch (SqlException exception) when (exception.Number == 2601 || exception.Number == 2627)
            {
                ModelState.AddModelError("Username", "That username is already registered.");
                return View(model);
            }
            catch (SqlException)
            {
                ModelState.AddModelError("", "The database is unavailable. Check the SQL Server connection configured for this application.");
                return View(model);
            }
        }

        [HttpGet]
        public ActionResult AdminDashboard()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login");
            }

            return View();
        }

        [HttpGet]
        public ActionResult AdminBillsHistory()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login");
            }

            return View(GetAdminBillHistory());
        }

        [HttpGet]
        public ActionResult AdminBillDetails(int billId)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login");
            }

            AdminBillDetailsViewModel bill = GetAdminBillDetails(billId);
            if (bill == null)
            {
                return RedirectToAction("AdminBillsHistory");
            }

            return View(bill);
        }

        [HttpGet]
        public JsonResult ProductDetails(int productId)
        {
            if (!IsUser())
            {
                return Json(null, JsonRequestBehavior.AllowGet);
            }

            return Json(GetProduct(productId), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult AddToCart(int productId, decimal neededQuantity, string unit)
        {
            if (!IsUser())
            {
                return Json(new { success = false, message = "Please sign in again." });
            }

            if (neededQuantity <= 0)
            {
                return Json(new { success = false, message = "Needed quantity must be greater than 0." });
            }

            unit = (unit ?? string.Empty).Trim().ToLowerInvariant();
            if (!AllowedUnits.Contains(unit, StringComparer.Ordinal))
            {
                return Json(new { success = false, message = "Please select a valid unit." });
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(GetConnectionString()))
                {
                    connection.Open();
                    using (SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        try
                        {
                            decimal available = GetAvailableQuantity(connection, transaction, productId);
                            decimal existingQuantity = GetExistingCartQuantity(connection, transaction, productId);
                            if (available < existingQuantity + neededQuantity)
                            {
                                transaction.Rollback();
                                return Json(new { success = false, message = "Only " + Math.Max(0, available - existingQuantity).ToString("0.##") + " quantity available." });
                            }

                            using (SqlCommand command = new SqlCommand(@"UPDATE dbo.CartItems
SET NeededQuantity = NeededQuantity + @NeededQuantity, Unit = @Unit
WHERE UserId = @UserId AND ProductId = @ProductId;
IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.CartItems (UserId, ProductId, NeededQuantity, Unit)
    VALUES (@UserId, @ProductId, @NeededQuantity, @Unit);
END", connection, transaction))
                            {
                                AddCartParameters(command, productId, neededQuantity, unit);
                                command.ExecuteNonQuery();
                            }

                            transaction.Commit();
                            try
                            {
                                ClearConfirmedCartItem(productId);
                            }
                            catch (SqlException)
                            {
                                Session["ConfirmedCartItems"] = null;
                            }
                            return Json(new { success = true });
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (SqlException exception)
            {
                return Json(new { success = false, message = GetCartDatabaseErrorMessage(exception) });
            }
        }

        [HttpGet]
        public ActionResult AddedCartProducts(string message = null)
        {
            if (!IsUser())
            {
                return RedirectToAction("Login");
            }

            return View(new CartViewModel
            {
                Username = GetLoggedInUsername(),
                Items = GetCartItems(),
                ErrorMessage = TempData["CartError"] as string,
                SuccessMessage = message ?? TempData["CartSuccess"] as string
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateCartItem(int cartItemId, decimal neededQuantity, string unit)
        {
            if (!IsUser())
            {
                return RedirectToAction("Login");
            }

            unit = (unit ?? string.Empty).Trim().ToLowerInvariant();
            if (neededQuantity <= 0 || !AllowedUnits.Contains(unit, StringComparer.Ordinal))
            {
                TempData["CartError"] = "Needed quantity must be greater than 0 and the unit must be valid.";
                return RedirectToAction("AddedCartProducts");
            }

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            {
                connection.Open();
                decimal available;
                int productId;
                using (SqlCommand lookup = new SqlCommand(@"SELECT c.ProductId, p.Quantity
FROM dbo.CartItems c INNER JOIN dbo.Products p ON p.ProductId = c.ProductId
WHERE c.CartItemId = @CartItemId AND c.UserId = @UserId;", connection))
                {
                    lookup.Parameters.Add("@CartItemId", SqlDbType.Int).Value = cartItemId;
                    lookup.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                    using (SqlDataReader reader = lookup.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        if (!reader.Read())
                        {
                            TempData["CartError"] = "The cart item could not be found.";
                            return RedirectToAction("AddedCartProducts");
                        }

                        productId = reader.GetInt32(0);
                        available = reader.GetDecimal(1);
                    }
                }

                if (neededQuantity > available)
                {
                    TempData["CartError"] = "Only " + available.ToString("0.##") + " quantity available.";
                    return RedirectToAction("AddedCartProducts");
                }

                using (SqlCommand update = new SqlCommand(@"UPDATE dbo.CartItems
SET NeededQuantity = @NeededQuantity, Unit = @Unit
WHERE CartItemId = @CartItemId AND UserId = @UserId;", connection))
                {
                    update.Parameters.Add("@NeededQuantity", SqlDbType.Decimal).Value = neededQuantity;
                    update.Parameters["@NeededQuantity"].Precision = 10;
                    update.Parameters["@NeededQuantity"].Scale = 2;
                    update.Parameters.Add("@Unit", SqlDbType.NVarChar, 20).Value = unit;
                    update.Parameters.Add("@CartItemId", SqlDbType.Int).Value = cartItemId;
                    update.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                    if (update.ExecuteNonQuery() != 1)
                    {
                        TempData["CartError"] = "The cart item could not be updated.";
                        return RedirectToAction("AddedCartProducts");
                    }
                }

                ClearConfirmedCartItem(productId);
            }

            Session["BillingBillId"] = null;
            TempData["CartSuccess"] = "Cart item updated.";
            return RedirectToAction("AddedCartProducts");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteCartItem(int cartItemId)
        {
            if (!IsUser())
            {
                return RedirectToAction("Login");
            }

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand("DELETE FROM dbo.CartItems WHERE CartItemId = @CartItemId AND UserId = @UserId;", connection))
            {
                command.Parameters.Add("@CartItemId", SqlDbType.Int).Value = cartItemId;
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                connection.Open();
                command.ExecuteNonQuery();
            }

            TempData["CartSuccess"] = "Cart item deleted.";
            return RedirectToAction("AddedCartProducts");
        }

        [HttpGet]
        public JsonResult CartPreview()
        {
            if (!IsUser())
            {
                return Json(new { success = false, message = "Please sign in again." }, JsonRequestBehavior.AllowGet);
            }

            return Json(new
            {
                success = true,
                items = GetCartItems().Select(item => new
                {
                    item.ProductName,
                    item.NeededQuantity,
                    item.Unit
                }).ToList()
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ConfirmOrder()
        {
            if (!IsUser())
            {
                return RedirectToAction("Login");
            }

            string error;
            if (!TryConfirmOrder(out error))
            {
                TempData["CartError"] = error;
                return RedirectToAction("AddedCartProducts");
            }

            Session["BillingBillId"] = null;
            return RedirectToAction("YourOrder");
        }

        [HttpGet]
        public ActionResult YourOrder()
        {
            if (!IsUser())
            {
                return RedirectToAction("Login");
            }

            return View(new YourOrderViewModel
            {
                Username = GetLoggedInUsername(),
                Items = GetOrderItems()
            });
        }

        [HttpGet]
        public ActionResult BillingSection()
        {
            if (!IsUser())
            {
                return RedirectToAction("Login");
            }

            BillingViewModel billing = CreateOrLoadBill();
            if (billing == null)
            {
                TempData["CartError"] = "There are no confirmed products to bill.";
                return RedirectToAction("AddedCartProducts");
            }

            return View(billing);
        }

        [HttpGet]
        public ActionResult AdminFeature(string feature)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("Login");
            }

            ViewBag.Feature = string.IsNullOrWhiteSpace(feature) ? "Admin feature" : feature;
            return View();
        }

        [HttpGet]
        public ActionResult UserDashboard(string billNumber, string billDate, int? billMonth, int? billYear, int? viewBillId)
        {
            if (!IsUser())
            {
                return RedirectToAction("Login");
            }

            try
            {
                return View(new UserDashboardViewModel
                {
                    Username = GetLoggedInUsername(),
                    Categories = ProductCatalogCategories,
                    Products = GetProducts()
                });
            }
            catch (SqlException)
            {
                TempData["LoginError"] = "The user account was found, but the product database could not be loaded.";
                Session.Clear();
                return RedirectToAction("Login");
            }
        }

        [HttpGet]
        public ActionResult BillTransaction(string billNumber, string billDate, int? billMonth, int? billYear, int? viewBillId)
        {
            if (!IsUser())
            {
                return RedirectToAction("Login");
            }

            try
            {
                return View(new UserDashboardViewModel
                {
                    Username = GetLoggedInUsername(),
                    Bills = GetBillHistory(billNumber, billDate, billMonth, billYear),
                    BillNumberFilter = billNumber,
                    BillDateFilter = billDate,
                    BillMonthFilter = billMonth,
                    BillYearFilter = billYear,
                    SelectedBill = viewBillId.HasValue ? GetBillHistoryDetail(viewBillId.Value) : null
                });
            }
            catch (SqlException)
            {
                TempData["LoginError"] = "The bill transaction data could not be loaded.";
                return RedirectToAction("UserDashboard");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            Session.Clear();
            return RedirectToAction("Login");
        }

        private User FindUser(string username)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT TOP 1 UserId, Username, PasswordHash, Role, CreatedDate
FROM dbo.Users WHERE Username = @Username;", connection))
            {
                command.Parameters.Add("@Username", SqlDbType.NVarChar, 50).Value = username;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleRow))
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return new User
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        PasswordHash = reader.GetString(2),
                        Role = reader.GetString(3),
                        CreatedDate = reader.GetDateTime(4)
                    };
                }
            }
        }

        private Product[] GetProducts()
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT ProductId, ProductName, Category, Price, Quantity, QuantityDescription, Unit, ProductImage, CreatedDate
FROM dbo.Products ORDER BY Category, ProductName, ProductId;", connection))
            {
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    List<Product> products = new List<Product>();
                    while (reader.Read())
                    {
                        Product product = ReadProduct(reader);
                        product.ProductImage = ResolveProductImagePath(product.ProductImage);
                        products.Add(product);
                    }

                    return products.ToArray();
                }
            }
        }

        private Product GetProduct(int productId)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT ProductId, ProductName, Category, Price, Quantity, QuantityDescription, Unit, ProductImage, CreatedDate
FROM dbo.Products WHERE ProductId = @ProductId;", connection))
            {
                command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleRow))
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    Product product = ReadProduct(reader);
                    product.ProductImage = ResolveProductImagePath(product.ProductImage);
                    return product;
                }
            }
        }

        private Product ReadProduct(SqlDataReader reader)
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
                CreatedDate = reader.GetDateTime(8)
            };
        }

        private List<CartItemViewModel> GetCartItems()
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT c.CartItemId, c.ProductId, p.ProductName,
p.ProductImage, p.QuantityDescription, c.NeededQuantity, c.Unit, p.Quantity
FROM dbo.CartItems c INNER JOIN dbo.Products p ON p.ProductId = c.ProductId
WHERE c.UserId = @UserId ORDER BY c.CreatedDate, c.CartItemId;", connection))
            {
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    List<CartItemViewModel> items = new List<CartItemViewModel>();
                    while (reader.Read())
                    {
                        items.Add(new CartItemViewModel
                        {
                            CartItemId = reader.GetInt32(0),
                            ProductId = reader.GetInt32(1),
                            ProductName = reader.GetString(2),
                            ProductImage = ResolveProductImagePath(reader.IsDBNull(3) ? null : reader.GetString(3)),
                            QuantityDescription = reader.IsDBNull(4) ? null : reader.GetString(4),
                            NeededQuantity = reader.GetDecimal(5),
                            Unit = reader.GetString(6),
                            CurrentQuantity = reader.GetDecimal(7)
                        });
                    }

                    return items;
                }
            }
        }

        private List<OrderItemViewModel> GetOrderItems()
        {
            HashSet<int> confirmedItems = GetConfirmedCartItems();
            if (!confirmedItems.Any())
            {
                return new List<OrderItemViewModel>();
            }

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT c.CartItemId, c.ProductId, p.ProductName,
c.NeededQuantity, c.Unit, p.QuantityDescription
FROM dbo.CartItems c INNER JOIN dbo.Products p ON p.ProductId = c.ProductId
WHERE c.UserId = @UserId ORDER BY c.CreatedDate, c.CartItemId;", connection))
            {
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    List<OrderItemViewModel> items = new List<OrderItemViewModel>();
                    while (reader.Read())
                    {
                        if (!confirmedItems.Contains(reader.GetInt32(0)))
                        {
                            continue;
                        }

                        items.Add(new OrderItemViewModel
                        {
                            ProductId = reader.GetInt32(1),
                            ProductName = reader.GetString(2),
                            NeededQuantity = reader.GetDecimal(3),
                            Unit = reader.GetString(4),
                            QuantityDescription = reader.IsDBNull(5) ? null : reader.GetString(5)
                        });
                    }

                    return items;
                }
            }
        }

        private BillingViewModel CreateOrLoadBill()
        {
            int? existingBillId = Session["BillingBillId"] is int ? (int?)Session["BillingBillId"] : null;
            if (existingBillId.HasValue)
            {
                BillingViewModel existingBill = GetBill(existingBillId.Value);
                if (existingBill != null)
                {
                    return existingBill;
                }
            }

            HashSet<int> confirmedItems = GetConfirmedCartItems();
            if (!confirmedItems.Any())
            {
                return null;
            }

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            {
                connection.Open();
                using (SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        List<BillingItemViewModel> items = GetBillingItems(connection, transaction, confirmedItems);
                        if (!items.Any())
                        {
                            transaction.Rollback();
                            return null;
                        }

                        decimal totalAmount = items.Sum(item => item.Amount);
                        int billId;
                        using (SqlCommand insertBill = new SqlCommand(@"INSERT INTO dbo.Bills (UserId, TotalAmount)
OUTPUT INSERTED.BillId
VALUES (@UserId, @TotalAmount);", connection, transaction))
                        {
                            insertBill.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                            insertBill.Parameters.Add("@TotalAmount", SqlDbType.Decimal).Value = totalAmount;
                            insertBill.Parameters["@TotalAmount"].Precision = 12;
                            insertBill.Parameters["@TotalAmount"].Scale = 2;
                            billId = Convert.ToInt32(insertBill.ExecuteScalar());
                        }

                        using (SqlCommand numberBill = new SqlCommand("SELECT BillNumber FROM dbo.Bills WHERE BillId = @BillId;", connection, transaction))
                        {
                            numberBill.Parameters.Add("@BillId", SqlDbType.Int).Value = billId;
                            string billNumber = Convert.ToString(numberBill.ExecuteScalar());
                            using (SqlCommand insertItem = new SqlCommand(@"INSERT INTO dbo.BillItems
(BillId, ProductId, ProductName, QuantityPurchased, Unit, Price, Amount)
VALUES (@BillId, @ProductId, @ProductName, @QuantityPurchased, @Unit, @Price, @Amount);", connection, transaction))
                            {
                                insertItem.Parameters.Add("@BillId", SqlDbType.Int);
                                insertItem.Parameters.Add("@ProductId", SqlDbType.Int);
                                insertItem.Parameters.Add("@ProductName", SqlDbType.NVarChar, 150);
                                insertItem.Parameters.Add("@QuantityPurchased", SqlDbType.Decimal).Precision = 10;
                                insertItem.Parameters["@QuantityPurchased"].Scale = 2;
                                insertItem.Parameters.Add("@Unit", SqlDbType.NVarChar, 20);
                                insertItem.Parameters.Add("@Price", SqlDbType.Decimal).Precision = 10;
                                insertItem.Parameters["@Price"].Scale = 2;
                                insertItem.Parameters.Add("@Amount", SqlDbType.Decimal).Precision = 12;
                                insertItem.Parameters["@Amount"].Scale = 2;

                                foreach (BillingItemViewModel item in items)
                                {
                                    insertItem.Parameters["@BillId"].Value = billId;
                                    insertItem.Parameters["@ProductId"].Value = item.ProductId;
                                    insertItem.Parameters["@ProductName"].Value = item.ProductName;
                                    insertItem.Parameters["@QuantityPurchased"].Value = item.QuantityPurchased;
                                    insertItem.Parameters["@Unit"].Value = item.Unit;
                                    insertItem.Parameters["@Price"].Value = item.Price;
                                    insertItem.Parameters["@Amount"].Value = item.Amount;
                                    insertItem.ExecuteNonQuery();
                                }
                            }

                            DeleteCompletedCartItems(connection, transaction, confirmedItems);
                            transaction.Commit();
                            InsertSalesLogSafely(connection, items);
                            Session["BillingBillId"] = billId;
                            Session["ConfirmedCartItems"] = null;
                            return new BillingViewModel
                            {
                                BillNumber = billNumber,
                                Items = items,
                                TotalAmount = totalAmount
                            };
                        }
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private void InsertSalesLogSafely(SqlConnection connection, IEnumerable<BillingItemViewModel> items)
        {
            try
            {
                using (SqlCommand insertSales = new SqlCommand(@"INSERT INTO dbo.SalesLog
(ProductId, SaleDate, Amount, Quantity, CreatedTime)
VALUES (@ProductId, CONVERT(date, GETDATE()), @Amount, @Quantity, GETDATE());", connection))
                {
                    insertSales.Parameters.Add("@ProductId", SqlDbType.Int);
                    insertSales.Parameters.Add("@Amount", SqlDbType.Decimal).Precision = 12;
                    insertSales.Parameters["@Amount"].Scale = 2;
                    insertSales.Parameters.Add("@Quantity", SqlDbType.Decimal).Precision = 10;
                    insertSales.Parameters["@Quantity"].Scale = 2;

                    foreach (BillingItemViewModel item in items)
                    {
                        try
                        {
                            insertSales.Parameters["@ProductId"].Value = item.ProductId;
                            insertSales.Parameters["@Amount"].Value = item.Amount;
                            insertSales.Parameters["@Quantity"].Value = item.QuantityPurchased;
                            insertSales.ExecuteNonQuery();
                        }
                        catch (Exception exception)
                        {
                            Trace.TraceError("SalesLog insertion failed for ProductId {0} after bill completion: {1}", item.ProductId, exception);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Trace.TraceError("SalesLog insertion failed after bill completion: {0}", exception);
            }
        }

        private BillingViewModel GetBill(int billId)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT b.BillNumber, b.TotalAmount,
i.ProductId, i.ProductName, i.QuantityPurchased, i.Unit, i.Price, i.Amount
FROM dbo.Bills b INNER JOIN dbo.BillItems i ON i.BillId = b.BillId
WHERE b.BillId = @BillId AND b.UserId = @UserId ORDER BY i.BillItemId;", connection))
            {
                command.Parameters.Add("@BillId", SqlDbType.Int).Value = billId;
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    BillingViewModel billing = null;
                    List<BillingItemViewModel> items = new List<BillingItemViewModel>();
                    while (reader.Read())
                    {
                        if (billing == null)
                        {
                            billing = new BillingViewModel
                            {
                                BillNumber = reader.IsDBNull(0) ? null : reader.GetString(0),
                                TotalAmount = reader.GetDecimal(1)
                            };
                        }

                        items.Add(new BillingItemViewModel
                        {
                            ProductId = reader.GetInt32(2),
                            ProductName = reader.GetString(3),
                            QuantityPurchased = reader.GetDecimal(4),
                            Unit = reader.GetString(5),
                            Price = reader.GetDecimal(6),
                            Amount = reader.GetDecimal(7)
                        });
                    }

                    if (billing != null)
                    {
                        billing.Items = items;
                    }

                    return billing;
                }
            }
        }

        private List<BillHistoryViewModel> GetBillHistory(string billNumber, string billDate, int? billMonth, int? billYear)
        {
            DateTime parsedDate;
            bool hasDate = DateTime.TryParseExact(billDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate);
            const string query = @"SELECT b.BillId, b.BillNumber, b.BillDate, b.TotalAmount
FROM dbo.Bills b
WHERE b.UserId = @UserId
  AND (@BillNumber = N'' OR b.BillNumber LIKE N'%' + @BillNumber + N'%')
  AND (@BillDate IS NULL OR (b.BillDate >= @BillDate AND b.BillDate < DATEADD(day, 1, @BillDate)))
  AND (@BillMonth IS NULL OR MONTH(b.BillDate) = @BillMonth)
  AND (@BillYear IS NULL OR YEAR(b.BillDate) = @BillYear)
ORDER BY b.BillDate DESC, b.BillId DESC;";

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                command.Parameters.Add("@BillNumber", SqlDbType.NVarChar, 50).Value = (billNumber ?? string.Empty).Trim();
                SqlParameter dateParameter = command.Parameters.Add("@BillDate", SqlDbType.Date);
                dateParameter.Value = hasDate ? parsedDate.Date : (object)DBNull.Value;
                command.Parameters.Add("@BillMonth", SqlDbType.Int).Value = billMonth.HasValue && billMonth >= 1 && billMonth <= 12 ? (object)billMonth.Value : DBNull.Value;
                command.Parameters.Add("@BillYear", SqlDbType.Int).Value = billYear == 2026 || billYear == 2027 ? (object)billYear.Value : DBNull.Value;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    List<BillHistoryViewModel> bills = new List<BillHistoryViewModel>();
                    while (reader.Read())
                    {
                        bills.Add(new BillHistoryViewModel
                        {
                            BillId = reader.GetInt32(0),
                            BillNumber = reader.GetString(1),
                            BillDate = reader.GetDateTime(2),
                            TotalAmount = reader.GetDecimal(3)
                        });
                    }

                    return bills;
                }
            }
        }

        private List<AdminBillHistoryViewModel> GetAdminBillHistory()
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT b.BillId, u.UserId, u.Username, b.TotalAmount
FROM dbo.Bills b INNER JOIN dbo.Users u ON u.UserId = b.UserId
ORDER BY b.BillId DESC;", connection))
            {
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    List<AdminBillHistoryViewModel> bills = new List<AdminBillHistoryViewModel>();
                    while (reader.Read())
                    {
                        bills.Add(new AdminBillHistoryViewModel
                        {
                            BillId = reader.GetInt32(0),
                            UserId = reader.GetInt32(1),
                            Username = reader.GetString(2),
                            TotalAmount = reader.GetDecimal(3)
                        });
                    }

                    return bills;
                }
            }
        }

        private AdminBillDetailsViewModel GetAdminBillDetails(int billId)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT u.UserId, u.Username, b.BillId, b.BillNumber, b.BillDate,
b.TotalAmount, i.ProductId, i.ProductName, i.QuantityPurchased, i.Unit, i.Price, i.Amount
FROM dbo.Users u
INNER JOIN dbo.Bills b ON b.UserId = u.UserId
INNER JOIN dbo.BillItems i ON i.BillId = b.BillId
WHERE b.BillId = @BillId
ORDER BY i.BillItemId;", connection))
            {
                command.Parameters.Add("@BillId", SqlDbType.Int).Value = billId;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    AdminBillDetailsViewModel bill = null;
                    List<BillingItemViewModel> items = new List<BillingItemViewModel>();
                    while (reader.Read())
                    {
                        if (bill == null)
                        {
                            bill = new AdminBillDetailsViewModel
                            {
                                UserId = reader.GetInt32(0),
                                Username = reader.GetString(1),
                                BillId = reader.GetInt32(2),
                                BillNumber = reader.GetString(3),
                                BillDate = reader.GetDateTime(4),
                                TotalAmount = reader.GetDecimal(5)
                            };
                        }

                        items.Add(new BillingItemViewModel
                        {
                            ProductId = reader.GetInt32(6),
                            ProductName = reader.GetString(7),
                            QuantityPurchased = reader.GetDecimal(8),
                            Unit = reader.GetString(9),
                            Price = reader.GetDecimal(10),
                            Amount = reader.GetDecimal(11)
                        });
                    }

                    if (bill != null)
                    {
                        bill.Items = items;
                    }

                    return bill;
                }
            }
        }

        private BillingHistoryDetailViewModel GetBillHistoryDetail(int billId)
        {
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand(@"SELECT b.BillId, b.BillNumber, b.BillDate, b.TotalAmount,
i.ProductId, i.ProductName, i.QuantityPurchased, i.Unit, i.Price, i.Amount
FROM dbo.Bills b INNER JOIN dbo.BillItems i ON i.BillId = b.BillId
WHERE b.BillId = @BillId AND b.UserId = @UserId ORDER BY i.BillItemId;", connection))
            {
                command.Parameters.Add("@BillId", SqlDbType.Int).Value = billId;
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    BillingHistoryDetailViewModel detail = null;
                    List<BillingItemViewModel> items = new List<BillingItemViewModel>();
                    while (reader.Read())
                    {
                        if (detail == null)
                        {
                            detail = new BillingHistoryDetailViewModel
                            {
                                BillId = reader.GetInt32(0),
                                BillNumber = reader.GetString(1),
                                BillDate = reader.GetDateTime(2),
                                TotalAmount = reader.GetDecimal(3)
                            };
                        }

                        items.Add(new BillingItemViewModel
                        {
                            ProductId = reader.GetInt32(4),
                            ProductName = reader.GetString(5),
                            QuantityPurchased = reader.GetDecimal(6),
                            Unit = reader.GetString(7),
                            Price = reader.GetDecimal(8),
                            Amount = reader.GetDecimal(9)
                        });
                    }

                    if (detail != null)
                    {
                        detail.Items = items;
                    }

                    return detail;
                }
            }
        }

        private void DeleteCompletedCartItems(SqlConnection connection, SqlTransaction transaction, HashSet<int> confirmedItems)
        {
            string[] parameterNames = confirmedItems.Select((id, index) => "@CompletedCartItemId" + index).ToArray();
            using (SqlCommand command = new SqlCommand(@"DELETE FROM dbo.CartItems
WHERE UserId = @UserId AND CartItemId IN (" + string.Join(",", parameterNames) + ");", connection, transaction))
            {
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                int parameterIndex = 0;
                foreach (int cartItemId in confirmedItems)
                {
                    command.Parameters.Add(parameterNames[parameterIndex++], SqlDbType.Int).Value = cartItemId;
                }

                command.ExecuteNonQuery();
            }
        }

        private List<BillingItemViewModel> GetBillingItems(SqlConnection connection, SqlTransaction transaction, HashSet<int> confirmedItems)
        {
            List<BillingItemViewModel> items = new List<BillingItemViewModel>();
            string[] parameterNames = confirmedItems.Select((id, index) => "@CartItemId" + index).ToArray();
            string query = @"SELECT c.CartItemId, c.ProductId, p.ProductName, c.NeededQuantity, c.Unit, p.Price
FROM dbo.CartItems c INNER JOIN dbo.Products p ON p.ProductId = c.ProductId
WHERE c.UserId = @UserId AND c.CartItemId IN (" + string.Join(",", parameterNames) + @")
ORDER BY c.CreatedDate, c.CartItemId;";

            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                int parameterIndex = 0;
                foreach (int cartItemId in confirmedItems)
                {
                    command.Parameters.Add(parameterNames[parameterIndex++], SqlDbType.Int).Value = cartItemId;
                }

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        decimal quantity = reader.GetDecimal(3);
                        decimal price = reader.GetDecimal(5);
                        items.Add(new BillingItemViewModel
                        {
                            ProductId = reader.GetInt32(1),
                            ProductName = reader.GetString(2),
                            QuantityPurchased = quantity,
                            Unit = reader.GetString(4),
                            Price = price,
                            Amount = Math.Round(quantity * price, 2, MidpointRounding.AwayFromZero)
                        });
                    }
                }
            }

            return items;
        }

        private decimal GetAvailableQuantity(SqlConnection connection, SqlTransaction transaction, int productId)
        {
            using (SqlCommand command = new SqlCommand("SELECT Quantity FROM dbo.Products WITH (UPDLOCK, HOLDLOCK) WHERE ProductId = @ProductId;", connection, transaction))
            {
                command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToDecimal(value);
            }
        }

        private decimal GetExistingCartQuantity(SqlConnection connection, SqlTransaction transaction, int productId)
        {
            using (SqlCommand command = new SqlCommand("SELECT NeededQuantity FROM dbo.CartItems WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId AND ProductId = @ProductId;", connection, transaction))
            {
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToDecimal(value);
            }
        }

        private void AddCartParameters(SqlCommand command, int productId, decimal neededQuantity, string unit)
        {
            command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
            command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;
            command.Parameters.Add("@NeededQuantity", SqlDbType.Decimal).Value = neededQuantity;
            command.Parameters["@NeededQuantity"].Precision = 10;
            command.Parameters["@NeededQuantity"].Scale = 2;
            command.Parameters.Add("@Unit", SqlDbType.NVarChar, 20).Value = unit;
        }

        private string GetCartDatabaseErrorMessage(SqlException exception)
        {
            if (exception.Number == 547)
            {
                return "The selected product or user is no longer available. Refresh the page and try again.";
            }

            if (exception.Number == 2601 || exception.Number == 2627)
            {
                return "This product is already being added to your cart. Refresh the cart and try again.";
            }

            return HttpContext.IsDebuggingEnabled
                ? "Cart database error: " + exception.Message
                : "The item could not be added to the cart. Please try again.";
        }

        private bool TryConfirmOrder(out string error)
        {
            error = null;
            List<CartItemViewModel> items = GetCartItems();
            HashSet<int> confirmedItems = GetConfirmedCartItems();
            List<CartItemViewModel> pendingItems = items.Where(item => !confirmedItems.Contains(item.CartItemId)).ToList();
            if (!pendingItems.Any())
            {
                if (items.Any() && items.All(item => confirmedItems.Contains(item.CartItemId)))
                {
                    return true;
                }

                error = "There are no new cart items to confirm.";
                return false;
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(GetConnectionString()))
                {
                    connection.Open();
                    using (SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        try
                        {
                            foreach (CartItemViewModel item in pendingItems)
                            {
                                decimal available = GetAvailableQuantity(connection, transaction, item.ProductId);
                                if (item.NeededQuantity > available)
                                {
                                    transaction.Rollback();
                                    error = "Only " + available.ToString("0.##") + " quantity available for " + item.ProductName + ".";
                                    return false;
                                }

                                using (SqlCommand update = new SqlCommand(@"UPDATE dbo.Products
SET Quantity = Quantity - @NeededQuantity
WHERE ProductId = @ProductId AND Quantity >= @NeededQuantity;", connection, transaction))
                                {
                                    update.Parameters.Add("@NeededQuantity", SqlDbType.Decimal).Value = item.NeededQuantity;
                                    update.Parameters["@NeededQuantity"].Precision = 10;
                                    update.Parameters["@NeededQuantity"].Scale = 2;
                                    update.Parameters.Add("@ProductId", SqlDbType.Int).Value = item.ProductId;
                                    if (update.ExecuteNonQuery() != 1)
                                    {
                                        transaction.Rollback();
                                        error = "The stock changed while confirming " + item.ProductName + ". Please try again.";
                                        return false;
                                    }
                                }
                            }

                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (SqlException exception)
            {
                error = GetCartDatabaseErrorMessage(exception);
                return false;
            }

            foreach (CartItemViewModel item in pendingItems)
            {
                confirmedItems.Add(item.CartItemId);
            }
            Session["ConfirmedCartItems"] = confirmedItems.ToArray();
            return true;
        }

        private HashSet<int> GetConfirmedCartItems()
        {
            int[] itemIds = Session["ConfirmedCartItems"] as int[];
            return itemIds == null ? new HashSet<int>() : new HashSet<int>(itemIds);
        }

        private void ClearConfirmedCartItem(int productId)
        {
            List<int> confirmed = GetConfirmedCartItems().ToList();
            if (!confirmed.Any())
            {
                return;
            }

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand("SELECT CartItemId FROM dbo.CartItems WHERE UserId = @UserId AND ProductId = @ProductId;", connection))
            {
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = GetUserId();
                command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        confirmed.Remove(reader.GetInt32(0));
                    }
                }
            }

            Session["ConfirmedCartItems"] = confirmed.ToArray();
        }

        private string GetLoggedInUsername()
        {
            int userId = GetUserId();
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            using (SqlCommand command = new SqlCommand("SELECT Username FROM dbo.Users WHERE UserId = @UserId;", connection))
            {
                command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                connection.Open();
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? (Session["Username"] as string) : Convert.ToString(value);
            }
        }

        private int GetUserId()
        {
            return Session["UserId"] is int ? (int)Session["UserId"] : 0;
        }

        private string ResolveProductImagePath(string storedImagePath)
        {
            if (string.IsNullOrWhiteSpace(storedImagePath))
            {
                return null;
            }

            string fileName = Path.GetFileName(storedImagePath.Replace('\\', '/'));
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

        private static string HashPassword(string password)
        {
            byte[] salt = new byte[SaltSize];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(salt);
            }

            using (Rfc2898DeriveBytes deriveBytes = new Rfc2898DeriveBytes(password, salt, HashIterations))
            {
                byte[] hash = deriveBytes.GetBytes(HashSize);
                return string.Format("{0}.{1}.{2}", HashIterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
            }
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            string[] parts = storedHash.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            int iterations;
            if (!int.TryParse(parts[0], out iterations))
            {
                return false;
            }

            byte[] salt;
            byte[] expectedHash;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expectedHash = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            using (Rfc2898DeriveBytes deriveBytes = new Rfc2898DeriveBytes(password, salt, iterations))
            {
                byte[] actualHash = deriveBytes.GetBytes(expectedHash.Length);
                return FixedTimeEquals(actualHash, expectedHash);
            }
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private string GetConnectionString()
        {
            ConnectionStringSettings settings = ConfigurationManager.ConnectionStrings[ConnectionStringName];
            if (settings == null || string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                throw new ConfigurationErrorsException("The database connection string is missing.");
            }

            return settings.ConnectionString;
        }

        private void SetSession(int userId, string username, string role)
        {
            Session["UserId"] = userId;
            Session["Username"] = username;
            Session["Role"] = role;
        }

        private bool IsLoggedIn()
        {
            return Session["Username"] != null && Session["Role"] != null;
        }

        private bool IsAdmin()
        {
            return string.Equals(Session["Role"] as string, "Admin", StringComparison.Ordinal);
        }

        private bool IsUser()
        {
            return string.Equals(Session["Role"] as string, "User", StringComparison.Ordinal);
        }

        private ActionResult RedirectToDashboard()
        {
            return IsAdmin() ? RedirectToAction("AdminDashboard") : RedirectToAction("UserDashboard");
        }
    }
}
