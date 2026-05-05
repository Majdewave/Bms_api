using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly TrialService _trialService;

    public DebugController(TrialService trialService)
    {
        _trialService = trialService;
    }

    [HttpPost("run-trial-job")]
    public async Task<IActionResult> RunTrialJob()
    {
        await _trialService.ProcessTrialsAsync();
        return Ok("Trial job executed");
    }
}
