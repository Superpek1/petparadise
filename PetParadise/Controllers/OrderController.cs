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
        if (orderId == null)
        {
            return View(null);
        }

        var order = (from o in _db.Orders
                     .Include(o => o.Orderitems)
                     .ThenInclude(oi => oi.Product)
                     .Include(o => o.Payments)
                     where o.OrderId == orderId
                     select o).FirstOrDefault();

        return View(order);
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