using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PetParadise.Models;

public class PromotionEvaluationResult
{
    public int PromotionId { get; set; }
    public bool IsEligible { get; set; }
    public decimal DiscountAmount { get; set; }
    public bool IsFreeShipping { get; set; }
    public string ConditionText { get; set; } = "";
    public string IneligibleReason { get; set; } = "";
}

public class WarehouseIssueNote
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string IssueType { get; set; } = ""; // สินค้าชำรุด / วันหมดอายุ / สินค้าไม่เพียงพอ
    public string Detail { get; set; } = "";
    public string ReportedBy { get; set; } = "";
    public DateTime ReportedAt { get; set; }
    public bool IsResolved { get; set; }
}

public class DeliveryTrackingNote
{
    public int OrderId { get; set; }
    public string TrackingNumber { get; set; } = "";
    public string CarrierName { get; set; } = "";
    public string DeliveryIssue { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public class TopSellingProductSummary
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int TotalQuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
    public int StockQuantity { get; set; }
}

public class PetVerificationNote
{
    public int PetId { get; set; }
    public string ImageUrl { get; set; } = "";
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? Remark { get; set; }
}

public class SystemNotesData
{
    public List<WarehouseIssueNote> WarehouseIssues { get; set; } = new();
    public List<DeliveryTrackingNote> DeliveryNotes { get; set; } = new();
    public Dictionary<int, string> PromoCodes { get; set; } = new();
    public List<PetVerificationNote> PetVerifications { get; set; } = new();
}

public static class PromotionHelper
{
    // ดึงโค้ดโปรโมชั่น (Promo Code) ของแต่ละโปรโมชั่น
    public static string GetPromoCode(Promotion promo, SystemNotesData? notes = null)
    {
        notes ??= LoadSystemNotes();
        if (notes.PromoCodes != null && notes.PromoCodes.TryGetValue(promo.PromotionId, out var customCode) && !string.IsNullOrWhiteSpace(customCode))
        {
            return customCode.Trim().ToUpperInvariant();
        }

        return promo.PromotionType switch
        {
            "FirstOrder" => "NEW10",
            "Fixed" => promo.DiscountAmount != null ? $"SAVE{promo.DiscountAmount.Value:0}" : $"SAVE{promo.PromotionId}",
            "BulkItem" => "BULK15",
            "FreeShipping" => "FREESHIP",
            "BirthdayPet" => "HBD20",
            _ => $"PROMO{promo.PromotionId}"
        };
    }

    public static void SetPromoCode(int promotionId, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var notes = LoadSystemNotes();
        notes.PromoCodes ??= new Dictionary<int, string>();
        notes.PromoCodes[promotionId] = code.Trim().ToUpperInvariant();
        SaveSystemNotes(notes);
    }

