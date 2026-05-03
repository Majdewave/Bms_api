using Microsoft.Extensions.Caching.Memory;

public class ResetRateLimiter
{
    private readonly IMemoryCache _cache;

    public ResetRateLimiter(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool CanRequest(string email, string ip)
    {
        var emailKey = $"reset_email_{email}";
        var ipKey = $"reset_ip_{ip}";

        // בדיקה לפי אימייל
        if (_cache.TryGetValue<int>(emailKey, out var emailCount))
        {
            if (emailCount >= 3) return false;
            _cache.Set(emailKey, emailCount + 1, TimeSpan.FromHours(1));
        }
        else
        {
            _cache.Set(emailKey, 1, TimeSpan.FromHours(1));
        }

        // בדיקה לפי IP
        if (_cache.TryGetValue<int>(ipKey, out var ipCount))
        {
            if (ipCount >= 10) return false;
            _cache.Set(ipKey, ipCount + 1, TimeSpan.FromHours(1));
        }
        else
        {
            _cache.Set(ipKey, 1, TimeSpan.FromHours(1));
        }

        return true;
    }
}
