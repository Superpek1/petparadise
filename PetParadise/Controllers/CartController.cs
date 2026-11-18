using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetParadise.Models;

namespace PetParadise.Controllers;

public class CartController : Controller
{
    private readonly Csi402dbContext _db;

    public CartController(Csi402dbContext db)
    {
        _db = db;
    }

    private int GetCurrentUserId()
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId != null) return userId.Value;

        var firstUser = _db.Users.FirstOrDefault();
        return firstUser?.UserId ?? 1;
    }

    public IActionResult Index()
    {
        int userId = GetCurrentUserId();
        var cart = _db.Carts
            .Include(c => c.Cartitems)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefault(c => c.UserId == userId);

        var items = cart?.Cartitems.ToList() ?? new List<Cartitem>();

        decimal subtotal = items.Sum(i => i.Quantity * i.Product.Price);
        decimal totalWeight = items.Sum(i => i.Quantity * i.Product.Weight);
        decimal shippingFee = items.Any() 
            ? (totalWeight <= 1 ? 40 : (40 + (Math.Ceiling(totalWeight - 1) * 15))) 
            : 0;

        ViewBag.Subtotal = subtotal;
        ViewBag.TotalWeight = totalWeight;
        ViewBag.ShippingFee = shippingFee;
        ViewBag.GrandTotal = subtotal + shippingFee;

        return View(items);
    }

    [HttpPost]
    public IActionResult AddToCart(int productId, int quantity = 1)
    {
        int userId = GetCurrentUserId();
        var product = _db.Products.Find(productId);
        if (product == null) return NotFound();

        var cart = _db.Carts.FirstOrDefault(c => c.UserId == userId);
        if (cart == null)
        {
            cart = new Cart { UserId = userId, CreatedAt = DateTime.Now };
            _db.Carts.Add(cart);
            _db.SaveChanges();
        }

        var existingItem = _db.Cartitems.FirstOrDefault(ci => ci.CartId == cart.CartId && ci.ProductId == productId);
        if (existingItem != null)
        {
            existingItem.Quantity += quantity;
            existingItem.Price = product.Price;
        }
        else
        {
            _db.Cartitems.Add(new Cartitem
            {
                CartId = cart.CartId,
                ProductId = productId,
                Quantity = quantity,
                Price = product.Price
            });
        }

        _db.SaveChanges();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult RemoveItem(int cartItemId)
    {
        var item = _db.Cartitems.Find(cartItemId);
        if (item != null)
        {
            _db.Cartitems.Remove(item);
            _db.SaveChanges();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Checkout()
    {
        int userId = GetCurrentUserId();
        var cart = _db.Carts
            .Include(c => c.Cartitems)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefault(c => c.UserId == userId);

        var items = cart?.Cartitems.ToList() ?? new List<Cartitem>();
        if (!items.Any()) return RedirectToAction(nameof(Index));

        var today = DateOnly.FromDateTime(DateTime.Now);
        ViewBag.Addresses = _db.Addresses.Where(a => a.UserId == userId).ToList();
        ViewBag.Promotions = _db.Promotions
            .Where(p => p.Status == true && p.StartDate <= today && p.EndDate >= today)
            .ToList();

        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ConfirmOrder(int? selectedAddressId, int? selectedPromotionId, string recipientName, string phone, string addressDetail, IFormFile? slipFile)
    {
        int userId = GetCurrentUserId();
        var cart = _db.Carts
            .Include(c => c.Cartitems)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefault(c => c.UserId == userId);

        if (cart == null || !cart.Cartitems.Any()) return RedirectToAction(nameof(Index));

        int finalAddressId = 0;
        if (selectedAddressId != null && selectedAddressId > 0)
        {
            finalAddressId = selectedAddressId.Value;
        }
        else
        {
            var newAddr = new Address
            {
                UserId = userId,
                RecipientName = recipientName ?? "ลูกค้าทั่วไป",
                Phone = phone ?? "-",
                AddressDetail = addressDetail ?? "-",
                District = "-",
                Province = "-",
                PostalCode = "-",
                IsDefault = false
            };
            _db.Addresses.Add(newAddr);
            _db.SaveChanges();
            finalAddressId = newAddr.AddressId;
        }

        decimal subtotal = cart.Cartitems.Sum(i => i.Quantity * i.Product.Price);
        decimal totalWeight = cart.Cartitems.Sum(i => i.Quantity * i.Product.Weight);
        decimal shippingFee = totalWeight <= 1 ? 40 : (40 + (Math.Ceiling(totalWeight - 1) * 15));

        decimal discount = 0;
        int? appliedPromotionId = null;

        if (selectedPromotionId != null && selectedPromotionId > 0)
        {
            var promo = _db.Promotions.Find(selectedPromotionId.Value);
            var today = DateOnly.FromDateTime(DateTime.Now);

            if (promo != null && promo.Status == true && promo.StartDate <= today && promo.EndDate >= today && subtotal >= (promo.MinPurchase ?? 0))
            {
                appliedPromotionId = promo.PromotionId;
                if (promo.PromotionType == "Percent")
                {
                    discount = Math.Round(subtotal * ((promo.DiscountPercent ?? 0) / 100m), 2);
                    if (promo.MaxDiscount != null && promo.MaxDiscount > 0 && discount > promo.MaxDiscount.Value)
                    {
                        discount = promo.MaxDiscount.Value;
                    }
                }
                else
                {
                    discount = promo.DiscountAmount ?? 0;
                }

                if (discount > subtotal) discount = subtotal;
            }
        }

        decimal afterDiscount = Math.Max(0, subtotal - discount);
        decimal vat = Math.Round((afterDiscount + shippingFee) * 0.07m, 2);
        decimal totalAmount = afterDiscount + shippingFee + vat;

        // 1. บันทึก Order
        var order = new Order
        {
            UserId = userId,
            AddressId = finalAddressId,
            PromotionId = appliedPromotionId,
            OrderDate = DateTime.Now,
            Subtotal = subtotal,
            Discount = discount,
            ShippingFee = shippingFee,
            Vat = vat,
            TotalAmount = totalAmount,
            Status = "Pending"
        };
        _db.Orders.Add(order);
        _db.SaveChanges();

        // 2. อัปโหลดสลิปและบันทึกลงตาราง Payments
        string? savedSlipPath = null;
        if (slipFile != null && slipFile.Length > 0)
        {
            string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "slips");
            Directory.CreateDirectory(uploadFolder);

            string fileName = $"slip_{order.OrderId}_{Guid.NewGuid():N}{Path.GetExtension(slipFile.FileName)}";
            string fullPath = Path.Combine(uploadFolder, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                slipFile.CopyTo(stream);
            }
            savedSlipPath = "/uploads/slips/" + fileName;
        }

        _db.Payments.Add(new Payment
        {
            OrderId = order.OrderId,
            PaymentMethod = "Bank Transfer",
            PaymentAmount = totalAmount,
            PaymentDate = DateTime.Now,
            PaymentStatus = savedSlipPath != null ? "Uploaded" : "Pending",
            PaymentSlip = savedSlipPath
        });

        // 3. ย้ายสินค้าจากตะกร้าลง Orderitems และตัดสต็อก
        foreach (var item in cart.Cartitems)
        {
            _db.Orderitems.Add(new Orderitem
            {
                OrderId = order.OrderId,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.Product.Price
            });

            if (item.Product.StockQuantity >= item.Quantity)
            {
                item.Product.StockQuantity -= item.Quantity;
            }
        }

        // 4. ล้างตะกร้าสินค้า
        _db.Cartitems.RemoveRange(cart.Cartitems);
        _db.SaveChanges();

        return RedirectToAction("Tracking", "Order", new { orderId = order.OrderId });
    }
}