    public static Promotion? FindPromotionByCode(string? inputCode, IEnumerable<Promotion> promotions)
    {
        if (string.IsNullOrWhiteSpace(inputCode)) return null;
        string normalized = inputCode.Trim().TrimStart('#').ToUpperInvariant();
        var notes = LoadSystemNotes();

        foreach (var p in promotions)
        {
            string code = GetPromoCode(p, notes);
            if (string.Equals(code, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"PROMO{p.PromotionId}", normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.PromotionId.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }
        return null;
    }
    // คำนวณค่าจัดส่งตามช่วงน้ำหนักที่กำหนดในสเปก:
    // ไม่เกิน 1 กิโลกรัม = 40 บาท
    // 1.01 - 3 กิโลกรัม = 60 บาท
    // 3.01 - 5 กิโลกรัม = 80 บาท
    // มากกว่า 5 กิโลกรัม = 100 บาท
    public static decimal CalculateShippingFee(decimal totalWeight)
    {
        if (totalWeight <= 0m) return 0m;
        if (totalWeight <= 1m) return 40m;
        if (totalWeight <= 3m) return 60m;
        if (totalWeight <= 5m) return 80m;
        return 100m;
    }

    // สร้างโปรโมชั่นหลักทั้ง 5 รูปแบบตามที่ออกแบบไว้หากยังไม่มีในฐานข้อมูล
    public static void EnsureDefaultPromotions(Csi402dbContext db)
    {
        var start = DateOnly.FromDateTime(DateTime.Now.AddMonths(-1));
        var end = DateOnly.FromDateTime(DateTime.Now.AddYears(1));
        bool changed = false;

        if (!db.Promotions.Any(p => p.PromotionType == "FirstOrder"))
        {
            db.Promotions.Add(new Promotion
            {
                PromotionName = "ส่วนลดสมาชิกใหม่ (ลด 10% ครั้งแรก)",
                PromotionType = "FirstOrder",
                DiscountPercent = 10m,
                DiscountAmount = null,
                MinPurchase = 500m,
                MaxDiscount = 100m,
                StartDate = start,
                EndDate = end,
                Status = true
            });
            changed = true;
        }

        if (!db.Promotions.Any(p => p.PromotionType == "Fixed" && p.MinPurchase == 1000m))
        {
            db.Promotions.Add(new Promotion
            {
                PromotionName = "ซื้อครบลดทันที (ครบ 1,000 ลด 150 บาท)",
                PromotionType = "Fixed",
                DiscountPercent = null,
                DiscountAmount = 150m,
                MinPurchase = 1000m,
                MaxDiscount = 150m,
                StartDate = start,
                EndDate = end,
                Status = true
            });
            changed = true;
        }

        if (!db.Promotions.Any(p => p.PromotionType == "BulkItem"))
        {
            db.Promotions.Add(new Promotion
            {
                PromotionName = "ซื้อจำนวนมากลดเพิ่ม (สินค้าเดียวกัน 3 ชิ้นขึ้นไป ลด 15%)",
                PromotionType = "BulkItem",
                DiscountPercent = 15m,
                DiscountAmount = null,
                MinPurchase = 0m,
                MaxDiscount = null,
                StartDate = start,
                EndDate = end,
                Status = true
            });
            changed = true;
        }

        if (!db.Promotions.Any(p => p.PromotionType == "FreeShipping"))
        {
            db.Promotions.Add(new Promotion
            {
                PromotionName = "ส่งฟรี (ซื้อครบ 799 บาท จัดส่งฟรี)",
                PromotionType = "FreeShipping",
                DiscountPercent = null,
                DiscountAmount = 0m,
                MinPurchase = 799m,
                MaxDiscount = null,
                StartDate = start,
                EndDate = end,
                Status = true
            });
            changed = true;
        }

        if (!db.Promotions.Any(p => p.PromotionType == "BirthdayPet"))
        {
            db.Promotions.Add(new Promotion
            {
                PromotionName = "Birthday Pet (ลด 20% เดือนเกิดสัตว์เลี้ยง)",
                PromotionType = "BirthdayPet",
                DiscountPercent = 20m,
                DiscountAmount = null,
                MinPurchase = 0m,
                MaxDiscount = 200m,
                StartDate = start,
                EndDate = end,
                Status = true
            });
            changed = true;
        }

        if (changed)
        {
            db.SaveChanges();
        }
    }

    // ตรวจสอบเงื่อนไขและคำนวณส่วนลดของแต่ละโปรโมชั่น
    public static PromotionEvaluationResult EvaluatePromotion(
        Promotion promo,
        List<Cartitem> items,
        int userId,
        Csi402dbContext db)
    {
        var result = new PromotionEvaluationResult
        {
            PromotionId = promo.PromotionId,
            IsEligible = false,
            DiscountAmount = 0m,
            IsFreeShipping = false
        };

        decimal subtotal = items.Sum(i => i.Quantity * i.Product.Price);
        decimal minPurchase = promo.MinPurchase ?? 0m;
        var today = DateOnly.FromDateTime(DateTime.Now);

        if (promo.Status != true || today < promo.StartDate || today > promo.EndDate)
        {
            result.IneligibleReason = "โปรโมชั่นหมดอายุหรือปิดใช้งาน";
            return result;
        }

        switch (promo.PromotionType)
        {
            case "FirstOrder":
            {
                result.ConditionText = $"สมาชิกที่สั่งซื้อครั้งแรก • ขั้นต่ำ {(minPurchase > 0 ? minPurchase : 500):N0}฿ • ลดสูงสุด {(promo.MaxDiscount ?? 100):N0}฿";
                bool hasPriorOrders = db.Orders.Any(o => o.UserId == userId && o.Status != "Cancelled");
                if (hasPriorOrders)
                {
                    result.IneligibleReason = "เฉพาะการสั่งซื้อครั้งแรกเท่านั้น";
                    return result;
                }
                if (subtotal < (minPurchase > 0 ? minPurchase : 500m))
                {
                    result.IneligibleReason = $"ยอดสั่งซื้อขั้นต่ำ {(minPurchase > 0 ? minPurchase : 500m):N0}฿";
                    return result;
                }

                decimal pct = promo.DiscountPercent ?? 10m;
                decimal disc = Math.Round(subtotal * (pct / 100m), 2);
                decimal maxD = promo.MaxDiscount ?? 100m;
                if (maxD > 0 && disc > maxD) disc = maxD;
                if (disc > subtotal) disc = subtotal;

                result.IsEligible = true;
                result.DiscountAmount = disc;
                return result;
            }

            case "Fixed":
            {
                result.ConditionText = $"ซื้อครบ {minPurchase:N0}฿ ลดทันที {(promo.DiscountAmount ?? 0):N0}฿ (1 ครั้ง/คำสั่งซื้อ)";
                if (subtotal < minPurchase)
                {
                    result.IneligibleReason = $"ยอดสั่งซื้อขั้นต่ำ {minPurchase:N0}฿";
                    return result;
                }

                decimal disc = promo.DiscountAmount ?? 0m;
                if (disc > subtotal) disc = subtotal;

                result.IsEligible = true;
                result.DiscountAmount = disc;
                return result;
            }

            case "BulkItem":
            {
                decimal pct = promo.DiscountPercent ?? 15m;
                result.ConditionText = $"ซื้อสินค้าชนิดเดียวกันตั้งแต่ 3 ชิ้นขึ้นไป ลด {pct:N0}% เฉพาะสินค้ารายการนั้น";
                var qualifyingItems = items.Where(i => i.Quantity >= 3).ToList();
                if (!qualifyingItems.Any())
                {
                    result.IneligibleReason = "ต้องมีสินค้าชนิดเดียวกันอย่างน้อย 3 ชิ้น";
                    return result;
                }
                if (subtotal < minPurchase)
                {
                    result.IneligibleReason = $"ยอดสั่งซื้อขั้นต่ำ {minPurchase:N0}฿";
                    return result;
                }

                decimal bulkSubtotal = qualifyingItems.Sum(i => i.Quantity * i.Product.Price);
                decimal disc = Math.Round(bulkSubtotal * (pct / 100m), 2);
                if (promo.MaxDiscount != null && promo.MaxDiscount > 0 && disc > promo.MaxDiscount.Value)
                {
                    disc = promo.MaxDiscount.Value;
                }
                if (disc > subtotal) disc = subtotal;

                result.IsEligible = true;
                result.DiscountAmount = disc;
                return result;
            }

            case "FreeShipping":
            {
                decimal reqMin = minPurchase > 0 ? minPurchase : 799m;
                result.ConditionText = $"ยอดสั่งซื้อหลังหักส่วนลดไม่น้อยกว่า {reqMin:N0}฿ รับสิทธิ์จัดส่งฟรีภายในประเทศ";
                if (subtotal < reqMin)
                {
                    result.IneligibleReason = $"ยอดสั่งซื้อขั้นต่ำ {reqMin:N0}฿";
                    return result;
                }

                result.IsEligible = true;
                result.DiscountAmount = 0m;
                result.IsFreeShipping = true;
                return result;
            }

            case "BirthdayPet":
            {
                decimal pct = promo.DiscountPercent ?? 20m;
                decimal maxD = promo.MaxDiscount ?? 200m;
                result.ConditionText = $"ลด {pct:N0}% ในเดือนเกิดสัตว์เลี้ยงที่ลงทะเบียนและผ่านการยืนยันรูปภาพแล้ว • ลดสูงสุด {maxD:N0}฿";
                int currentMonth = DateTime.Now.Month;
                var birthdayPets = db.Pets
                    .Where(p => p.UserId == userId && p.BirthDate != null && p.BirthDate.Value.Month == currentMonth)
                    .ToList();

                if (!birthdayPets.Any())
                {
                    result.IneligibleReason = "ไม่มีสัตว์เลี้ยงที่เกิดในเดือนนี้ (ลงทะเบียนพร้อมแนบรูปได้ที่หน้าโปรไฟล์)";
                    return result;
                }

                var notes = LoadSystemNotes();
                bool hasApprovedPet = birthdayPets.Any(p =>
                {
                    var v = GetPetVerification(p.PetId, notes);
                    return v != null && v.Status == "Approved";
                });

                if (!hasApprovedPet)
                {
                    bool hasPendingPet = birthdayPets.Any(p =>
                    {
                        var v = GetPetVerification(p.PetId, notes);
                        return v == null || v.Status == "Pending";
                    });

                    result.IneligibleReason = hasPendingPet
                        ? "ข้อมูลและรูปสัตว์เลี้ยงของคุณอยู่ระหว่างรอเจ้าหน้าที่ (Staff) ตรวจสอบและกดยืนยัน"
                        : "รูปสัตว์เลี้ยงไม่ผ่านการยืนยัน กรุณาลบและลงทะเบียนสัตว์เลี้ยงพร้อมแนบรูปใหม่อีกครั้ง";
                    return result;
                }

                if (subtotal < minPurchase)
                {
                    result.IneligibleReason = $"ยอดสั่งซื้อขั้นต่ำ {minPurchase:N0}฿";
                    return result;
                }

                decimal disc = Math.Round(subtotal * (pct / 100m), 2);
                if (maxD > 0 && disc > maxD) disc = maxD;
                if (disc > subtotal) disc = subtotal;

                result.IsEligible = true;
                result.DiscountAmount = disc;
                return result;
            }

            default: // "Percent" หรือประเภทอื่นๆ
            {
                decimal pct = promo.DiscountPercent ?? 0m;
                result.ConditionText = $"ลด {pct:N0}% • ขั้นต่ำ {minPurchase:N0}฿" + (promo.MaxDiscount > 0 ? $" • สูงสุด {promo.MaxDiscount:N0}฿" : "");
                if (subtotal < minPurchase)
                {
                    result.IneligibleReason = $"ยอดสั่งซื้อขั้นต่ำ {minPurchase:N0}฿";
                    return result;
                }

                decimal disc = Math.Round(subtotal * (pct / 100m), 2);
                if (promo.MaxDiscount != null && promo.MaxDiscount > 0 && disc > promo.MaxDiscount.Value)
                {
                    disc = promo.MaxDiscount.Value;
                }
                if (disc > subtotal) disc = subtotal;

                result.IsEligible = true;
                result.DiscountAmount = disc;
                return result;
            }
        }
    }

    // จัดการบันทึกแจ้งปัญหาคลังสินค้าและข้อมูลเลขพัสดุจัดส่ง
    private static string GetNotesFilePath()
    {
        string folder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "system_notes.json");
    }

    public static SystemNotesData LoadSystemNotes()
    {
        string path = GetNotesFilePath();
        if (!File.Exists(path)) return new SystemNotesData();
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SystemNotesData>(json) ?? new SystemNotesData();
        }
        catch
        {
            return new SystemNotesData();
        }
    }

