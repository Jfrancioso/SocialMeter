using Microsoft.AspNetCore.Mvc;
using SocialMeter.Services;

namespace SocialMeter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PracticeController : ControllerBase
{
    private readonly PracticeHttpService _practiceService;  // ← Same pattern!

    public PracticeController(PracticeHttpService practiceService)  // ← Same pattern!
    {
        _practiceService = practiceService;
    }

    [HttpGet("catfact")]
    public async Task<IActionResult> GetCatFact()
    {
        // Same pattern as your HomeController!
        var fact = await _practiceService.GetRandomCatFactAsync();
        return Ok(new { fact });
    }
}