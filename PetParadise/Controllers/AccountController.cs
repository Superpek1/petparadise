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

    // 1. หน้าเข้าสู่ระบบ (Login) พร้อมสร้างบัญชี Admin เริ่มต้นอัตโนมัติ
    [HttpGet]
    public IActionResult Login()
    {
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

            // แยกเส้นทางหลังล็อกอินตาม Role ทั้ง 5 ตำแหน่ง
            return user.Role switch
            {
                "Admin" => RedirectToAction("Dashboard", "Admin"),
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

    // 4.2 ลงทะเบียนข้อมูลสัตว์เลี้ยง (สำหรับโปรโมชั่น Birthday Pet)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddPet(int userId, string petName, string petType, DateOnly? birthDate)
    {
        int? currentUserId = HttpContext.Session.GetInt32("UserId");
        if (currentUserId == null || currentUserId != userId)
            return RedirectToAction(nameof(Login));

        if (!string.IsNullOrWhiteSpace(petName) && !string.IsNullOrWhiteSpace(petType))
        {
            _db.Pets.Add(new Pet
            {
                UserId = userId,
                PetName = petName.Trim(),
                PetType = petType.Trim(),
                BirthDate = birthDate
            });
            _db.SaveChanges();
            TempData["Success"] = $"ลงทะเบียนสัตว์เลี้ยง '{petName}' เรียบร้อยแล้ว";
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
        }

        return RedirectToAction(nameof(Profile));
    }

    // 6. แก้ไขที่อยู่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult EditAddress(Address address)
    {
        var existing = _db.Addresses.Find(address.AddressId);
        if (existing != null)
        {
            existing.RecipientName = address.RecipientName;
            existing.Phone = address.Phone;
            existing.AddressDetail = address.AddressDetail;
            existing.District = address.District;
            existing.Province = address.Province;
            existing.PostalCode = address.PostalCode;
            _db.SaveChanges();
        }
        return RedirectToAction(nameof(Profile));
    }

    // 7. ลบที่อยู่
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteAddress(int addressId)
    {
        var addr = _db.Addresses.Find(addressId);
        if (addr != null && !_db.Orders.Any(o => o.AddressId == addressId))
        {
            _db.Addresses.Remove(addr);
            _db.SaveChanges();
        }
        return RedirectToAction(nameof(Profile));
    }
}