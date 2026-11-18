using System;
using System.Collections.Generic;

namespace PetParadise.Models;

public partial class Order
{
    public int OrderId { get; set; }

    public int UserId { get; set; }

    public int AddressId { get; set; }

    public int? PromotionId { get; set; }

    public DateTime? OrderDate { get; set; }

    public decimal Subtotal { get; set; }

    public decimal? Discount { get; set; }

    public decimal? ShippingFee { get; set; }

    public decimal? Vat { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = null!;

    public virtual Address Address { get; set; } = null!;

    public virtual ICollection<Orderitem> Orderitems { get; set; } = new List<Orderitem>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Promotion? Promotion { get; set; }

    public virtual User User { get; set; } = null!;
}
