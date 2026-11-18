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
        PromotionHelper.EnsureDefaultPromotions(_db);

        var promos = (from p in _db.Promotions
                      where p.Status == true
                      orderby p.PromotionId
                      select p).ToList();

        return View(promos);
    }
}
