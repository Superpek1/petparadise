using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetParadise.Models;

namespace PetParadise.Controllers;

public class HomeController : Controller
{
    private readonly Csi402dbContext _db;

    public HomeController(Csi402dbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Lab9()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Lab9(Labstudent data)
    {
        if (ModelState.IsValid)
        {
            var isDuplicate = _db.Labstudents.Any(s => s.StdId == data.StdId);

            if (isDuplicate)
            {
                ModelState.AddModelError("StdId", "ID ซ้ำ");
                return View(data);
            }

            Labstudent student = new()
            {
                StdId = data.StdId,
                StdPassword = data.StdPassword,
                StdName = data.StdName,
                StdLastname = data.StdLastname
            };

            _db.Add(student);
            _db.SaveChanges();
            ViewBag.Success = "บันทึกข้อมูลเรียบร้อยแล้ว!";
            return View(data);
        }

        return View(data);
    }

    public IActionResult Lab9_1(string? status)
    {
        ViewBag.Status = status;
        var user = (from l in _db.Labstudents select l).ToList();
        return View(user);
    }

    [HttpGet]
    public IActionResult Lab10(string? StdId)
    {
        var user = (from s in _db.Labstudents where s.StdId == StdId select s).FirstOrDefault();
        if (user == null)
        {
            return RedirectToAction("Lab9_1", new { status = "ไม่พบข้อมูล" });
        }
        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Lab10_Update(Labstudent data)
    {
        var existingStudent = _db.Labstudents.FirstOrDefault(s => s.StdId == data.StdId);
        if (existingStudent != null)
        {
            existingStudent.StdPassword = data.StdPassword;
            existingStudent.StdName = data.StdName;
            existingStudent.StdLastname = data.StdLastname;

            _db.SaveChanges();
            return RedirectToAction("Lab9_1", new { status = "แก้ไขข้อมูลสำเร็จ" });
        }

        return RedirectToAction("Lab9_1", new { status = "แก้ไขไม่สำเร็จ" });
    }
    [HttpPost]
    public IActionResult Lab10_1(string? StdId)
    {
        var Check = (from s in _db.Labstudents where s.StdId == StdId select s).FirstOrDefault();
        if (Check != null)
        {
            _db.Remove(Check);
            _db.SaveChanges();
            return RedirectToAction("Lab9_1", "Home", new { status = "ลบสำเร็จ" });
        }
        else
        {
            return RedirectToAction("Lab9_1", "Home", new { status = "ลบไม่สำเร็จ" });
        }
    }

    public IActionResult Index()
    {
        PromotionHelper.EnsureDefaultPromotions(_db);

        var activeProducts = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Productimages)
            .Include(p => p.Reviews)
            .Where(p => p.Status == "Active")
            .OrderByDescending(p => p.ProductId)
            .ToList();

        // สินค้าขายดี (คำนวณจากยอดขายจริงใน Orderitems ที่ไม่ถูกยกเลิก)
        var bestSellerIds = _db.Orderitems
            .Include(oi => oi.Order)
            .Where(oi => oi.Order.Status != "Cancelled")
            .GroupBy(oi => oi.ProductId)
            .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(x => x.Quantity) })
            .OrderByDescending(x => x.TotalSold)
            .Take(4)
            .ToList();

        var bestSellers = bestSellerIds
            .Select(b => activeProducts.FirstOrDefault(p => p.ProductId == b.ProductId))
            .Where(p => p != null)
            .Cast<Product>()
            .ToList();

        if (bestSellers.Count < 4)
        {
            foreach (var p in activeProducts)
            {
                if (bestSellers.All(b => b.ProductId != p.ProductId))
                {
                    bestSellers.Add(p);
                }
                if (bestSellers.Count >= 4) break;
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        ViewBag.Promotions = _db.Promotions
            .Where(p => p.Status == true && p.StartDate <= today && p.EndDate >= today)
            .ToList();
        ViewBag.BestSellers = bestSellers;
        ViewBag.Categories = _db.Categories.ToList();

        return View(activeProducts.Take(8).ToList());
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}