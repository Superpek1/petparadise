using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using PetParadise.Models;

namespace PetParadise.Controllers;

public class AdminController : Controller
{
    private readonly Csi402dbContext _db;

    public AdminController(Csi402dbContext db)
    {
        _db = db;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var role = HttpContext.Session.GetString("Role");
        string[] staffRoles = { "Admin", "Staff", "Warehouse", "Delivery" };

        if (string.IsNullOrEmpty(role) || !staffRoles.Contains(role))
        {
            context.Result = RedirectToAction("Login", "Account");
        }
        base.OnActionExecuting(context);
    }

    private List<TopSellingProductSummary> GetTopSellingProducts(int limit = 10)
    {
        var products = _db.Products.Include(p => p.Category).ToList();
        var soldByProduct = _db.Orderitems
            .Include(oi => oi.Order)
            .Where(oi => oi.Order.Status != "Cancelled")
            .GroupBy(oi => oi.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                TotalQty = g.Sum(x => x.Quantity),
                TotalRev = g.Sum(x => x.Quantity * x.UnitPrice)
            })
            .ToList()
            .ToDictionary(x => x.ProductId, x => x);

        return products
            .Select(p => new TopSellingProductSummary
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                CategoryName = p.Category?.CategoryName ?? "-",
                UnitPrice = p.Price,
                TotalQuantitySold = soldByProduct.ContainsKey(p.ProductId) ? soldByProduct[p.ProductId].TotalQty : 0,
                TotalRevenue = soldByProduct.ContainsKey(p.ProductId) ? soldByProduct[p.ProductId].TotalRev : 0m,
                StockQuantity = p.StockQuantity
            })
            .OrderByDescending(x => x.TotalQuantitySold)
            .ThenByDescending(x => x.TotalRevenue)
            .Take(limit)
            .ToList();
    }

    // 1. แดชบอร์ดภาพรวม (เฉพาะ Admin)
    [HttpGet]
    public IActionResult Dashboard()
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        PromotionHelper.EnsureDefaultPromotions(_db);

        ViewBag.TotalSales = _db.Orders.Where(o => o.Status != "Cancelled").Sum(o => (decimal?)o.TotalAmount) ?? 0;
        ViewBag.TotalOrders = _db.Orders.Count();
        ViewBag.PendingOrders = _db.Orders.Count(o => o.Status == "Pending");
        ViewBag.TotalCustomers = _db.Users.Count(u => u.Role == "Customer");
        ViewBag.LowStockProducts = _db.Products.Where(p => p.StockQuantity <= 5).ToList();
        ViewBag.TopSellingProducts = GetTopSellingProducts(5);

        var notes = PromotionHelper.LoadSystemNotes();
        ViewBag.WarehouseIssues = notes.WarehouseIssues.Where(i => !i.IsResolved).OrderByDescending(i => i.ReportedAt).ToList();

        var recentOrders = _db.Orders.Include(o => o.User).OrderByDescending(o => o.OrderDate).Take(5).ToList();
        return View(recentOrders);
    }

    // 1.1 หน้ารายงาน (รายงานยอดขาย, รายงานจำนวนคำสั่งซื้อ, รายงานสินค้าขายดี, รายงานสินค้าคงเหลือ)
    [HttpGet]
    public IActionResult Reports()
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        var allOrders = _db.Orders
            .Include(o => o.User)
            .Include(o => o.Promotion)
            .OrderByDescending(o => o.OrderDate)
            .ToList();

        var validOrders = allOrders.Where(o => o.Status != "Cancelled").ToList();

        ViewBag.TotalSales = validOrders.Sum(o => o.TotalAmount);
        ViewBag.TotalSubtotal = validOrders.Sum(o => o.Subtotal);
        ViewBag.TotalDiscount = validOrders.Sum(o => o.Discount ?? 0m);
        ViewBag.TotalShipping = validOrders.Sum(o => o.ShippingFee ?? 0m);
        ViewBag.TotalVat = validOrders.Sum(o => o.Vat ?? 0m);

        ViewBag.TotalOrders = allOrders.Count;
        ViewBag.PendingCount = allOrders.Count(o => o.Status == "Pending");
        ViewBag.PaidCount = allOrders.Count(o => o.Status == "Paid");
        ViewBag.PreparingCount = allOrders.Count(o => o.Status == "Preparing");
        ViewBag.ShippedCount = allOrders.Count(o => o.Status == "Shipped");
        ViewBag.CompletedCount = allOrders.Count(o => o.Status == "Completed");
        ViewBag.CancelledCount = allOrders.Count(o => o.Status == "Cancelled");

        ViewBag.TopSellingProducts = GetTopSellingProducts(20);
        ViewBag.AllProductsInventory = _db.Products
            .Include(p => p.Category)
            .OrderBy(p => p.StockQuantity)
            .ToList();

        return View(allOrders);
    }

    // 2. จัดการคำสั่งซื้อและตรวจการชำระเงิน (Admin & Staff)
    [HttpGet]
    public IActionResult Orders(string? statusFilter)
    {
        var role = HttpContext.Session.GetString("Role");
        if (role != "Admin" && role != "Staff")
            return RedirectToAction("Login", "Account");

        var query = _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.Orderitems).ThenInclude(oi => oi.Product)
            .Include(o => o.Payments)
            .AsQueryable();

        if (!string.IsNullOrEmpty(statusFilter))
            query = query.Where(o => o.Status == statusFilter);

        ViewBag.StatusFilter = statusFilter;
        return View(query.OrderByDescending(o => o.OrderDate).ToList());
    }

    // 3. หน้างานคลังสินค้า (Admin, Warehouse & Staff)
    [HttpGet]
    public IActionResult Warehouse()
    {
        var role = HttpContext.Session.GetString("Role");
        if (role != "Admin" && role != "Warehouse" && role != "Staff")
            return RedirectToAction("Login", "Account");

        ViewBag.OrdersToPack = _db.Orders
            .Include(o => o.User)
            .Include(o => o.Orderitems).ThenInclude(oi => oi.Product)
            .Where(o => o.Status == "Paid" || o.Status == "Preparing")
            .OrderBy(o => o.OrderDate)
            .ToList();

        var notes = PromotionHelper.LoadSystemNotes();
        ViewBag.WarehouseIssues = notes.WarehouseIssues.OrderByDescending(i => i.ReportedAt).ToList();

        var products = _db.Products.Include(p => p.Category).OrderBy(p => p.StockQuantity).ToList();
        return View(products);
    }

    // 4. บันทึกรับสินค้าเข้าคลัง / ปรับปรุงสต็อก (Admin & Warehouse)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateStock(int productId, int addQuantity)
    {
        var product = _db.Products.Find(productId);
        if (product != null && addQuantity != 0)
        {
            product.StockQuantity = Math.Max(0, product.StockQuantity + addQuantity);
            _db.SaveChanges();
            TempData["Success"] = $"อัปเดตสต็อกสินค้า '{product.ProductName}' เป็น {product.StockQuantity} ชิ้นเรียบร้อยแล้ว";
        }
        return RedirectToAction(nameof(Warehouse));
    }

    // 4.1 แจ้งปัญหาสินค้าชำรุด / วันหมดอายุ / สินค้าไม่เพียงพอ ให้ผู้ดูแลระบบทราบ
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ReportWarehouseIssue(int productId, string issueType, string detail)
    {
        var product = _db.Products.Find(productId);
        if (product != null && !string.IsNullOrWhiteSpace(detail))
        {
            var notes = PromotionHelper.LoadSystemNotes();
            int nextId = notes.WarehouseIssues.Any() ? notes.WarehouseIssues.Max(i => i.Id) + 1 : 1;
            notes.WarehouseIssues.Add(new WarehouseIssueNote
            {
                Id = nextId,
                ProductId = productId,
                ProductName = product.ProductName,
                IssueType = issueType,
                Detail = detail.Trim(),
                ReportedBy = HttpContext.Session.GetString("Username") ?? "Warehouse",
                ReportedAt = DateTime.Now,
                IsResolved = false
            });
            PromotionHelper.SaveSystemNotes(notes);
            TempData["Success"] = $"บันทึกแจ้งปัญหา '{issueType}' ของสินค้า '{product.ProductName}' ให้ผู้ดูแลระบบเรียบร้อยแล้ว";
        }
        return RedirectToAction(nameof(Warehouse));
    }

    // 4.2 ปิดงานแจ้งปัญหาคลังสินค้า
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ResolveWarehouseIssue(int id, string? returnUrl)
    {
        var notes = PromotionHelper.LoadSystemNotes();
        var issue = notes.WarehouseIssues.FirstOrDefault(i => i.Id == id);
        if (issue != null)
        {
            issue.IsResolved = true;
            PromotionHelper.SaveSystemNotes(notes);
            TempData["Success"] = $"ดำเนินการแก้ไขรายการแจ้งปัญหา #{id} เรียบร้อยแล้ว";
        }
        if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Warehouse));
    }

    // 5. หน้างานพนักงานจัดส่งสินค้า (Admin & Delivery)
    [HttpGet]
    public IActionResult Delivery()
    {
        var role = HttpContext.Session.GetString("Role");
        if (role != "Admin" && role != "Delivery")
            return RedirectToAction("Login", "Account");

        var deliveryOrders = _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.Orderitems).ThenInclude(oi => oi.Product)
            .Where(o => o.Status == "Preparing" || o.Status == "Shipped" || o.Status == "Completed")
            .OrderByDescending(o => o.OrderDate)
            .ToList();

        var notes = PromotionHelper.LoadSystemNotes();
        ViewBag.DeliveryNotes = notes.DeliveryNotes.ToDictionary(n => n.OrderId, n => n);

        return View(deliveryOrders);
    }

    // 5.1 บันทึกเลขพัสดุ / แจ้งปัญหาการจัดส่ง / อัปเดตสถานะจัดส่ง
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SaveDeliveryTracking(
        int orderId,
        string? trackingNumber,
        string? carrierName,
        string? deliveryIssue,
        string? newStatus)
    {
        var notes = PromotionHelper.LoadSystemNotes();
        var existing = notes.DeliveryNotes.FirstOrDefault(n => n.OrderId == orderId);
        if (existing == null)
        {
            existing = new DeliveryTrackingNote { OrderId = orderId };
            notes.DeliveryNotes.Add(existing);
        }

        if (trackingNumber != null) existing.TrackingNumber = trackingNumber.Trim();
        if (carrierName != null) existing.CarrierName = carrierName.Trim();
        if (deliveryIssue != null) existing.DeliveryIssue = deliveryIssue.Trim();
        existing.UpdatedAt = DateTime.Now;
        PromotionHelper.SaveSystemNotes(notes);

        if (!string.IsNullOrEmpty(newStatus))
        {
            var order = _db.Orders.Find(orderId);
            if (order != null && order.Status != "Cancelled" && order.Status != "Completed")
            {
                order.Status = newStatus;
                _db.SaveChanges();
            }
        }

        TempData["Success"] = $"บันทึกข้อมูลการจัดส่งคำสั่งซื้อ #{orderId} เรียบร้อยแล้ว";
        return RedirectToAction(nameof(Delivery));
    }

    // 6. อัปเดตสถานะคำสั่งซื้อกลาง (ล็อกถาวรหากเป็น Cancelled หรือ Completed)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateOrderStatus(int orderId, string newStatus, string? returnUrl)
    {
        var order = _db.Orders
            .Include(o => o.Orderitems).ThenInclude(oi => oi.Product)
            .Include(o => o.Payments)
            .FirstOrDefault(o => o.OrderId == orderId);

        if (order != null)
        {
            if (order.Status == "Cancelled" || order.Status == "Completed")
            {
                TempData["Error"] = $"คำสั่งซื้อ #{orderId} อยู่ในสถานะ {order.Status} แล้ว ไม่สามารถเปลี่ยนแปลงสถานะได้อีก";
                return !string.IsNullOrEmpty(returnUrl) ? Redirect(returnUrl) : RedirectToAction(nameof(Orders));
            }

            if (newStatus == "Cancelled")
            {
                foreach (var item in order.Orderitems)
                {
                    if (item.Product != null) item.Product.StockQuantity += item.Quantity;
                }
            }
            else if (newStatus == "Paid")
            {
                var payment = order.Payments.FirstOrDefault();
                if (payment != null) payment.PaymentStatus = "Verified";
            }

            order.Status = newStatus;
            _db.SaveChanges();
            TempData["Success"] = $"อัปเดตสถานะคำสั่งซื้อ #{orderId} เป็น {newStatus} เรียบร้อยแล้ว";
        }

        if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Orders));
    }

    // 7. หน้าจัดการผู้ใช้งานและกำหนด Role ทั้ง 5 ตำแหน่ง (เฉพาะ Admin)
    [HttpGet]
    public IActionResult Users(string? roleFilter)
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        var query = _db.Users.AsQueryable();
        if (!string.IsNullOrEmpty(roleFilter))
            query = query.Where(u => u.Role == roleFilter);

        ViewBag.RoleFilter = roleFilter;
        return View(query.OrderByDescending(u => u.UserId).ToList());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreateUser(User user)
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        ModelState.Remove("Addresses");
        ModelState.Remove("Carts");
        ModelState.Remove("Orders");
        ModelState.Remove("Pets");
        ModelState.Remove("Reviews");

        if (_db.Users.Any(u => u.Username == user.Username || u.Email == user.Email))
        {
            TempData["Error"] = "ชื่อผู้ใช้หรืออีเมลนี้มีอยู่ในระบบแล้ว";
            return RedirectToAction(nameof(Users));
        }

        if (ModelState.IsValid)
        {
            user.CreatedAt = DateTime.Now;
            _db.Users.Add(user);
            _db.SaveChanges();
            TempData["Success"] = $"เพิ่มผู้ใช้งาน {user.Username} ตำแหน่ง {user.Role} เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateUserRole(int userId, string newRole)
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        var user = _db.Users.Find(userId);
        if (user != null)
        {
            user.Role = newRole;
            _db.SaveChanges();
            TempData["Success"] = $"เปลี่ยนสิทธิ์ของ {user.Username} เป็น {newRole} เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteUser(int userId)
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        int? currentUserId = HttpContext.Session.GetInt32("UserId");
        if (userId == currentUserId)
        {
            TempData["Error"] = "ไม่สามารถลบบัญชีที่กำลังใช้งานอยู่ได้";
            return RedirectToAction(nameof(Users));
        }

        var user = _db.Users.Find(userId);
        if (user != null)
        {
            if (_db.Orders.Any(o => o.UserId == userId))
            {
                TempData["Error"] = "ไม่สามารถลบผู้ใช้นี้ได้ เนื่องจากมีประวัติคำสั่งซื้ออยู่ในระบบ";
                return RedirectToAction(nameof(Users));
            }

            _db.Users.Remove(user);
            _db.SaveChanges();
            TempData["Success"] = $"ลบผู้ใช้งาน {user.Username} เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(Users));
    }

    // 8. หน้าจัดการโปรโมชั่นทั้งหมด (เฉพาะ Admin)
    [HttpGet]
    public IActionResult Promotions()
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        PromotionHelper.EnsureDefaultPromotions(_db);

        var promos = _db.Promotions.OrderBy(p => p.PromotionId).ToList();
        return View(promos);
    }

    // 9. เพิ่มโปรโมชั่นใหม่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreatePromotion(Promotion promo, string? promoCode)
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        ModelState.Remove("Orders");

        if (promo.EndDate < promo.StartDate)
        {
            TempData["Error"] = "วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่มต้น";
            return RedirectToAction(nameof(Promotions));
        }

        if (ModelState.IsValid)
        {
            promo.Status = true;
            _db.Promotions.Add(promo);
            _db.SaveChanges();
            if (!string.IsNullOrWhiteSpace(promoCode))
            {
                PromotionHelper.SetPromoCode(promo.PromotionId, promoCode);
            }
            TempData["Success"] = $"เพิ่มโปรโมชั่น '{promo.PromotionName}' เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(Promotions));
    }

    // 9.1 แก้ไขเงื่อนไขโปรโมชั่น
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EditPromotion(Promotion promo, string? promoCode)
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        var existing = _db.Promotions.Find(promo.PromotionId);
        if (existing != null)
        {
            if (promo.EndDate < promo.StartDate)
            {
                TempData["Error"] = "วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่มต้น";
                return RedirectToAction(nameof(Promotions));
            }

            existing.PromotionName = promo.PromotionName;
            existing.PromotionType = promo.PromotionType;
            existing.DiscountPercent = promo.DiscountPercent;
            existing.DiscountAmount = promo.DiscountAmount;
            existing.MinPurchase = promo.MinPurchase;
            existing.MaxDiscount = promo.MaxDiscount;
            existing.StartDate = promo.StartDate;
            existing.EndDate = promo.EndDate;
            _db.SaveChanges();

            if (!string.IsNullOrWhiteSpace(promoCode))
            {
                PromotionHelper.SetPromoCode(existing.PromotionId, promoCode);
            }

            TempData["Success"] = $"แก้ไขเงื่อนไขโปรโมชั่น '{existing.PromotionName}' เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(Promotions));
    }

    // 10. สลับสถานะเปิด/ปิดการใช้งานโปรโมชั่น
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult TogglePromotionStatus(int promotionId)
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        var promo = _db.Promotions.Find(promotionId);
        if (promo != null)
        {
            promo.Status = !(promo.Status ?? false);
            _db.SaveChanges();
            TempData["Success"] = $"เปลี่ยนสถานะโปรโมชั่น '{promo.PromotionName}' เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(Promotions));
    }

    // 11. ลบโปรโมชั่น
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeletePromotion(int promotionId)
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        var promo = _db.Promotions.Find(promotionId);
        if (promo != null)
        {
            if (_db.Orders.Any(o => o.PromotionId == promotionId))
            {
                TempData["Error"] = "ไม่สามารถลบโปรโมชั่นนี้ได้ เนื่องจากมีคำสั่งซื้อที่ใช้งานไปแล้ว (แนะนำให้กดปิดสถานะแทน)";
                return RedirectToAction(nameof(Promotions));
            }

            _db.Promotions.Remove(promo);
            _db.SaveChanges();
            TempData["Success"] = $"ลบโปรโมชั่น '{promo.PromotionName}' เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(Promotions));
    }
}
