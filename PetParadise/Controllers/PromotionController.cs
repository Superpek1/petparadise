using Microsoft.AspNetCore.Mvc;
using PetParadise.Models;

namespace PetParadise.Controllers;

public class PromotionController : Controller
{
    private readonly Csi402dbContext _db;

    public PromotionController(Csi402dbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Index()
    {
        // ใช้ p.Status ตามโมเดลจริง
        var promos = (from p in _db.Promotions
                      where p.Status == true
                      select p).ToList();

        return View(promos);
    }
}