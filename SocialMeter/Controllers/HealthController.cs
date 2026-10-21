using Microsoft.AspNetCore.Mvc;

namespace SocialMeter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", timeUtc = DateTime.UtcNow });
}
