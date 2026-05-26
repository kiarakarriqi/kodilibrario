using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LibraryAPI.Models;
using LibraryAPI.Data;

namespace LibraryAPI.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════
    // BORROW REQUEST CONTROLLER
    // ═══════════════════════════════════════════════════════════════════════
    [ApiController]
    [Route("api/borrowrequest")]
    public class BorrowRequestController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public BorrowRequestController(LibraryDbContext db) => _db = db;

        // GET api/borrowrequest/user/{userId}
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(int userId)
        {
            var requests = await _db.BorrowRequests
                .Include(r => r.Book).ThenInclude(b => b.Author)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => new {
                    r.Id, r.RequestedAt, r.ExpiresAt, r.Days,
                    Status = r.Status.ToString(),
                    r.RejectionReason,
                    Book = new {
                        r.Book.Id, r.Book.Title,
                        Author = r.Book.Author == null ? "" : r.Book.Author.Name,
                        r.Book.AvailableCopies
                    }
                }).ToListAsync();
            return Ok(requests);
        }

        // GET api/borrowrequest/pending
        [HttpGet("pending")]
        public async Task<IActionResult> GetPending()
        {
            var requests = await _db.BorrowRequests
                .Include(r => r.User)
                .Include(r => r.Book).ThenInclude(b => b.Author)
                .Where(r => r.Status == BorrowRequestStatus.Pending &&
                            r.ExpiresAt > DateTime.UtcNow)
                .OrderBy(r => r.RequestedAt)
                .Select(r => new {
                    r.Id, r.RequestedAt, r.ExpiresAt, r.Days, r.Notes,
                    MemberName  = r.User.Name,
                    MemberEmail = r.User.Email,
                    MemberId    = r.UserId,
                    Book = new {
                        r.Book.Id, r.Book.Title,
                        Author = r.Book.Author == null ? "" : r.Book.Author.Name,
                        r.Book.AvailableCopies
                    }
                }).ToListAsync();
            return Ok(requests);
        }

        // POST api/borrowrequest — Member bën request online
        [HttpPost]
        public async Task<IActionResult> Create(CreateBorrowRequestDto dto)
        {
            var user = await _db.Users.FindAsync(dto.UserId);
            if (user == null || !user.IsActive)
                return BadRequest(new { message = "User not found or inactive." });

            var unpaidFines = await _db.Fines
                .Where(f => f.UserId == dto.UserId &&
                       (f.Status == FineStatus.Unpaid || f.Status == FineStatus.Pending))
                .ToListAsync();
            var totalFines = unpaidFines.Sum(f => f.AmountDue - f.AmountPaid);
            var policy = await _db.FinePolicies.FirstOrDefaultAsync();
            if (policy != null && totalFines >= policy.BorrowBlockThreshold)
                return BadRequest(new { message = "You have unpaid fines above the limit. Please settle them first." });

            var activeLoans = await _db.Loans
                .CountAsync(l => l.UserId == dto.UserId && l.Status == LoanStatus.Active);
            if (activeLoans >= 5)
                return BadRequest(new { message = "You have reached the maximum of 5 active loans." });

            var book = await _db.Books.FindAsync(dto.BookId);
            if (book == null || !book.IsActive)
                return NotFound(new { message = "Book not found." });
            if (book.AvailableCopies <= 0)
                return BadRequest(new { message = "No copies available. You can reserve this book instead." });

            var existingRequest = await _db.BorrowRequests.AnyAsync(r =>
                r.UserId == dto.UserId && r.BookId == dto.BookId &&
                r.Status == BorrowRequestStatus.Pending);
            if (existingRequest)
                return BadRequest(new { message = "You already have a pending request for this book." });

            var days = dto.Days.HasValue ? Math.Clamp(dto.Days.Value, 1, 30) : 14;

            var request = new BorrowRequest
            {
                UserId    = dto.UserId,
                BookId    = dto.BookId,
                Days      = days,
                Notes     = dto.Notes,
                ExpiresAt = DateTime.UtcNow.AddHours(48)
            };
            _db.BorrowRequests.Add(request);

            // Reserve the copy immediately so it cannot be borrowed by someone else
            // while the request is pending. Copy is restored on rejection or expiry.
            book.AvailableCopies--;

            _db.Notifications.Add(new Notification {
                UserId  = dto.UserId,
                Message = "Your request for \"" + book.Title + "\" has been submitted. Please come to the library desk within 48 hours. Bring a cash deposit."
            });

            // Notify every Staff and Admin user that a new request needs review
            var staffUsers = await _db.Users
                .Where(u => u.IsActive && (u.Role == UserRole.Staff || u.Role == UserRole.Admin))
                .Select(u => u.Id)
                .ToListAsync();
            foreach (var staffId in staffUsers)
            {
                _db.Notifications.Add(new Notification {
                    UserId  = staffId,
                    Message = "New borrow request: \"" + book.Title + "\" by " + user.Name + "."
                });
            }

            await _db.SaveChangesAsync();
            return Ok(new {
                request.Id,
                request.ExpiresAt,
                message = "Request submitted! Come to the library desk within 48 hours to collect \"" + book.Title + "\". Bring a cash deposit."
            });
        }

        // POST api/borrowrequest/{id}/approve — Staff lëshon librin fizikisht
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(int id, ApproveRequestDto dto)
        {
            try
            {
                var request = await _db.BorrowRequests
                    .Include(r => r.Book)
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (request == null) return NotFound();
                if (request.Status != BorrowRequestStatus.Pending)
                    return BadRequest(new { message = "Request is no longer pending." });
                // Note: the copy was already reserved (AvailableCopies decremented) when the
                // request was created. We do NOT decrement again here.

                var loan = new Loan {
                    UserId     = request.UserId,
                    BookId     = request.BookId,
                    BorrowDate = DateTime.UtcNow,
                    DueDate    = DateTime.UtcNow.AddDays(request.Days),
                    Status     = LoanStatus.Active
                };
                _db.Loans.Add(loan);
                // (no AvailableCopies change here — already reserved at request time)

                request.Status             = BorrowRequestStatus.Approved;
                request.ProcessedAt        = DateTime.UtcNow;
                request.ProcessedByStaffId = dto.StaffId;

                await _db.SaveChangesAsync();

                request.LoanId = loan.Id;

                // Regjistro depozitën nëse ka
                if (dto.DepositAmount > 0)
                {
                    _db.Deposits.Add(new Deposit {
                        LoanId             = loan.Id,
                        MemberId           = request.UserId,
                        Amount             = dto.DepositAmount,
                        CollectedByStaffId = dto.StaffId,
                        Notes              = dto.DepositNotes
                    });
                }

                var depositMsg = dto.DepositAmount > 0
                    ? " Deposit held: " + dto.DepositAmount.ToString("F2") + " EUR."
                    : "";

                _db.Notifications.Add(new Notification {
                    UserId  = request.UserId,
                    Message = "Your loan for \"" + request.Book.Title + "\" has been issued. Due: " +
                              loan.DueDate.ToString("dd MMM yyyy") + "." + depositMsg
                });

                await _db.SaveChangesAsync();
                return Ok(new {
                    LoanId  = loan.Id,
                    DueDate = loan.DueDate,
                    Deposit = dto.DepositAmount,
                    message = "Book issued! Due: " + loan.DueDate.ToString("dd MMM yyyy")
                });
            }
            catch (Exception ex)
            {
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

        // POST api/borrowrequest/{id}/reject
        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(int id, RejectRequestDto dto)
        {
            var request = await _db.BorrowRequests
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (request == null) return NotFound();
            if (request.Status != BorrowRequestStatus.Pending)
                return BadRequest(new { message = "Request is no longer pending." });

            request.Status             = BorrowRequestStatus.Rejected;
            request.ProcessedAt        = DateTime.UtcNow;
            request.ProcessedByStaffId = dto.StaffId;
            request.RejectionReason    = dto.Reason;

            // Release the copy that was reserved when the request was created
            request.Book.AvailableCopies++;

            _db.Notifications.Add(new Notification {
                UserId  = request.UserId,
                Message = "Your borrow request for \"" + request.Book.Title + "\" was not approved. Reason: " + dto.Reason
            });

            await _db.SaveChangesAsync();
            return Ok(new { message = "Request rejected." });
        }

        // POST api/borrowrequest/{id}/cancel
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromQuery] int userId)
        {
            var request = await _db.BorrowRequests
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (request == null) return NotFound();
            if (request.Status != BorrowRequestStatus.Pending)
                return BadRequest(new { message = "Can only cancel pending requests." });
            request.Status = BorrowRequestStatus.Cancelled;
            // Release the reserved copy
            request.Book.AvailableCopies++;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Request cancelled." });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DEPOSITS CONTROLLER
    // ═══════════════════════════════════════════════════════════════════════
    [ApiController]
    [Route("api/deposits")]
    public class DepositsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public DepositsController(LibraryDbContext db) => _db = db;

        // GET api/deposits/held
        [HttpGet("held")]
        public async Task<IActionResult> GetHeld()
        {
            var deposits = await _db.Deposits
                .Include(d => d.Loan).ThenInclude(l => l.Book)
                .Include(d => d.Member)
                .Where(d => d.Status == DepositStatus.Held)
                .OrderByDescending(d => d.CollectedAt)
                .Select(d => new {
                    d.Id, d.Amount, d.CollectedAt, d.Notes,
                    MemberName = d.Member.Name,
                    MemberId   = d.MemberId,
                    LoanId     = d.LoanId,
                    BookTitle  = d.Loan.Book.Title,
                    DueDate    = d.Loan.DueDate,
                    IsOverdue  = d.Loan.DueDate < DateTime.UtcNow && d.Loan.Status == LoanStatus.Active,
                    LoanStatus = d.Loan.Status.ToString()
                }).ToListAsync();
            return Ok(deposits);
        }

        // POST api/deposits/{id}/return
        [HttpPost("{id}/return")]
        public async Task<IActionResult> ReturnDeposit(int id, [FromQuery] int staffId)
        {
            var deposit = await _db.Deposits
                .Include(d => d.Loan).ThenInclude(l => l.Book)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (deposit == null) return NotFound();
            if (deposit.Status != DepositStatus.Held)
                return BadRequest(new { message = "Deposit is not currently held." });
            if (deposit.Loan.Status != LoanStatus.Returned)
                return BadRequest(new { message = "Cannot return deposit — the book has not been returned yet." });

            deposit.Status            = DepositStatus.Returned;
            deposit.ReturnedAt        = DateTime.UtcNow;
            deposit.ReturnedByStaffId = staffId;

            _db.Notifications.Add(new Notification {
                UserId  = deposit.MemberId,
                Message = "Your deposit of " + deposit.Amount.ToString("F2") + " EUR for \"" +
                          deposit.Loan.Book.Title + "\" has been returned. Thank you!"
            });

            await _db.SaveChangesAsync();
            return Ok(new { message = "Deposit of " + deposit.Amount.ToString("F2") + " EUR returned to member." });
        }

        // POST api/deposits/{id}/forfeit
        [HttpPost("{id}/forfeit")]
        public async Task<IActionResult> ForfeitDeposit(int id, [FromQuery] int staffId, [FromQuery] string reason = "Book not returned")
        {
            var deposit = await _db.Deposits
                .Include(d => d.Loan).ThenInclude(l => l.Book)
                .FirstOrDefaultAsync(d => d.Id == id);
            if (deposit == null) return NotFound();

            deposit.Status            = DepositStatus.Forfeited;
            deposit.ReturnedAt        = DateTime.UtcNow;
            deposit.ReturnedByStaffId = staffId;
            deposit.Notes             = (deposit.Notes ?? "") + " | Forfeited: " + reason;

            _db.Notifications.Add(new Notification {
                UserId  = deposit.MemberId,
                Message = "Your deposit of " + deposit.Amount.ToString("F2") + " EUR for \"" +
                          deposit.Loan.Book.Title + "\" has been forfeited. Reason: " + reason
            });

            await _db.SaveChangesAsync();
            return Ok(new { message = "Deposit forfeited." });
        }
    }

    public record CreateBorrowRequestDto(int UserId, int BookId, int? Days, string? Notes);
    public record ApproveRequestDto(int StaffId, decimal DepositAmount, string? DepositNotes);
    public record RejectRequestDto(int StaffId, string Reason);
}