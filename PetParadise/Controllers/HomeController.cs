using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
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
        return View();
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