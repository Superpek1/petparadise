using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetParadise.Models;

namespace PetParadise.Controllers;

public class AccountController : Controller
{
    private readonly Csi402dbContext _db;

    public AccountController(Csi402dbContext db)
    {
        _db = db;
    }

    // 1. หน้าเข้าสู่ระบบ (Login) พร้อมสร้างบัญชี Owner / Admin / Accounting / Marketing เริ่มต้นอัตโนมัติ
    [HttpGet]
    public IActionResult Login()
    {
        bool seeded = false;
        if (!_db.Users.Any(u => u.Role == "Owner" || u.Email == "owaner@gmail.com"))
        {
            _db.Users.Add(new User
            {
                Username = "owaner",
                Email = "owaner@gmail.com",
                Password = "1234",
                FirstName = "เจ้าของกิจการ",
                LastName = "เพ็ทพาราไดซ์",
                Phone = "089-999-9999",
                Role = "Owner",
                CreatedAt = DateTime.Now
            });
            seeded = true;
        }

        if (!_db.Users.Any(u => u.Role == "Admin"))
        {
            _db.Users.Add(new User
            {
                Username = "admin",
                Email = "admin@petparadise.com",
                Password = "1234",
                FirstName = "ผู้ดูแลระบบ",
                LastName = "หลัก",
                Phone = "080-000-0000",
                Role = "Admin",
                CreatedAt = DateTime.Now
            });
            seeded = true;
        }

        if (!_db.Users.Any(u => u.Role == "Accounting"))
        {
            _db.Users.Add(new User
            {
                Username = "accounting",
                Email = "accounting@petparadise.com",
                Password = "1234",
                FirstName = "ฝ่ายบัญชี",
                LastName = "การเงิน",
                Phone = "080-000-0001",
                Role = "Accounting",
                CreatedAt = DateTime.Now
            });
            seeded = true;
        }

        if (!_db.Users.Any(u => u.Role == "Marketing"))
        {
            _db.Users.Add(new User
            {
                Username = "marketing",
                Email = "marketing@petparadise.com",
                Password = "1234",
                FirstName = "ฝ่ายการตลาด",
                LastName = "โปรโมชั่น",
                Phone = "080-000-0002",
                Role = "Marketing",
                CreatedAt = DateTime.Now
            });
            seeded = true;
        }

        if (seeded)
        {
            _db.SaveChanges();
        }

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(string username, string password)
    {
        var user = (from u in _db.Users
                    where (u.Username == username || u.Email == username) && u.Password == password
                    select u).FirstOrDefault();

        if (user != null)
        {
            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("Role", user.Role);

            // แยกเส้นทางหลังล็อกอินตาม Role ทั้ง 8 ตำแหน่ง
            return user.Role switch
            {
                "Owner" => RedirectToAction("Dashboard", "Admin"),
                "Admin" => RedirectToAction("Dashboard", "Admin"),
                "Accounting" => RedirectToAction("Reports", "Admin"),
                "Marketing" => RedirectToAction("Promotions", "Admin"),
                "Staff" => RedirectToAction("Orders", "Admin"),
                "Warehouse" => RedirectToAction("Warehouse", "Admin"),
                "Delivery" => RedirectToAction("Delivery", "Admin"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        ViewBag.Error = "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง";
        return View();
    }

    // 2. หน้าสมัครสมาชิก (Register - สำหรับ Customer)
    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Register(User user)
    {
        ModelState.Remove("Addresses");
        ModelState.Remove("Carts");
        ModelState.Remove("Orders");
        ModelState.Remove("Pets");
        ModelState.Remove("Reviews");
        ModelState.Remove("Role");

        bool isDuplicate = _db.Users.Any(u => u.Username == user.Username || u.Email == user.Email);
        if (isDuplicate)
        {
            ViewBag.Error = "ชื่อผู้ใช้หรืออีเมลนี้ถูกใช้งานแล้ว";
            return View(user);
        }

        if (ModelState.IsValid)
        {
            user.Role = "Customer";
            user.CreatedAt = DateTime.Now;

            _db.Users.Add(user);
            _db.SaveChanges();

            TempData["Success"] = "สมัครสมาชิกสำเร็จ กรุณาเข้าสู่ระบบ";
            return RedirectToAction(nameof(Login));
        }

        return View(user);
    }

    // 3. ออกจากระบบ (Logout)
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index", "Home");
    }

    // 4. หน้าข้อมูลส่วนตัว (Profile)
    [HttpGet]
    public IActionResult Profile()
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return RedirectToAction(nameof(Login));
        }

        var userProfile = (from u in _db.Users
                           .Include(u => u.Addresses)
                           .Include(u => u.Orders)
                           .Include(u => u.Pets)
                           where u.UserId == userId
                           select u).FirstOrDefault();

        if (userProfile == null) return NotFound();

        var notes = PromotionHelper.LoadSystemNotes();
        ViewBag.PetVerifications = (notes.PetVerifications ?? new List<PetVerificationNote>())
            .ToDictionary(p => p.PetId, p => p);

        return View(userProfile);
    }

    // 4.1 แก้ไขข้อมูลส่วนตัว (Edit Profile)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EditProfile(int userId, string firstName, string lastName, string email, string? phone, string? newPassword)
    {
        int? currentUserId = HttpContext.Session.GetInt32("UserId");
        if (currentUserId == null || currentUserId != userId)
            return RedirectToAction(nameof(Login));

        var user = _db.Users.Find(userId);
        if (user != null)
        {
            user.FirstName = firstName.Trim();
            user.LastName = lastName.Trim();
            user.Email = email.Trim();
            user.Phone = phone?.Trim();
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                user.Password = newPassword.Trim();
            }
            _db.SaveChanges();
            TempData["Success"] = "อัปเดตข้อมูลส่วนตัวเรียบร้อยแล้ว";
        }
        return RedirectToAction(nameof(Profile));
    }