    public static void SaveSystemNotes(SystemNotesData data)
    {
        string path = GetNotesFilePath();
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static PetVerificationNote? GetPetVerification(int petId, SystemNotesData? notes = null)
    {
        notes ??= LoadSystemNotes();
        return notes.PetVerifications?.FirstOrDefault(p => p.PetId == petId);
    }

    public static void SavePetVerification(int petId, string? imageUrl, string status = "Pending", string? verifiedBy = null, string? remark = null)
    {
        var notes = LoadSystemNotes();
        notes.PetVerifications ??= new List<PetVerificationNote>();
        var existing = notes.PetVerifications.FirstOrDefault(p => p.PetId == petId);
        if (existing == null)
        {
            existing = new PetVerificationNote
            {
                PetId = petId,
                ImageUrl = imageUrl ?? "",
                Status = status,
                VerifiedBy = verifiedBy,
                VerifiedAt = status != "Pending" ? DateTime.Now : null,
                Remark = remark
            };
            notes.PetVerifications.Add(existing);
        }
        else
        {
            if (!string.IsNullOrEmpty(imageUrl)) existing.ImageUrl = imageUrl;
            existing.Status = status;
            existing.VerifiedBy = verifiedBy;
            existing.VerifiedAt = DateTime.Now;
            existing.Remark = remark;
        }
        SaveSystemNotes(notes);
    }

    public static void RemovePetVerification(int petId)
    {
        var notes = LoadSystemNotes();
        if (notes.PetVerifications != null)
        {
            notes.PetVerifications.RemoveAll(p => p.PetId == petId);
            SaveSystemNotes(notes);
        }
    }
}
