using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetParadise.Models;

namespace PetParadise.Controllers;

public class ProductController : Controller
{
    private readonly Csi402dbContext _db;

    public ProductController(Csi402dbContext db)
    {
        _db = db;
    }

    // 1. หน้ารายการสินค้าทั้งหมด (Product Listing)
    [HttpGet]
    public IActionResult Index(int? categoryId, string? search)
    {
        var query = from p in _db.Products
                        .Include(p => p.Category)
                        .Include(p => p.Productimages)
                        .Include(p => p.Reviews)
                    where p.Status == "Active"
                    select p;

        if (categoryId != null && categoryId > 0)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(p => p.ProductName.Contains(search));
        }

        ViewBag.Categories = (from c in _db.Categories select c).ToList();
        ViewBag.SelectedCategory = categoryId;
        ViewBag.SearchKeyword = search;

        return View(query.ToList());
    }

    // 2. หน้ารายละเอียดสินค้า (Product Detail + รีวิว)
    [HttpGet]
    public IActionResult Detail(int id)
    {
        var product = (from p in _db.Products
                       .Include(p => p.Category)
                       .Include(p => p.Productimages)
                       .Include(p => p.Reviews).ThenInclude(r => r.User)
                       where p.ProductId == id
                       select p).FirstOrDefault();

        if (product == null)
        {
            return NotFound();
        }

        // ตรวจสอบว่าผู้ใช้ปัจจุบันเคยซื้อสินค้านี้และสถานะ Completed แล้วหรือยัง
        int? userId = HttpContext.Session.GetInt32("UserId");
        bool canReview = false;

        if (userId != null)
        {
            canReview = _db.Orders
                .Include(o => o.Orderitems)
                .Any(o => o.UserId == userId
                       && o.Status == "Completed"
                       && o.Orderitems.Any(oi => oi.ProductId == id));
        }

        ViewBag.CanReview = canReview;
        return View(product);
    }

    // 3. บันทึกรีวิวสินค้า
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddReview(int productId, int rating, string? comment)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        // ยืนยันสิทธิ์การซื้อสินค้าสำเร็จ
        bool hasPurchased = _db.Orders
            .Include(o => o.Orderitems)
            .Any(o => o.UserId == userId
                   && o.Status == "Completed"
                   && o.Orderitems.Any(oi => oi.ProductId == productId));

        if (!hasPurchased)
        {
            TempData["Error"] = "คุณสามารถรีวิวสินค้าได้หลังจากสั่งซื้อและได้รับสินค้าเรียบร้อยแล้วเท่านั้น";
            return RedirectToAction(nameof(Detail), new { id = productId });
        }

        rating = Math.Clamp(rating, 1, 5);

        // หากเคยรีวิวสินค้านี้แล้วให้อัปเดตข้อความและคะแนนเดิม
        var existingReview = _db.Reviews.FirstOrDefault(r => r.UserId == userId.Value && r.ProductId == productId);
        if (existingReview != null)
        {
            existingReview.Rating = rating;
            existingReview.Comment = comment;
            existingReview.CreatedAt = DateTime.Now;
            TempData["Success"] = "อัปเดตรีวิวสินค้าของคุณเรียบร้อยแล้ว";
        }
        else
        {
            _db.Reviews.Add(new Review
            {
                UserId = userId.Value,
                ProductId = productId,
                Rating = rating,
                Comment = comment,
                CreatedAt = DateTime.Now
            });
            TempData["Success"] = "ขอบคุณสำหรับการรีวิวสินค้า!";
        }

        _db.SaveChanges();
        return RedirectToAction(nameof(Detail), new { id = productId });
    }
}