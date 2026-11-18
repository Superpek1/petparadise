using System;
using System.Collections.Generic;

namespace PetParadise.Models;

public partial class Pet
{
    public int PetId { get; set; }

    public int UserId { get; set; }

    public string PetName { get; set; } = null!;

    public string PetType { get; set; } = null!;

    public DateOnly? BirthDate { get; set; }

    public virtual User User { get; set; } = null!;
}
