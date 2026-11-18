using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PetParadise.Models;

namespace PetParadise.Controllers;

public class AdminProductController : Controller
{
    private readonly Csi402dbContext _db;

    public AdminProductController(Csi402dbContext db)
    {
        _db = db;
    }

    // ตัวกรองสิทธิ์: อนุญาตเฉพาะ Role == "Admin" เท่านั้น
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var role = HttpContext.Session.GetString("Role");
        if (role != "Admin")
        {
            context.Result = RedirectToAction("Login", "Account");
        }
        base.OnActionExecuting(context);
    }

    // 1. หน้ารายการสินค้าทั้งหมด
    [HttpGet]
    public IActionResult Index()
    {
        var products = (from p in _db.Products.Include(p => p.Category)
                        orderby p.ProductId descending
                        select p).ToList();

        return View(products);
    }

    // 2. หน้าเปิดฟอร์มเพิ่มสินค้า (พร้อมส่งข้อมูลหมวดหมู่ไปแสดง)
    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.Categories = new SelectList(_db.Categories, "CategoryId", "CategoryName");
        ViewBag.CategoryList = (from c in _db.Categories orderby c.CategoryId descending select c).ToList();
        return View();
    }

    // 3. ฟังก์ชันบันทึกสินค้าใหม่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(Product product, string? ImageUrl)
    {
        ModelState.Remove("Category");
        ModelState.Remove("Productimages");
        ModelState.Remove("Orderitems");
        ModelState.Remove("Cartitems");
        ModelState.Remove("Reviews");

        if (product.Price < 0)
        {
            ModelState.AddModelError("Price", "ราคาสินค้าต้องไม่ติดลบ");
        }
        if (product.Weight < 0)
        {
            ModelState.AddModelError("Weight", "น้ำหนักสินค้าต้องไม่ติดลบ");
        }
        if (product.StockQuantity < 0)
        {
            ModelState.AddModelError("StockQuantity", "จำนวนสต็อกต้องไม่ติดลบ");
        }

        if (ModelState.IsValid)
        {
            product.CreatedAt = DateTime.Now;
            if (string.IsNullOrEmpty(product.Status))
            {
                product.Status = "Active";
            }

            _db.Products.Add(product);
            _db.SaveChanges();

            if (!string.IsNullOrWhiteSpace(ImageUrl))
            {
                var img = new Productimage
                {
                    ProductId = product.ProductId,
                    ImageUrl = ImageUrl.Trim()
                };
                _db.Productimages.Add(img);
                _db.SaveChanges();
            }

            return RedirectToAction(nameof(Index));
        }

        ViewBag.Categories = new SelectList(_db.Categories, "CategoryId", "CategoryName", product.CategoryId);
        ViewBag.CategoryList = (from c in _db.Categories orderby c.CategoryId descending select c).ToList();
        return View(product);
    }

    // 4. หน้าเปิดฟอร์มแก้ไขสินค้า
    [HttpGet]
    public IActionResult Edit(int id)
    {
        var product = (from p in _db.Products.Include(p => p.Productimages)
                       where p.ProductId == id
                       select p).FirstOrDefault();

        if (product == null)
        {
            return NotFound();
        }

        ViewBag.Categories = new SelectList(_db.Categories, "CategoryId", "CategoryName", product.CategoryId);
        ViewBag.FirstImage = product.Productimages.FirstOrDefault()?.ImageUrl ?? "";
        return View(product);
    }

    // 5. ฟังก์ชันบันทึกการแก้ไขสินค้า
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(int id, Product product, string? ImageUrl)
    {
        if (id != product.ProductId)
        {
            return NotFound();
        }

        ModelState.Remove("Category");
        ModelState.Remove("Productimages");
        ModelState.Remove("Orderitems");
        ModelState.Remove("Cartitems");
        ModelState.Remove("Reviews");

        if (product.Price < 0)
        {
            ModelState.AddModelError("Price", "ราคาสินค้าต้องไม่ติดลบ");
        }
        if (product.Weight < 0)
        {
            ModelState.AddModelError("Weight", "น้ำหนักสินค้าต้องไม่ติดลบ");
        }
        if (product.StockQuantity < 0)
        {
            ModelState.AddModelError("StockQuantity", "จำนวนสต็อกต้องไม่ติดลบ");
        }

        if (ModelState.IsValid)
        {
            var existingProduct = (from p in _db.Products.Include(p => p.Productimages)
                                   where p.ProductId == id
                                   select p).FirstOrDefault();

            if (existingProduct == null)
            {
                return NotFound();
            }

            existingProduct.ProductName = product.ProductName;
            existingProduct.CategoryId = product.CategoryId;
            existingProduct.Price = product.Price;
            existingProduct.Weight = product.Weight;
            existingProduct.StockQuantity = product.StockQuantity;
            existingProduct.Status = product.Status;
            existingProduct.Description = product.Description;

            if (!string.IsNullOrWhiteSpace(ImageUrl))
            {
                var existingImg = existingProduct.Productimages.FirstOrDefault();
                if (existingImg != null)
                {
                    existingImg.ImageUrl = ImageUrl.Trim();
                }
                else
                {
                    _db.Productimages.Add(new Productimage
                    {
                        ProductId = id,
                        ImageUrl = ImageUrl.Trim()
                    });
                }
            }

            _db.SaveChanges();
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Categories = new SelectList(_db.Categories, "CategoryId", "CategoryName", product.CategoryId);
        ViewBag.FirstImage = ImageUrl;
        return View(product);
    }

    // 6. ฟังก์ชันลบสินค้า
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        var product = (from p in _db.Products where p.ProductId == id select p).FirstOrDefault();
        if (product != null)
        {
            var images = (from i in _db.Productimages where i.ProductId == id select i).ToList();
            _db.Productimages.RemoveRange(images);

            _db.Products.Remove(product);
            _db.SaveChanges();
        }

        return RedirectToAction(nameof(Index));
    }

    // 7. ฟังก์ชันเพิ่มหมวดหมู่ใหม่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddCategory(string categoryName, string? description)
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var cat = new Category
            {
                CategoryName = categoryName.Trim(),
                Description = description ?? ""
            };
            _db.Categories.Add(cat);
            _db.SaveChanges();
        }
        return RedirectToAction(nameof(Create));
    }

    // 8. ฟังก์ชันลบหมวดหมู่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteCategory(int id)
    {
        var cat = (from c in _db.Categories where c.CategoryId == id select c).FirstOrDefault();
        if (cat != null)
        {
            var hasProducts = _db.Products.Any(p => p.CategoryId == id);
            if (!hasProducts)
            {
                _db.Categories.Remove(cat);
                _db.SaveChanges();
            }
        }
        return RedirectToAction(nameof(Create));
    }
}