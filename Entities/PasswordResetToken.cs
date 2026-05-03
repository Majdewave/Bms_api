using System;
using System.ComponentModel.DataAnnotations;

namespace Clienta.Api.Entities
{
    public class PasswordResetToken
    {
        [Key]
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        [Required]
        public string Token { get; set; } = default!;

        public DateTime ExpiresAt { get; set; }

        public bool Used { get; set; } = false;

        public User User { get; set; } = default!;
    }
}
