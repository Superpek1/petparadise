using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetParadise.Models;

namespace PetParadise.Controllers;

public class OrderController : Controller
{
    private readonly Csi402dbContext _db;

    public OrderController(Csi402dbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Tracking(int? orderId)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        var notes = PromotionHelper.LoadSystemNotes();
        ViewBag.DeliveryNotes = notes.DeliveryNotes.ToDictionary(n => n.OrderId, n => n);
        ViewBag.IsLoggedIn = userId != null;

        // ดึงรายการคำสั่งซื้อของผู้ใช้ที่ล็อกอินอยู่ ซึ่งยังไม่ได้จัดส่งสำเร็จ (ไม่รวม Completed และ Cancelled)
        var activeOrders = new List<Order>();
        if (userId != null)
        {
            activeOrders = _db.Orders
                .Include(o => o.Orderitems).ThenInclude(oi => oi.Product)
                .Include(o => o.Payments)
                .Include(o => o.Promotion)
                .Include(o => o.Address)
                .Where(o => o.UserId == userId.Value && o.Status != "Completed" && o.Status != "Cancelled")
                .OrderByDescending(o => o.OrderDate)
                .ToList();
        }
        ViewBag.ActiveOrders = activeOrders;

        if (orderId == null)
        {
            return View(null);
        }

        var order = (from o in _db.Orders
                     .Include(o => o.Orderitems)
                     .ThenInclude(oi => oi.Product)
                     .Include(o => o.Payments)
                     .Include(o => o.Promotion)
                     .Include(o => o.Address)
                     where o.OrderId == orderId
                     select o).FirstOrDefault();

        if (order != null)
        {
            ViewBag.DeliveryNote = notes.DeliveryNotes.FirstOrDefault(n => n.OrderId == order.OrderId);
        }

        return View(order);
    }

    // หน้าพิมพ์ใบเสร็จ / ใบสรุปคำสั่งซื้อ (Receipt / Invoice)
    [HttpGet]
    public IActionResult Receipt(int orderId)
    {
        var order = _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.Promotion)
            .Include(o => o.Payments)
            .Include(o => o.Orderitems).ThenInclude(oi => oi.Product)
            .FirstOrDefault(o => o.OrderId == orderId);

        if (order == null) return NotFound();

        var notes = PromotionHelper.LoadSystemNotes();
        ViewBag.DeliveryNote = notes.DeliveryNotes.FirstOrDefault(n => n.OrderId == order.OrderId);

        return View(order);
    }

    // แจ้งชำระเงิน / อัปโหลดสลิปเพิ่มเติม
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UploadPaymentSlip(int orderId, IFormFile? slipFile)
    {
        if (slipFile == null || slipFile.Length == 0)
        {
            TempData["Error"] = "กรุณาเลือกไฟล์รูปภาพสลิปการโอนเงิน";
            return RedirectToAction(nameof(Tracking), new { orderId });
        }

        var order = _db.Orders.Include(o => o.Payments).FirstOrDefault(o => o.OrderId == orderId);
        if (order == null) return NotFound();

        string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "slips");
        Directory.CreateDirectory(uploadFolder);

        string fileName = $"slip_{order.OrderId}_{Guid.NewGuid():N}{Path.GetExtension(slipFile.FileName)}";
        string fullPath = Path.Combine(uploadFolder, fileName);

        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            slipFile.CopyTo(stream);
        }

        string savedSlipPath = "/uploads/slips/" + fileName;
        var payment = order.Payments.FirstOrDefault();
        if (payment != null)
        {
            payment.PaymentSlip = savedSlipPath;
            payment.PaymentStatus = "Uploaded";
            payment.PaymentDate = DateTime.Now;
        }
        else
        {
            _db.Payments.Add(new Payment
            {
                OrderId = order.OrderId,
                PaymentMethod = "Bank Transfer",
                PaymentAmount = order.TotalAmount,
                PaymentDate = DateTime.Now,
                PaymentStatus = "Uploaded",
                PaymentSlip = savedSlipPath
            });
        }

        _db.SaveChanges();
        TempData["Success"] = $"แจ้งชำระเงินคำสั่งซื้อ #{orderId} และอัปโหลดหลักฐานการโอนเรียบร้อยแล้ว รอเจ้าหน้าที่ตรวจสอบ";
        return RedirectToAction(nameof(Tracking), new { orderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CancelOrder(int orderId)
    {
        var order = (from o in _db.Orders
                     .Include(o => o.Orderitems)
                     .ThenInclude(oi => oi.Product)
                     where o.OrderId == orderId
                     select o).FirstOrDefault();

        if (order == null) return NotFound();

        string[] cancellableStatuses = { "Pending", "Paid", "Preparing" };
        if (cancellableStatuses.Contains(order.Status))
        {
            order.Status = "Cancelled";

            // คืนสต็อกสินค้ากลับเข้าคลัง
            foreach (var item in order.Orderitems)
            {
                if (item.Product != null)
                {
                    item.Product.StockQuantity += item.Quantity;
                }
            }

            _db.SaveChanges();
            TempData["Success"] = $"ยกเลิกคำสั่งซื้อเลขที่ #{orderId} และคืนสินค้าเข้าคลังเรียบร้อยแล้ว";
        }
        else
        {
            TempData["Error"] = "ไม่สามารถยกเลิกคำสั่งซื้อได้ เนื่องจากสินค้าอยู่ระหว่างจัดส่งหรือเสร็จสิ้นแล้ว";
        }

        return RedirectToAction(nameof(Tracking), new { orderId = orderId });
    }
}
