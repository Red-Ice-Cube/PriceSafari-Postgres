using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class CoOfrClass
{
    [Key]
    public int Id { get; set; }
    public string OfferUrl { get; set; }
    public List<int> ProductIds { get; set; } = new List<int>();
}
