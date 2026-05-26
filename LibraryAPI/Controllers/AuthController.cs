using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LibraryAPI.Models;
using LibraryAPI.Data;

namespace LibraryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public AuthController(LibraryDbContext db) => _db = db;

        // POST api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
                return BadRequest(new { message = "Email already exists!" });

            var user = new User
            {
                Name         = dto.Name,
                Email        = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role         = UserRole.Member
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Ok(new { userId = user.Id, name = user.Name, email = user.Email, role = user.Role.ToString() });
        }

        // POST api/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid email or password!" });

            if (!user.IsActive)
                return Unauthorized(new { message = "Account is deactivated. Contact an administrator." });

            return Ok(new { userId = user.Id, name = user.Name, email = user.Email, role = user.Role.ToString() });
        }

        // GET api/auth/profile/{userId}
        [HttpGet("profile/{userId}")]
        public async Task<IActionResult> GetProfile(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return NotFound();
            return Ok(new { user.Id, user.Name, user.Email, role = user.Role.ToString(), user.ReadingGoal, user.CreatedAt });
        }

        // GET api/auth/users  — used by staff dashboard to search members for check-out
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _db.Users
                .Select(u => new { u.Id, u.Name, u.Email, role = u.Role.ToString(), u.IsActive, u.CreatedAt })
                .ToListAsync();
            return Ok(users);
        }

        // PUT api/auth/profile/{userId}
        [HttpPut("profile/{userId}")]
        public async Task<IActionResult> UpdateProfile(int userId, UpdateProfileDto dto)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return NotFound();
            if (!string.IsNullOrWhiteSpace(dto.Name)) user.Name = dto.Name;
            if (dto.ReadingGoal.HasValue) user.ReadingGoal = dto.ReadingGoal;
            await _db.SaveChangesAsync();
            return Ok(new { user.Id, user.Name, user.ReadingGoal });
        }

        // POST api/auth/change-password/{userId}
        [HttpPost("change-password/{userId}")]
        public async Task<IActionResult> ChangePassword(int userId, ChangePasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { message = "Both fields are required." });

            if (dto.NewPassword.Length < 6)
                return BadRequest(new { message = "New password must be at least 6 characters." });

            var user = await _db.Users.FindAsync(userId);
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest(new { message = "Current password is incorrect." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Password changed successfully." });
        }
    }

    // ─── DTOs ─────────────────────────────────────────────────────────────────
    public record RegisterDto(string Name, string Email, string Password);
    public record LoginDto(string Email, string Password);
    public record UpdateProfileDto(string? Name, int? ReadingGoal);
    public record ChangePasswordDto(string CurrentPassword, string NewPassword);
}