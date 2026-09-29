namespace OrderApi.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // to make it default empty
    public decimal Price { get; set; }
    public int Stock { get; set; }
}