    // 4.2 ลงทะเบียนข้อมูลสัตว์เลี้ยง พร้อมแนบรูปภาพให้ Staff ตรวจสอบ (สำหรับโปรโมชั่น Birthday Pet)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddPet(int userId, string petName, string petType, DateOnly? birthDate, IFormFile? petImage)
    {
        int? currentUserId = HttpContext.Session.GetInt32("UserId");
        if (currentUserId == null || currentUserId != userId)
            return RedirectToAction(nameof(Login));

        if (petImage == null || petImage.Length == 0)
        {
            TempData["Error"] = "กรุณาแนบรูปถ่ายสัตว์เลี้ยงของคุณเพื่อส่งให้เจ้าหน้าที่ตรวจสอบยืนยัน";
            return RedirectToAction(nameof(Profile));
        }

        if (!string.IsNullOrWhiteSpace(petName) && !string.IsNullOrWhiteSpace(petType))
        {
            var newPet = new Pet
            {
                UserId = userId,
                PetName = petName.Trim(),
                PetType = petType.Trim(),
                BirthDate = birthDate
            };
            _db.Pets.Add(newPet);
            _db.SaveChanges();

            string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "pets");
            Directory.CreateDirectory(uploadFolder);

            string ext = Path.GetExtension(petImage.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            string fileName = $"pet_{newPet.PetId}_{Guid.NewGuid():N}{ext}";
            string fullPath = Path.Combine(uploadFolder, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                petImage.CopyTo(stream);
            }

            string savedImagePath = "/uploads/pets/" + fileName;
            PromotionHelper.SavePetVerification(newPet.PetId, savedImagePath, "Pending");

            TempData["Success"] = $"ส่งข้อมูลและรูปภาพของ '{petName}' เรียบร้อยแล้ว กรุณารอเจ้าหน้าที่ (Staff) ตรวจสอบและกดยืนยัน";
        }
        return RedirectToAction(nameof(Profile));
    }

    // 4.3 ลบข้อมูลสัตว์เลี้ยง
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeletePet(int petId)
    {
        int? currentUserId = HttpContext.Session.GetInt32("UserId");
        var pet = _db.Pets.Find(petId);
        if (pet != null && pet.UserId == currentUserId)
        {
            _db.Pets.Remove(pet);
            _db.SaveChanges();
            PromotionHelper.RemovePetVerification(petId);
            TempData["Success"] = "ลบข้อมูลสัตว์เลี้ยงเรียบร้อยแล้ว";
        }
        return RedirectToAction(nameof(Profile));
    }

    // 5. เพิ่มที่อยู่จัดส่งใหม่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddAddress(Address address)
    {
        ModelState.Remove("User");
        ModelState.Remove("Orders");

        if (ModelState.IsValid)
        {
            address.IsDefault = !_db.Addresses.Any(a => a.UserId == address.UserId);
            _db.Addresses.Add(address);
            _db.SaveChanges();
            TempData["Success"] = "เพิ่มที่อยู่จัดส่งใหม่เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(Profile));
    }

    // 6. แก้ไขที่อยู่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EditAddress(Address address)
    {
        int? currentUserId = HttpContext.Session.GetInt32("UserId");
        var existing = _db.Addresses.Find(address.AddressId);
        if (existing != null && existing.UserId == currentUserId)
        {
            existing.RecipientName = address.RecipientName.Trim();
            existing.Phone = address.Phone.Trim();
            existing.AddressDetail = address.AddressDetail.Trim();
            existing.District = address.District.Trim();
            existing.Province = address.Province.Trim();
            existing.PostalCode = address.PostalCode.Trim();
            _db.SaveChanges();
            TempData["Success"] = "แก้ไขที่อยู่จัดส่งเรียบร้อยแล้ว";
        }
        return RedirectToAction(nameof(Profile));
    }

    // 7. ลบที่อยู่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteAddress(int addressId)
    {
        int? currentUserId = HttpContext.Session.GetInt32("UserId");
        var addr = _db.Addresses.Find(addressId);
        if (addr != null && addr.UserId == currentUserId)
        {
            if (_db.Orders.Any(o => o.AddressId == addressId))
            {
                TempData["Error"] = "ไม่สามารถลบที่อยู่นี้ได้ เนื่องจากถูกใช้งานในประวัติคำสั่งซื้อแล้ว (คุณสามารถกดปุ่ม 'แก้ไข' เพื่อปรับปรุงข้อมูลที่อยู่แทนได้)";
                return RedirectToAction(nameof(Profile));
            }

            _db.Addresses.Remove(addr);
            _db.SaveChanges();
            TempData["Success"] = "ลบที่อยู่จัดส่งเรียบร้อยแล้ว";
        }
        return RedirectToAction(nameof(Profile));
    }
}