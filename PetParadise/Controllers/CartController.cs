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

    private int? GetCurrentUserId()
    {
        return HttpContext.Session.GetInt32("UserId");
    }

    private IActionResult RedirectToLoginWithMessage()
    {
        TempData["Error"] = "กรุณาเข้าสู่ระบบก่อนเลือกซื้อสินค้าหรือเพิ่มสินค้าลงตะกร้า";
        return RedirectToAction("Login", "Account");
    }

    public IActionResult Index()
    {
        int? userId = GetCurrentUserId();
        if (userId == null) return RedirectToLoginWithMessage();

        var cart = _db.Carts
            .Include(c => c.Cartitems)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefault(c => c.UserId == userId.Value);

        var items = cart?.Cartitems.ToList() ?? new List<Cartitem>();

        decimal subtotal = items.Sum(i => i.Quantity * i.Product.Price);
        decimal totalWeight = items.Sum(i => i.Quantity * i.Product.Weight);
        decimal shippingFee = items.Any()
            ? PromotionHelper.CalculateShippingFee(totalWeight)
            : 0m;

        ViewBag.Subtotal = subtotal;
        ViewBag.TotalWeight = totalWeight;
        ViewBag.ShippingFee = shippingFee;
        ViewBag.GrandTotal = subtotal + shippingFee;

        return View(items);
    }

    [HttpPost]
    public IActionResult AddToCart(int productId, int quantity = 1)
    {
        int? userId = GetCurrentUserId();
        if (userId == null) return RedirectToLoginWithMessage();

        var product = _db.Products.Find(productId);
        if (product == null) return NotFound();

        var cart = _db.Carts.FirstOrDefault(c => c.UserId == userId.Value);
        if (cart == null)
        {
            cart = new Cart { UserId = userId.Value, CreatedAt = DateTime.Now };
            _db.Carts.Add(cart);
            _db.SaveChanges();
        }

        var existingItem = _db.Cartitems.FirstOrDefault(ci => ci.CartId == cart.CartId && ci.ProductId == productId);
        if (existingItem != null)
        {
            existingItem.Quantity = Math.Min(product.StockQuantity, existingItem.Quantity + quantity);
            existingItem.Price = product.Price;
        }
        else
        {
            _db.Cartitems.Add(new Cartitem
            {
                CartId = cart.CartId,
                ProductId = productId,
                Quantity = Math.Min(product.StockQuantity, Math.Max(1, quantity)),
                Price = product.Price
            });
        }

        _db.SaveChanges();
        return RedirectToAction(nameof(Index));
    }

    // เพิ่มหรือลดจำนวนสินค้าในตะกร้า
    [HttpPost]
    public IActionResult UpdateQuantity(int cartItemId, int delta)
    {
        if (GetCurrentUserId() == null) return RedirectToLoginWithMessage();

        var item = _db.Cartitems
            .Include(ci => ci.Product)
            .FirstOrDefault(ci => ci.CartItemId == cartItemId);

        if (item != null)
        {
            int newQty = item.Quantity + delta;
            if (newQty <= 0)
            {
                _db.Cartitems.Remove(item);
            }
            else if (item.Product != null && newQty > item.Product.StockQuantity)
            {
                item.Quantity = item.Product.StockQuantity;
                TempData["Error"] = $"สินค้า '{item.Product.ProductName}' มีในคลังสูงสุด {item.Product.StockQuantity} ชิ้น";
            }
            else
            {
                item.Quantity = newQty;
            }
            _db.SaveChanges();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult RemoveItem(int cartItemId)
    {
        if (GetCurrentUserId() == null) return RedirectToLoginWithMessage();

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
        int? userId = GetCurrentUserId();
        if (userId == null) return RedirectToLoginWithMessage();

        var cart = _db.Carts
            .Include(c => c.Cartitems)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefault(c => c.UserId == userId.Value);

        var items = cart?.Cartitems.ToList() ?? new List<Cartitem>();
        if (!items.Any()) return RedirectToAction(nameof(Index));

        PromotionHelper.EnsureDefaultPromotions(_db);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var activePromos = _db.Promotions
            .Where(p => p.Status == true && p.StartDate <= today && p.EndDate >= today)
            .ToList();

        var evalDict = new Dictionary<int, PromotionEvaluationResult>();
        foreach (var p in activePromos)
        {
            evalDict[p.PromotionId] = PromotionHelper.EvaluatePromotion(p, items, userId.Value, _db);
        }

        ViewBag.Addresses = _db.Addresses.Where(a => a.UserId == userId.Value).ToList();
        ViewBag.Promotions = activePromos;
        ViewBag.PromoEvaluations = evalDict;

        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ConfirmOrder(
        int? selectedAddressId,
        int? selectedPromotionId,
        string? promoCode,
        string? recipientName,
        string? phone,
        string? addressDetail,
        string? paymentMethod,
        IFormFile? slipFile)
    {
        int? currentUserId = GetCurrentUserId();
        if (currentUserId == null) return RedirectToLoginWithMessage();
        int userId = currentUserId.Value;

        var cart = _db.Carts
            .Include(c => c.Cartitems)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefault(c => c.UserId == userId);

        if (cart == null || !cart.Cartitems.Any()) return RedirectToAction(nameof(Index));

        var items = cart.Cartitems.ToList();

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

        decimal subtotal = items.Sum(i => i.Quantity * i.Product.Price);
        decimal totalWeight = items.Sum(i => i.Quantity * i.Product.Weight);
        decimal shippingFee = PromotionHelper.CalculateShippingFee(totalWeight);

        decimal discount = 0m;
        int? appliedPromotionId = null;
        bool isFreeShippingPromo = false;
        decimal freeShippingMinAfterDiscount = 799m;

        Promotion? promo = null;
        if (selectedPromotionId != null && selectedPromotionId > 0)
        {
            promo = _db.Promotions.Find(selectedPromotionId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(promoCode))
        {
            var allPromos = _db.Promotions.Where(p => p.Status == true).ToList();
            promo = PromotionHelper.FindPromotionByCode(promoCode, allPromos);
        }

        if (promo != null)
        {
            var eval = PromotionHelper.EvaluatePromotion(promo, items, userId, _db);
            if (eval.IsEligible)
            {
                appliedPromotionId = promo.PromotionId;
                discount = eval.DiscountAmount;
                isFreeShippingPromo = eval.IsFreeShipping;
                freeShippingMinAfterDiscount = promo.MinPurchase > 0 ? promo.MinPurchase.Value : 799m;
            }
        }

        decimal afterDiscount = Math.Max(0m, subtotal - discount);

        // เงื่อนไขโปรโมชั่นที่ 4 (ส่งฟรี): ยอดสั่งซื้อหลังหักส่วนลดต้องไม่น้อยกว่า 799 บาท
        if (isFreeShippingPromo && afterDiscount >= freeShippingMinAfterDiscount)
        {
            shippingFee = 0m;
        }

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
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Bank Transfer" : paymentMethod,
            PaymentAmount = totalAmount,
            PaymentDate = DateTime.Now,
            PaymentStatus = savedSlipPath != null ? "Uploaded" : "Pending",
            PaymentSlip = savedSlipPath
        });

        // 3. ย้ายสินค้าจากตะกร้าลง Orderitems และตัดสต็อก
        foreach (var item in items)
        {
            _db.Orderitems.Add(new Orderitem
            {
                OrderId = order.OrderId,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.Product.Price,
                Subtotal = item.Quantity * item.Product.Price
            });

            if (item.Product.StockQuantity >= item.Quantity)
            {
                item.Product.StockQuantity -= item.Quantity;
            }
        }

        // 4. ล้างตะกร้าสินค้า
        _db.Cartitems.RemoveRange(items);
        _db.SaveChanges();

        return RedirectToAction("Tracking", "Order", new { orderId = order.OrderId });
    }
}
