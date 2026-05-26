using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LibraryAPI.Models;
using LibraryAPI.Data;

namespace LibraryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoansController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public LoansController(LibraryDbContext db) => _db = db;

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(int userId)
        {
            var loans = await _db.Loans
                .Include(l => l.Book).ThenInclude(b => b.Author)
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.BorrowDate)
                .Select(l => new {
                    l.Id, l.BorrowDate, l.DueDate, l.ReturnDate,
                    Status = l.Status.ToString(),
                    l.RenewalCount, l.LastRenewedAt,
                    Book = new { l.Book.Id, l.Book.Title, Author = l.Book.Author == null ? "" : l.Book.Author.Name },
                    HasReview = l.Review != null
                }).ToListAsync();
            return Ok(loans);
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
        {
            var loans = await _db.Loans
                .Include(l => l.User).Include(l => l.Book)
                .Where(l => l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue)
                .OrderBy(l => l.DueDate)
                .Select(l => new {
                    l.Id, l.BorrowDate, l.DueDate,
                    Status = l.Status.ToString(),
                    MemberName = l.User.Name, MemberId = l.UserId,
                    BookTitle = l.Book.Title, BookId = l.BookId
                }).ToListAsync();
            return Ok(loans);
        }

        [HttpPost("borrow")]
        public async Task<IActionResult> Borrow(BorrowDto dto)
        {
            try
            {
                var user = await _db.Users.FindAsync(dto.UserId);
                if (user == null || !user.IsActive)
                    return BadRequest(new { message = "User not found or inactive." });
                
                if (user.BorrowSuspendedUntil.HasValue && user.BorrowSuspendedUntil.Value.Date >= DateTime.UtcNow.Date)
                    return BadRequest(new { message = $"Borrowing is suspended until {user.BorrowSuspendedUntil:dd/MM/yyyy}. Return overdue books to lift the suspension." });
                var unpaidFines = await _db.Fines
                    .Where(f => f.UserId == dto.UserId &&
                           (f.Status == FineStatus.Unpaid || f.Status == FineStatus.Pending))
                    .ToListAsync();
                var totalUnpaid = unpaidFines.Sum(f => f.AmountDue - f.AmountPaid);
                var policy = await _db.FinePolicies.FirstOrDefaultAsync();
                if (policy != null && totalUnpaid >= policy.BorrowBlockThreshold)
                    return BadRequest(new { message = $"You have €{totalUnpaid:F2} in unpaid fines. Please settle them before borrowing." });

                var activeCount = await _db.Loans
                    .CountAsync(l => l.UserId == dto.UserId && l.Status == LoanStatus.Active);
                if (activeCount >= 5)
                    return BadRequest(new { message = "You have reached the maximum of 5 active loans." });

                var book = await _db.Books.FindAsync(dto.BookId);
                if (book == null || !book.IsActive)
                    return NotFound(new { message = "Book not found." });
                if (book.AvailableCopies <= 0)
                    return BadRequest(new { message = "No copies available. You can reserve this book instead." });

                var days = dto.Days.HasValue ? Math.Clamp(dto.Days.Value, 1, 30) : 14;

                var reservation = await _db.Reservations.FirstOrDefaultAsync(r =>
                    r.UserId == dto.UserId && r.BookId == dto.BookId && r.Status == ReservationStatus.Pending);
                if (reservation != null)
                    reservation.Status = ReservationStatus.Fulfilled;

                book.AvailableCopies--;
                var loan = new Loan
                {
                    UserId    = dto.UserId, BookId = dto.BookId,
                    BorrowDate = DateTime.UtcNow,
                    DueDate    = DateTime.UtcNow.AddDays(days),
                    Status     = LoanStatus.Active
                };
                _db.Loans.Add(loan);
                await _db.SaveChangesAsync();

                return Ok(new { loan.Id, loan.BorrowDate, loan.DueDate, Days = days, message = "Book borrowed successfully!" });
            }
            catch (Exception ex)
            {
                // Surface the full inner exception so the front-end can show a useful error
                var detail = ex.Message;
                var inner = ex.InnerException;
                while (inner != null)
                {
                    detail += " | INNER: " + inner.Message;
                    inner = inner.InnerException;
                }
                return StatusCode(500, new { message = detail });
            }
        }

        [HttpPost("return/{loanId}")]
        public async Task<IActionResult> Return(int loanId, ReturnDto dto)
        {
            var loan = await _db.Loans
                .Include(l => l.Book).Include(l => l.Fine)
                .FirstOrDefaultAsync(l => l.Id == loanId);
            if (loan == null) return NotFound(new { message = "Loan not found." });
            if (loan.Status == LoanStatus.Returned)
                return BadRequest(new { message = "Already returned." });

            loan.ReturnDate = DateTime.UtcNow;
            loan.Status     = LoanStatus.Returned;
            loan.Book.AvailableCopies++;

            // ── Damage assessment ───────────────────────────────────────
            // Save the condition and look up the matching fee from FinePolicy.
            var damagePolicy = await _db.FinePolicies.FirstOrDefaultAsync();
            decimal damageFee = 0m;
            if (dto.Condition.HasValue && Enum.IsDefined(typeof(BookCondition), dto.Condition.Value))
            {
                var cond = (BookCondition)dto.Condition.Value;
                loan.ReturnCondition = cond;
                loan.ConditionNotes  = dto.Notes;

                if (damagePolicy != null)
                {
                    damageFee = cond switch
                    {
                        BookCondition.Good                => 0m,
                        BookCondition.MinorWear           => damagePolicy.MinorWearFee,
                        BookCondition.SignificantDamage   => damagePolicy.SignificantDamageFee,
                        BookCondition.MajorDamage         => damagePolicy.MajorDamageFee,
                        BookCondition.Lost                => damagePolicy.LostBookFee,
                        _                                 => 0m
                    };
                }
                loan.DamageFee = damageFee;

                if (damageFee > 0)
                {
                    // Fine is 1-to-1 with Loan. If an overdue fine exists, add the damage fee to it.
                    // Otherwise create a new Fine record for the damage.
                    if (loan.Fine != null)
                    {
                        loan.Fine.AmountDue += damageFee;
                        // Status stays as-is (Unpaid or whatever it was)
                    }
                    else
                    {
                        _db.Fines.Add(new Fine
                        {
                            LoanId    = loan.Id,
                            UserId    = loan.UserId,
                            AmountDue = damageFee,
                            Status    = FineStatus.Unpaid
                        });
                    }
                    _db.Notifications.Add(new Notification
                    {
                        UserId  = loan.UserId,
                        Message = $"A damage fee of €{damageFee:F2} has been issued for \"{loan.Book.Title}\" (condition: {cond})."
                                  + (string.IsNullOrWhiteSpace(dto.Notes) ? "" : $" Notes: {dto.Notes}")
                    });
                }

                // If the book is reported lost, remove a copy from circulation.
                if (cond == BookCondition.Lost)
                {
                    loan.Book.AvailableCopies--;          // revert the +1 above
                    if (loan.Book.TotalCopies > 0) loan.Book.TotalCopies--;
                }
            }

            var policy     = await _db.FinePolicies.FirstOrDefaultAsync();
            var graceDays  = policy?.GracePeriodDays ?? 1;
            var dailyRate  = policy?.DailyRate ?? 0.50m;
            var overdueDays = (int)(loan.ReturnDate.Value.Date - loan.DueDate.Date).TotalDays - graceDays;

            if (overdueDays > 0)
{
    var finalAmount = overdueDays * dailyRate;
    if (loan.Fine == null)
    {
        _db.Fines.Add(new Fine
        {
            LoanId = loan.Id, UserId = loan.UserId,
            AmountDue = finalAmount, Status = FineStatus.Unpaid
        });
        _db.Notifications.Add(new Notification
        {
            UserId  = loan.UserId,
            Message = $"A fine of €{finalAmount:F2} has been issued for late return of \"{loan.Book.Title}\" ({overdueDays} days overdue)."
        });
    }
    else if (loan.Fine.Status == FineStatus.Unpaid)
    {
        // Lock the final amount; no more daily growth after return
        loan.Fine.AmountDue = finalAmount;
    }
}

// Lift suspension if user has no other overdue loans
var stillOverdue = await _db.Loans
    .AnyAsync(l => l.UserId == loan.UserId
                && l.Id != loan.Id
                && l.Status != LoanStatus.Returned
                && l.DueDate.Date < DateTime.UtcNow.Date);
if (!stillOverdue)
{
    var user = await _db.Users.FindAsync(loan.UserId);
    if (user != null && user.BorrowSuspendedUntil != null)
    {
        user.BorrowSuspendedUntil = null;
        _db.Notifications.Add(new Notification
        {
            UserId = loan.UserId,
            Message = "Your borrowing privileges have been restored. Thank you for returning the book."
        });
    }
}

            if (loan.Book.AvailableCopies == 1)
            {
                var wishlistIds = await _db.Wishlists
                    .Where(w => w.BookId == loan.BookId).Select(w => w.UserId).ToListAsync();
                foreach (var uid in wishlistIds)
                    _db.Notifications.Add(new Notification
                    {
                        UserId  = uid,
                        Message = $"\"{loan.Book.Title}\" is now available to borrow!"
                    });

                var nextRes = await _db.Reservations
                    .Where(r => r.BookId == loan.BookId && r.Status == ReservationStatus.Pending)
                    .OrderBy(r => r.QueuePosition).FirstOrDefaultAsync();
                if (nextRes != null)
                    _db.Notifications.Add(new Notification
                    {
                        UserId  = nextRes.UserId,
                        Message = $"Your reserved book \"{loan.Book.Title}\" is now available! Come pick it up."
                    });
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "Book returned successfully.", overdueDays = Math.Max(0, overdueDays) });
        }

        [HttpPost("{loanId}/renew")]
        public async Task<IActionResult> Renew(int loanId)
        {
            const int MaxRenewals = 2;
            const int RenewalDays = 14;

            var loan = await _db.Loans
                .Include(l => l.Book)
                .Include(l => l.Fine)
                .FirstOrDefaultAsync(l => l.Id == loanId);

            if (loan == null)
                return NotFound(new { message = "Loan not found." });

            if (loan.Status == LoanStatus.Returned)
                return BadRequest(new { message = "Cannot renew a returned loan." });

            if (loan.RenewalCount >= MaxRenewals)
                return BadRequest(new { message = $"Maximum {MaxRenewals} renewals reached for this loan." });

            var unpaidFines = await _db.Fines
                .Where(f => f.UserId == loan.UserId &&
                       (f.Status == FineStatus.Unpaid || f.Status == FineStatus.Pending))
                .ToListAsync();
            var totalUnpaid = unpaidFines.Sum(f => f.AmountDue - f.AmountPaid);
            var policy = await _db.FinePolicies.FirstOrDefaultAsync();
            if (policy != null && totalUnpaid >= policy.BorrowBlockThreshold)
                return BadRequest(new { message = $"You have €{totalUnpaid:F2} in unpaid fines. Please settle them before renewing." });

            var hasReservation = await _db.Reservations.AnyAsync(r =>
                r.BookId == loan.BookId && r.Status == ReservationStatus.Pending && r.UserId != loan.UserId);
            if (hasReservation)
                return BadRequest(new { message = "Cannot renew — another member has reserved this book." });

            loan.DueDate       = loan.DueDate.AddDays(RenewalDays);
            loan.RenewalCount += 1;
            loan.LastRenewedAt = DateTime.UtcNow;
            if (loan.Status == LoanStatus.Overdue)
                loan.Status = LoanStatus.Active;

            _db.Notifications.Add(new Notification
            {
                UserId  = loan.UserId,
                Message = $"Your loan of \"{loan.Book.Title}\" has been renewed for {RenewalDays} days. New due date: {loan.DueDate:MMM dd, yyyy}."
            });

            await _db.SaveChangesAsync();
            return Ok(new
            {
                message      = $"Loan renewed! New due date: {loan.DueDate:MMM dd, yyyy}.",
                newDueDate   = loan.DueDate,
                renewalCount = loan.RenewalCount,
                renewalsLeft = MaxRenewals - loan.RenewalCount
            });
        }
    }

    public record BorrowDto(int UserId, int BookId, int? Days = null);
    public record ReturnDto(int StaffId, int? Condition = null, string? Notes = null);
}