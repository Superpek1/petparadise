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

    // 1. แดชบอร์ดภาพรวม (เฉพาะ Admin)
    [HttpGet]
    public IActionResult Dashboard()
    {
        if (HttpContext.Session.GetString("Role") != "Admin")
            return RedirectToAction("Login", "Account");

        ViewBag.TotalSales = _db.Orders.Where(o => o.Status != "Cancelled").Sum(o => (decimal?)o.TotalAmount) ?? 0;
        ViewBag.TotalOrders = _db.Orders.Count();
        ViewBag.PendingOrders = _db.Orders.Count(o => o.Status == "Pending");
        ViewBag.TotalCustomers = _db.Users.Count(u => u.Role == "Customer");
        ViewBag.LowStockProducts = _db.Products.Where(p => p.StockQuantity <= 5).ToList();

        var recentOrders = _db.Orders.Include(o => o.User).OrderByDescending(o => o.OrderDate).Take(5).ToList();
        return View(recentOrders);
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

    // 3. หน้างานคลังสินค้า (Admin & Warehouse)
    [HttpGet]
    public IActionResult Warehouse()
    {
        var role = HttpContext.Session.GetString("Role");
        if (role != "Admin" && role != "Warehouse")
            return RedirectToAction("Login", "Account");

        ViewBag.OrdersToPack = _db.Orders
            .Include(o => o.User)
            .Include(o => o.Orderitems).ThenInclude(oi => oi.Product)
            .Where(o => o.Status == "Paid" || o.Status == "Preparing")
            .OrderBy(o => o.OrderDate)
            .ToList();

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

        return View(deliveryOrders);
    }

    // 6. อัปเดตสถานะคำสั่งซื้อกลาง (ล็อกถาวรหากเป็น Cancelled หรือ Completed)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateOrderStatus(int orderId, string newStatus, string? returnUrl)
    {
        var order = _db.Orders
            .Include(o => o.Orderitems).ThenInclude(oi => oi.Product)
            .FirstOrDefault(o => o.OrderId == orderId);

        if (order != null)
        {
            // ป้องกันการแก้ไขหากคำสั่งซื้อถูกยกเลิกหรือจัดส่งสำเร็จแล้ว
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

        var promos = _db.Promotions.OrderByDescending(p => p.PromotionId).ToList();
        return View(promos);
    }

    // 9. เพิ่มโปรโมชั่นใหม่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreatePromotion(Promotion promo)
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
            TempData["Success"] = $"เพิ่มโปรโมชั่น '{promo.PromotionName}' เรียบร้อยแล้ว";
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