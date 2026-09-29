using Microsoft.AspNetCore.Mvc;
using OrderApi.Models;
using OrderApi.Services;

namespace OrderApi.Controllers;

// LEARNING NOTE — [Route("api/[controller]")]
// [controller] is a token that gets replaced with the controller class name
// minus the word "Controller". So ProductsController → "products".
// Full URL becomes: /api/products
// You had "api/[productController]" before — that's not a valid token, it
// would literally put "productController" in the URL instead of "products".

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    // LEARNING NOTE — [HttpPost] with a named sub-route
    // POST /api/products creates a new product.
    // We return CreatedAtAction so the response includes a Location header
    // pointing to where the new resource can be fetched. This is the HTTP
    // convention for 201 Created responses.
    [HttpPost]
    public IActionResult Create([FromBody] Product product)
    {
        _productService.Add(product);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    // GET /api/products/{id}
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id)
    {
        var product = _productService.GetById(id);
        if (product is null)
            return NotFound(new { message = $"Product {id} not found." });

        return Ok(product);
    }

    // GET /api/products
    [HttpGet]
    public IActionResult GetAll() => Ok(_productService.GetAll());
}
