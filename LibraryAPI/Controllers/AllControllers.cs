using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LibraryAPI.Models;
using LibraryAPI.Data;

// ═══════════════════════════════════════════════════════════════════════════════
// RESERVATIONS
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class ReservationsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public ReservationsController(LibraryDbContext db) => _db = db;

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(int userId)
        {
            var res = await _db.Reservations
                .Include(r => r.Book).ThenInclude(b => b.Author)
                .Where(r => r.UserId == userId && r.Status == ReservationStatus.Pending)
                .OrderBy(r => r.ReservedAt)
                .Select(r => new {
                    r.Id, r.ReservedAt, r.ExpiresAt, r.QueuePosition,
                    Status = r.Status.ToString(),
                    Book = new { r.Book.Id, r.Book.Title, Author = r.Book.Author == null ? "" : r.Book.Author.Name, r.Book.AvailableCopies }
                }).ToListAsync();
            return Ok(res);
        }

        [HttpPost]
        public async Task<IActionResult> Reserve(ReserveDto dto)
        {
            var activeCount = await _db.Reservations
                .CountAsync(r => r.UserId == dto.UserId && r.Status == ReservationStatus.Pending);
            if (activeCount >= 3)
                return BadRequest(new { message = "Maximum 3 active reservations allowed." });

            var alreadyExists = await _db.Reservations.AnyAsync(r =>
                r.UserId == dto.UserId && r.BookId == dto.BookId && r.Status == ReservationStatus.Pending);
            if (alreadyExists)
                return BadRequest(new { message = "You already have a reservation for this book." });

            var queue = await _db.Reservations
                .CountAsync(r => r.BookId == dto.BookId && r.Status == ReservationStatus.Pending);

            var reservation = new Reservation
            {
                UserId = dto.UserId, BookId = dto.BookId,
                ExpiresAt = DateTime.UtcNow.AddDays(3),
                QueuePosition = queue + 1
            };
            _db.Reservations.Add(reservation);
            await _db.SaveChangesAsync();
            return Ok(new { reservation.Id, reservation.QueuePosition, reservation.ExpiresAt, message = $"Reserved! You are #{reservation.QueuePosition} in queue." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Cancel(int id)
        {
            var r = await _db.Reservations.FindAsync(id);
            if (r == null) return NotFound();
            r.Status = ReservationStatus.Cancelled;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Reservation cancelled." });
        }
    }
    public record ReserveDto(int UserId, int BookId);
}

// ═══════════════════════════════════════════════════════════════════════════════
// FINES
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class FinesController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public FinesController(LibraryDbContext db) => _db = db;

       [HttpGet("user/{userId}")]
public async Task<IActionResult> GetByUser(
    int userId,
    [FromServices] LibraryAPI.Services.OverdueProcessorService processor)
{
    // Refresh fines on every read so the user always sees up-to-date amounts.
    // The background service still runs every 6h as a safety net.
    await processor.ProcessOverdueLoansAsync();

    var fines = await _db.Fines
        .Include(f => f.Loan).ThenInclude(l => l.Book)
        .Where(f => f.UserId == userId)
        .OrderByDescending(f => f.CreatedAt)
        .Select(f => new {
            f.Id, f.AmountDue, f.AmountPaid,
            Status = f.Status.ToString(),
            f.PaidAt, f.WaivedAt, f.WaiverReason,
            Book = f.Loan.Book.Title,
            DueDate = f.Loan.DueDate,
            ReturnDate = f.Loan.ReturnDate
        }).ToListAsync();
    return Ok(fines);
}

        [HttpPost("{fineId}/request-payment")]
        public async Task<IActionResult> RequestPayment(int fineId)
        {
            var fine = await _db.Fines.FindAsync(fineId);
            if (fine == null) return NotFound();
            if (fine.Status != FineStatus.Unpaid)
                return BadRequest(new { message = "Fine is not in Unpaid status." });
            fine.Status = FineStatus.Pending;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Payment requested. Staff will confirm soon." });
        }

        [HttpPost("{fineId}/confirm-payment")]
        public async Task<IActionResult> ConfirmPayment(int fineId, ConfirmPaymentDto dto)
        {
            var fine = await _db.Fines
                .Include(f => f.Loan).ThenInclude(l => l.Book)
                .FirstOrDefaultAsync(f => f.Id == fineId);
            if (fine == null) return NotFound();
            fine.AmountPaid += dto.Amount;
            if (fine.AmountPaid >= fine.AmountDue)
            {
                fine.AmountPaid = fine.AmountDue;
                fine.Status = FineStatus.Paid;
                fine.PaidAt = DateTime.UtcNow;
                fine.ConfirmedByStaffId = dto.StaffId;
                _db.Notifications.Add(new Notification
                {
                    UserId  = fine.UserId,
                    Message = $"Your fine of €{fine.AmountDue:F2} for \"{fine.Loan.Book.Title}\" has been marked as paid. Thank you!"
                });
            }
            await _db.SaveChangesAsync();
            return Ok(new { fine.AmountPaid, fine.AmountDue, Status = fine.Status.ToString() });
        }

        [HttpPost("{fineId}/waive")]
        public async Task<IActionResult> Waive(int fineId, WaiveDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return BadRequest(new { message = "Waiver reason is required." });
            var fine = await _db.Fines
                .Include(f => f.Loan).ThenInclude(l => l.Book)
                .FirstOrDefaultAsync(f => f.Id == fineId);
            if (fine == null) return NotFound();
            fine.Status = FineStatus.Waived;
            fine.WaivedAt = DateTime.UtcNow;
            fine.WaivedByAdminId = dto.AdminId;
            fine.WaiverReason = dto.Reason;
            _db.Notifications.Add(new Notification
            {
                UserId  = fine.UserId,
                Message = $"Your fine of €{fine.AmountDue:F2} for \"{fine.Loan.Book.Title}\" has been waived. Reason: {dto.Reason}"
            });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Fine waived." });
        }

        [HttpGet("policy")]
        public async Task<IActionResult> GetPolicy()
        {
            var p = await _db.FinePolicies.FirstOrDefaultAsync();
            if (p == null) return NotFound();
            return Ok(p);
        }

        [HttpPut("policy")]
        public async Task<IActionResult> UpdatePolicy(FinePolicyDto dto)
        {
            var policy = await _db.FinePolicies.FirstOrDefaultAsync();
            if (policy == null) return NotFound();
            policy.DailyRate = dto.DailyRate;
            policy.GracePeriodDays = dto.GracePeriodDays;
            policy.BorrowBlockThreshold = dto.BorrowBlockThreshold;
            policy.UpdatedAt = DateTime.UtcNow;
            policy.UpdatedByAdminId = dto.AdminId;
            await _db.SaveChangesAsync();
            return Ok(policy);
        }
    }
    public record ConfirmPaymentDto(int StaffId, decimal Amount);
    public record WaiveDto(int AdminId, string Reason);
    public record FinePolicyDto(decimal DailyRate, int GracePeriodDays, decimal BorrowBlockThreshold, int AdminId);
}

// ═══════════════════════════════════════════════════════════════════════════════
// REVIEWS
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class ReviewsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public ReviewsController(LibraryDbContext db) => _db = db;

        [HttpGet("book/{bookId}")]
        public async Task<IActionResult> GetForBook(int bookId)
        {
            var reviews = await _db.BookReviews
                .Include(r => r.User)
                .Where(r => r.BookId == bookId && !r.IsDeleted)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new {
                    r.Id, r.Rating, r.ReviewText, r.CreatedAt, r.UpdatedAt,
                    MemberName = r.User.Name
                }).ToListAsync();
            return Ok(reviews);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateReviewDto dto)
        {
            var loan = await _db.Loans.FindAsync(dto.LoanId);
            if (loan == null || loan.Status != LoanStatus.Returned)
                return BadRequest(new { message = "You can only review a book after returning it." });
            if (loan.UserId != dto.UserId) return Unauthorized();

            var alreadyReviewed = await _db.BookReviews.AnyAsync(r => r.UserId == dto.UserId && r.BookId == dto.BookId);
            if (alreadyReviewed)
                return BadRequest(new { message = "You have already reviewed this book." });

            var review = new BookReview
            {
                LoanId = dto.LoanId, UserId = dto.UserId, BookId = dto.BookId,
                Rating = dto.Rating, ReviewText = dto.ReviewText
            };
            _db.BookReviews.Add(review);
            await _db.SaveChangesAsync();
            await UpdateBookRating(dto.BookId);
            return Ok(new {
                review.Id,
                review.LoanId,
                review.UserId,
                review.BookId,
                review.Rating,
                review.ReviewText,
                review.CreatedAt
            });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id, [FromQuery] int requesterId, [FromQuery] bool isModerator = false)
        {
            var review = await _db.BookReviews.FindAsync(id);
            if (review == null) return NotFound();
            if (!isModerator && review.UserId != requesterId) return Unauthorized();
            review.IsDeleted = true;
            if (isModerator) review.ModeratedByStaffId = requesterId;
            if (review.UserId != requesterId)
                _db.Notifications.Add(new Notification
                {
                    UserId  = review.UserId,
                    Message = "One of your reviews has been removed by a moderator."
                });
            await _db.SaveChangesAsync();
            await UpdateBookRating(review.BookId);
            return Ok(new { message = "Review deleted." });
        }

        private async Task UpdateBookRating(int bookId)
        {
            var reviews = await _db.BookReviews
                .Where(r => r.BookId == bookId && !r.IsDeleted).ToListAsync();
            var book = await _db.Books.FindAsync(bookId);
            if (book == null) return;
            book.ReviewCount = reviews.Count;
            book.AverageRating = reviews.Count > 0 ? (decimal)reviews.Average(r => r.Rating) : 0;
            await _db.SaveChangesAsync();
        }
    }
    public record CreateReviewDto(int LoanId, int UserId, int BookId, int Rating, string? ReviewText);
}

// ═══════════════════════════════════════════════════════════════════════════════
// WISHLIST
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class WishlistController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public WishlistController(LibraryDbContext db) => _db = db;

        [HttpGet("{userId}")]
        public async Task<IActionResult> Get(int userId)
        {
            var list = await _db.Wishlists
                .Include(w => w.Book).ThenInclude(b => b.Author)
                .Where(w => w.UserId == userId)
                .Select(w => new {
                    w.Id, w.AddedAt,
                    Book = new { w.Book.Id, w.Book.Title,
                        Author = w.Book.Author == null ? "" : w.Book.Author.Name,
                        w.Book.AvailableCopies, w.Book.AverageRating }
                }).ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> Add(WishlistDto dto)
        {
            var exists = await _db.Wishlists.AnyAsync(w => w.UserId == dto.UserId && w.BookId == dto.BookId);
            if (exists) return BadRequest(new { message = "Already in wishlist." });
            _db.Wishlists.Add(new Wishlist { UserId = dto.UserId, BookId = dto.BookId });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Added to wishlist." });
        }

        [HttpDelete("{userId}/{bookId}")]
        public async Task<IActionResult> Remove(int userId, int bookId)
        {
            var w = await _db.Wishlists.FirstOrDefaultAsync(x => x.UserId == userId && x.BookId == bookId);
            if (w == null) return NotFound();
            _db.Wishlists.Remove(w);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Removed from wishlist." });
        }
    }
    public record WishlistDto(int UserId, int BookId);
}

// ═══════════════════════════════════════════════════════════════════════════════
// ROOMS — Fixed TimeSpan handling
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class RoomsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public RoomsController(LibraryDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _db.Rooms.Where(r => r.IsActive).ToListAsync());

        [HttpPost]
        public async Task<IActionResult> Create(RoomDto dto)
        {
            var room = new Room
            {
                Name = dto.Name, Floor = dto.Floor, Capacity = dto.Capacity,
                Description = dto.Description, Amenities = dto.Amenities
            };
            _db.Rooms.Add(room);
            await _db.SaveChangesAsync();
            return Ok(room);
        }

        [HttpGet("{roomId}/bookings")]
        public async Task<IActionResult> GetBookings(int roomId, [FromQuery] string date)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
                return BadRequest(new { message = "Invalid date format." });

            var bookings = await _db.RoomBookings
                .Include(b => b.User)
                .Where(b => b.RoomId == roomId &&
                            b.BookingDate.Date == parsedDate.Date &&
                            b.Status != RoomBookingStatus.Cancelled)
                .Select(b => new {
                    b.Id,
                    StartTime = b.StartTime.ToString(@"hh\:mm"),
                    EndTime   = b.EndTime.ToString(@"hh\:mm"),
                    Status    = b.Status.ToString(),
                    b.BlockedReason,
                    MemberName = b.User.Name,
                    MemberId   = b.UserId
                }).ToListAsync();
            return Ok(bookings);
        }

        [HttpPost("book")]
        public async Task<IActionResult> BookRoom(BookRoomDto dto)
        {
            // Max 2 active bookings per member
            var activeCount = await _db.RoomBookings.CountAsync(b =>
                b.UserId == dto.UserId &&
                b.Status == RoomBookingStatus.Confirmed &&
                b.BookingDate >= DateTime.UtcNow.Date);
            if (activeCount >= 2)
                return BadRequest(new { message = "Maximum 2 active room bookings allowed." });

            // Parse times
            if (!TimeSpan.TryParse(dto.StartTime, out var startTime))
                return BadRequest(new { message = "Invalid start time format. Use HH:mm:ss" });
            if (!TimeSpan.TryParse(dto.EndTime, out var endTime))
                return BadRequest(new { message = "Invalid end time format. Use HH:mm:ss" });

            // Max 2h
            if ((endTime - startTime).TotalHours > 2)
                return BadRequest(new { message = "Maximum booking duration is 2 hours." });
            if (endTime <= startTime)
                return BadRequest(new { message = "End time must be after start time." });

            // Parse booking date
            if (!DateTime.TryParse(dto.BookingDate, out var bookingDate))
                return BadRequest(new { message = "Invalid booking date." });
            bookingDate = bookingDate.Date;

            // Check overlap
            var existingBookings = await _db.RoomBookings
                .Where(b => b.RoomId == dto.RoomId &&
                            b.BookingDate.Date == bookingDate &&
                            b.Status == RoomBookingStatus.Confirmed)
                .ToListAsync();

            var conflict = existingBookings.Any(b =>
                b.StartTime < endTime && b.EndTime > startTime);
            if (conflict)
                return BadRequest(new { message = "This time slot is not available. Please choose another." });

            var room = await _db.Rooms.FindAsync(dto.RoomId);
            if (room == null) return NotFound(new { message = "Room not found." });

            var booking = new RoomBooking
            {
                RoomId = dto.RoomId, UserId = dto.UserId,
                BookingDate = bookingDate,
                StartTime = startTime,
                EndTime   = endTime,
                Status    = RoomBookingStatus.Confirmed
            };
            _db.RoomBookings.Add(booking);

            _db.Notifications.Add(new Notification
            {
                UserId  = dto.UserId,
                Message = $"Room booking confirmed: {room.Name} on {bookingDate:dd MMM} from {startTime:hh\\:mm} to {endTime:hh\\:mm}."
            });
            await _db.SaveChangesAsync();
            return Ok(new { booking.Id, message = "Room booked successfully!" });
        }

        [HttpDelete("bookings/{id}")]
        public async Task<IActionResult> CancelBooking(int id)
        {
            var b = await _db.RoomBookings.FindAsync(id);
            if (b == null) return NotFound();
            var slotStart = b.BookingDate.Add(b.StartTime);
            if (DateTime.UtcNow > slotStart.AddMinutes(-30))
                return BadRequest(new { message = "Cannot cancel within 30 minutes of the slot start." });
            b.Status = RoomBookingStatus.Cancelled;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Booking cancelled." });
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserBookings(int userId)
        {
            var bookings = await _db.RoomBookings
                .Include(b => b.Room)
                .Where(b => b.UserId == userId && b.BookingDate >= DateTime.UtcNow.Date && b.Status == RoomBookingStatus.Confirmed)
                .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime)
                .Select(b => new {
                    b.Id,
                    RoomName    = b.Room.Name,
                    Floor       = b.Room.Floor,
                    BookingDate = b.BookingDate.ToString("yyyy-MM-dd"),
                    StartTime   = b.StartTime.ToString(@"hh\:mm"),
                    EndTime     = b.EndTime.ToString(@"hh\:mm")
                }).ToListAsync();
            return Ok(bookings);
        }
    }
    public record RoomDto(string Name, string? Floor, int Capacity, string? Description, string? Amenities);
    public record BookRoomDto(int RoomId, int UserId, string BookingDate, string StartTime, string EndTime);
}

// ═══════════════════════════════════════════════════════════════════════════════
// DONATIONS
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class DonationsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public DonationsController(LibraryDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] DonationStatus? status)
        {
            var q = _db.BookDonations.Include(d => d.Donor).AsQueryable();
            if (status.HasValue) q = q.Where(d => d.Status == status.Value);
            return Ok(await q.OrderByDescending(d => d.SubmittedAt)
                .Select(d => new {
                    d.Id, d.Title, d.Author, d.ISBN, d.Condition,
                    d.Note, Status = d.Status.ToString(), d.SubmittedAt,
                    DonorName = d.Donor.Name, d.RejectionReason
                }).ToListAsync());
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(int userId) =>
            Ok(await _db.BookDonations.Where(d => d.DonorId == userId)
                .OrderByDescending(d => d.SubmittedAt)
                .Select(d => new { d.Id, d.Title, Status = d.Status.ToString(), d.SubmittedAt, d.RejectionReason })
                .ToListAsync());

        [HttpPost]
        public async Task<IActionResult> Submit(SubmitDonationDto dto)
        {
            var donation = new BookDonation
            {
                DonorId = dto.UserId, Title = dto.Title, Author = dto.Author,
                ISBN = dto.ISBN, Condition = dto.Condition ?? "Good", Note = dto.Note
            };
            _db.BookDonations.Add(donation);
            await _db.SaveChangesAsync();
            return Ok(new { donation.Id, message = "Donation submitted for review. Thank you!" });
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(int id, ApproveDonationDto dto)
        {
            var donation = await _db.BookDonations.Include(d => d.Donor)
                .FirstOrDefaultAsync(d => d.Id == id);
            if (donation == null) return NotFound();
            if (donation.Status != DonationStatus.Pending)
                return BadRequest(new { message = "Donation is no longer pending." });

            donation.Status = DonationStatus.Approved;
            donation.ReviewedByAdminId = dto.AdminId;
            donation.ReviewedAt = DateTime.UtcNow;

            // Resolve AuthorId: explicit > existing by name (case-insensitive) > create new
            int? authorId = dto.AuthorId;
            if (!authorId.HasValue && !string.IsNullOrWhiteSpace(dto.NewAuthorName))
            {
                var trimmed = dto.NewAuthorName.Trim();
                var existing = await _db.Authors.FirstOrDefaultAsync(a => a.Name.ToLower() == trimmed.ToLower());
                if (existing != null) { authorId = existing.Id; }
                else
                {
                    var newAuthor = new Author { Name = trimmed };
                    _db.Authors.Add(newAuthor);
                    await _db.SaveChangesAsync();
                    authorId = newAuthor.Id;
                }
            }
            else if (!authorId.HasValue && !string.IsNullOrWhiteSpace(donation.Author))
            {
                // Fallback: use the author name the donor typed
                var donorAuthor = donation.Author.Trim();
                var existing = await _db.Authors.FirstOrDefaultAsync(a => a.Name.ToLower() == donorAuthor.ToLower());
                if (existing != null) { authorId = existing.Id; }
                else
                {
                    var newAuthor = new Author { Name = donorAuthor };
                    _db.Authors.Add(newAuthor);
                    await _db.SaveChangesAsync();
                    authorId = newAuthor.Id;
                }
            }

            Book? book = null;
            if (!string.IsNullOrWhiteSpace(donation.ISBN))
                book = await _db.Books.FirstOrDefaultAsync(b => b.ISBN == donation.ISBN);

            if (book != null)
            {
                book.TotalCopies++;
                book.AvailableCopies++;
                // Backfill author/category if missing on the existing book
                if (book.AuthorId == null && authorId.HasValue) book.AuthorId = authorId;
                if (book.CategoryId == null && dto.CategoryId.HasValue) book.CategoryId = dto.CategoryId;
            }
            else
            {
                book = new Book
                {
                    Title = donation.Title, ISBN = donation.ISBN,
                    TotalCopies = 1, AvailableCopies = 1,
                    DonatedById = donation.DonorId,
                    AuthorId = authorId,
                    CategoryId = dto.CategoryId
                };
                _db.Books.Add(book);
            }

            _db.Notifications.Add(new Notification
            {
                UserId  = donation.DonorId,
                Message = $"Your donation of \"{donation.Title}\" has been accepted and added to the library catalog. Thank you!"
            });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Donation approved and book added to catalog." });
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(int id, RejectDonationDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return BadRequest(new { message = "Rejection reason is required." });
            var donation = await _db.BookDonations.FindAsync(id);
            if (donation == null) return NotFound();
            donation.Status = DonationStatus.Rejected;
            donation.ReviewedByAdminId = dto.AdminId;
            donation.ReviewedAt = DateTime.UtcNow;
            donation.RejectionReason = dto.Reason;
            _db.Notifications.Add(new Notification
            {
                UserId  = donation.DonorId,
                Message = $"Your donation of \"{donation.Title}\" was not accepted. Reason: {dto.Reason}"
            });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Donation rejected." });
        }
    }
    public record SubmitDonationDto(int UserId, string Title, string? Author, string? ISBN, string? Condition, string? Note);
    public record RejectDonationDto(int AdminId, string Reason);
    public record ApproveDonationDto(int AdminId, int? AuthorId, string? NewAuthorName, int? CategoryId);
}

// ═══════════════════════════════════════════════════════════════════════════════
// EVENTS
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public EventsController(LibraryDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var events = await _db.Events
                .Include(e => e.LinkedBook)
                .Include(e => e.Registrations)
                .Where(e => e.Status == EventStatus.Upcoming)
                .OrderBy(e => e.EventDate)
                .Select(e => new {
                    e.Id, e.Title, e.Description, e.EventDate, e.Location, e.Capacity,
                    LinkedBook = e.LinkedBook == null ? null : e.LinkedBook.Title,
                    RegisteredCount = e.Registrations.Count(r => !r.IsWaitlisted)
                }).ToListAsync();
            return Ok(events);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateEventDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest(new { message = "Title is required." });

            var ev = new LibraryEvent
            {
                Title            = dto.Title,
                Description      = dto.Description,
                EventDate        = dto.EventDate,
                Location         = dto.Location,
                Capacity         = dto.Capacity > 0 ? dto.Capacity : 30,
                LinkedBookId     = dto.LinkedBookId,
                CreatedByStaffId = dto.StaffId,
                Status           = EventStatus.Upcoming
            };
            _db.Events.Add(ev);
            await _db.SaveChangesAsync();
            return Ok(new { ev.Id, ev.Title, ev.EventDate, message = "Event created successfully!" });
        }

        [HttpPost("{eventId}/register")]
        public async Task<IActionResult> Register(int eventId, [FromQuery] int userId)
        {
            var ev = await _db.Events.Include(e => e.Registrations)
                .FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null || ev.Status != EventStatus.Upcoming)
                return BadRequest(new { message = "Event not available." });

            var alreadyRegistered = ev.Registrations.Any(r => r.UserId == userId);
            if (alreadyRegistered)
                return BadRequest(new { message = "You are already registered for this event." });

            var confirmedCount = ev.Registrations.Count(r => !r.IsWaitlisted);
            var isWaitlisted   = confirmedCount >= ev.Capacity;
            var waitlistPos    = isWaitlisted ? ev.Registrations.Count(r => r.IsWaitlisted) + 1 : 0;

            var reg = new EventRegistration
            {
                EventId = eventId, UserId = userId,
                IsWaitlisted = isWaitlisted, WaitlistPosition = waitlistPos
            };
            _db.EventRegistrations.Add(reg);
            _db.Notifications.Add(new Notification
            {
                UserId  = userId,
                Message = isWaitlisted
                    ? $"You are #{waitlistPos} on the waitlist for \"{ev.Title}\" on {ev.EventDate:dd MMM yyyy}."
                    : $"Registration confirmed for \"{ev.Title}\" on {ev.EventDate:dd MMM yyyy} at {ev.Location ?? "TBD"}."
            });
            await _db.SaveChangesAsync();
            return Ok(new { isWaitlisted, waitlistPos, message = isWaitlisted ? $"Added to waitlist (#{waitlistPos})." : "Registration confirmed!" });
        }

        [HttpDelete("{eventId}/register/{userId}")]
        public async Task<IActionResult> Unregister(int eventId, int userId)
        {
            var reg = await _db.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId);
            if (reg == null) return NotFound();
            _db.EventRegistrations.Remove(reg);

            if (!reg.IsWaitlisted)
            {
                var next = await _db.EventRegistrations
                    .Where(r => r.EventId == eventId && r.IsWaitlisted)
                    .OrderBy(r => r.WaitlistPosition).FirstOrDefaultAsync();
                if (next != null)
                {
                    next.IsWaitlisted = false;
                    next.WaitlistPosition = 0;
                    var ev = await _db.Events.FindAsync(eventId);
                    _db.Notifications.Add(new Notification
                    {
                        UserId  = next.UserId,
                        Message = $"Great news! A spot opened up for \"{ev?.Title}\". You are now confirmed!"
                    });
                }
            }
            await _db.SaveChangesAsync();
            return Ok(new { message = "Registration cancelled." });
        }

        [HttpPost("{eventId}/cancel")]
        public async Task<IActionResult> CancelEvent(int eventId)
        {
            var ev = await _db.Events.Include(e => e.Registrations)
                .FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null) return NotFound();
            ev.Status = EventStatus.Cancelled;
            foreach (var reg in ev.Registrations)
                _db.Notifications.Add(new Notification
                {
                    UserId  = reg.UserId,
                    Message = $"The event \"{ev.Title}\" on {ev.EventDate:dd MMM yyyy} has been cancelled."
                });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Event cancelled. All members notified." });
        }
    }
    public record CreateEventDto(string Title, string? Description, DateTime EventDate, string? Location, int Capacity, int? LinkedBookId, int StaffId);
}

// ═══════════════════════════════════════════════════════════════════════════════
// NOTIFICATIONS
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class NotificationsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public NotificationsController(LibraryDbContext db) => _db = db;

        [HttpGet("{userId}")]
        public async Task<IActionResult> Get(int userId) =>
            Ok(await _db.Notifications.Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync());

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var n = await _db.Notifications.FindAsync(id);
            if (n == null) return NotFound();
            n.IsRead = true;
            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// STATS
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class StatsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public StatsController(LibraryDbContext db) => _db = db;

        [HttpGet("member/{userId}")]
        public async Task<IActionResult> GetMemberStats(int userId)
        {
            var user  = await _db.Users.FindAsync(userId);
            var loans = await _db.Loans
                .Include(l => l.Book).ThenInclude(b => b.Category)
                .Where(l => l.UserId == userId).ToListAsync();

            var thisYear = DateTime.UtcNow.Year;
            var perMonth = Enumerable.Range(1, 12).Select(m => new
            {
                Month = m,
                Count = loans.Count(l => l.BorrowDate.Year == thisYear && l.BorrowDate.Month == m)
            });

            var categories = loans
                .Where(l => l.Book?.Category != null)
                .GroupBy(l => l.Book.Category!.Name)
                .OrderByDescending(g => g.Count())
                .Select(g => new { Category = g.Key, Count = g.Count() });

            return Ok(new
            {
                TotalLoans  = loans.Count,
                ThisYear    = loans.Count(l => l.Status == LoanStatus.Returned && l.BorrowDate.Year == thisYear),
                ThisMonth   = loans.Count(l => l.BorrowDate.Year == thisYear && l.BorrowDate.Month == DateTime.UtcNow.Month),
                Returned    = loans.Count(l => l.Status == LoanStatus.Returned),
                ReadingGoal = user?.ReadingGoal,
                PerMonth    = perMonth,
                TopCategories = categories.Take(5)
            });
        }

        [HttpGet("admin")]
        public async Task<IActionResult> GetAdminStats()
        {
            var now = DateTime.UtcNow;
            var allFines    = await _db.Fines.ToListAsync();
            var outstanding = allFines.Where(f => f.Status == FineStatus.Unpaid || f.Status == FineStatus.Pending).Sum(f => f.AmountDue - f.AmountPaid);
            var collected   = allFines.Where(f => f.Status == FineStatus.Paid).Sum(f => f.AmountPaid);

            return Ok(new
            {
                TotalLoansThisMonth   = await _db.Loans.CountAsync(l => l.BorrowDate.Month == now.Month && l.BorrowDate.Year == now.Year),
                TotalOverdue          = await _db.Loans.CountAsync(l => l.Status == LoanStatus.Active && l.DueDate < now),
                TotalFinesOutstanding = outstanding,
                TotalFinesCollected   = collected,
                TotalMembers          = await _db.Users.CountAsync(u => u.Role == UserRole.Member && u.IsActive),
                TopBooks = await _db.Loans.GroupBy(l => l.Book.Title)
                    .Select(g => new { Book = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count).Take(10).ToListAsync()
            });
        }

        [HttpGet("recommendations/{userId}")]
        public async Task<IActionResult> GetRecommendations(int userId)
        {
            var myBookIds = await _db.Loans.Where(l => l.UserId == userId)
                .Select(l => l.BookId).Distinct().ToListAsync();

            if (myBookIds.Count < 2)
            {
                var popular = await _db.Loans
                    .Where(l => !myBookIds.Contains(l.BookId))
                    .GroupBy(l => l.BookId)
                    .OrderByDescending(g => g.Count()).Take(6)
                    .Select(g => g.Key).ToListAsync();
                var popBooks = await _db.Books.Include(b => b.Author)
                    .Where(b => popular.Contains(b.Id) && b.IsActive)
                    .Select(b => new { b.Id, b.Title, Author = b.Author == null ? "" : b.Author.Name, b.AverageRating, b.AvailableCopies })
                    .ToListAsync();
                return Ok(new { Source = "popular", Books = popBooks });
            }

            var dismissed = await _db.DismissedRecommendations
                .Where(d => d.UserId == userId).Select(d => d.BookId).ToListAsync();

            var similarUserIds = await _db.Loans
                .Where(l => myBookIds.Contains(l.BookId) && l.UserId != userId)
                .GroupBy(l => l.UserId)
                .Where(g => g.Select(l => l.BookId).Distinct().Count() >= 2)
                .Select(g => g.Key).ToListAsync();

            var candidates = await _db.Loans
                .Where(l => similarUserIds.Contains(l.UserId) &&
                       !myBookIds.Contains(l.BookId) &&
                       !dismissed.Contains(l.BookId))
                .GroupBy(l => l.BookId)
                .OrderByDescending(g => g.Count()).Take(6)
                .Select(g => g.Key).ToListAsync();

            var books = await _db.Books.Include(b => b.Author).Include(b => b.Category)
                .Where(b => candidates.Contains(b.Id) && b.IsActive)
                .Select(b => new { b.Id, b.Title, Author = b.Author == null ? "" : b.Author.Name, Category = b.Category == null ? "" : b.Category.Name, b.AverageRating, b.AvailableCopies })
                .ToListAsync();

            // Fallback: nese collaborative s'ka rezultate, kthe librat popullore qe user-i nuk i ka marre
            if (books.Count == 0)
            {
                var popular = await _db.Loans
                    .Where(l => !myBookIds.Contains(l.BookId) && !dismissed.Contains(l.BookId))
                    .GroupBy(l => l.BookId)
                    .OrderByDescending(g => g.Count()).Take(6)
                    .Select(g => g.Key).ToListAsync();

                var popBooks = await _db.Books.Include(b => b.Author).Include(b => b.Category)
                    .Where(b => popular.Contains(b.Id) && b.IsActive)
                    .Select(b => new { b.Id, b.Title, Author = b.Author == null ? "" : b.Author.Name, Category = b.Category == null ? "" : b.Category.Name, b.AverageRating, b.AvailableCopies })
                    .ToListAsync();

                // Nese as popullore s'ka (ti i ke marre te gjitha), kthe libra te tjere te disponueshem
               if (popBooks.Count == 0)
                {
                    var allBooks = await _db.Books.Include(b => b.Author).Include(b => b.Category)
                        .Where(b => !myBookIds.Contains(b.Id) && !dismissed.Contains(b.Id) && b.IsActive)
                        .ToListAsync();

                    popBooks = allBooks
                        .OrderByDescending(b => b.AverageRating)
                        .Take(6)
                        .Select(b => new { b.Id, b.Title, Author = b.Author == null ? "" : b.Author.Name, Category = b.Category == null ? "" : b.Category.Name, b.AverageRating, b.AvailableCopies })
                        .ToList();
                }

                return Ok(new { Source = "popular-fallback", Books = popBooks });
            }

            return Ok(new { Source = "collaborative", Books = books });
        }

        [HttpGet("waitlist-analytics")]
        public async Task<IActionResult> GetWaitlistAnalytics()
        {
            var bookWaitlists = await _db.Reservations
                .Where(r => r.Status == ReservationStatus.Pending)
                .GroupBy(r => r.BookId)
                .Select(g => new {
                    BookId = g.Key,
                    Count  = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            var bookIds = bookWaitlists.Select(x => x.BookId).ToList();
            var books   = await _db.Books
                .Where(b => bookIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Title, b.AvailableCopies })
                .ToListAsync();

            var result = bookWaitlists.Select(w => {
                var book = books.FirstOrDefault(b => b.Id == w.BookId);
                return new {
                    BookId    = w.BookId,
                    Title     = book?.Title ?? "Unknown",
                    Available = book?.AvailableCopies ?? 0,
                    Waiting   = w.Count
                };
            });

            var eventWaitlists = await _db.EventRegistrations
                .Where(r => r.IsWaitlisted)
                .GroupBy(r => r.EventId)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            var eventIds = eventWaitlists.Select(x => x.EventId).ToList();
            var events   = await _db.Events
                .Where(e => eventIds.Contains(e.Id))
                .Select(e => new { e.Id, e.Title, e.Capacity,
                    Registered = e.Registrations.Count(r => !r.IsWaitlisted) })
                .ToListAsync();

            var eventResult = eventWaitlists.Select(w => {
                var ev = events.FirstOrDefault(e => e.Id == w.EventId);
                return new {
                    EventId    = w.EventId,
                    Title      = ev?.Title ?? "Unknown",
                    Capacity   = ev?.Capacity ?? 0,
                    Registered = ev?.Registered ?? 0,
                    Waiting    = w.Count
                };
            });

            return Ok(new { Books = result, Events = eventResult });
        }

        [HttpPost("dismiss")]
        public async Task<IActionResult> Dismiss(DismissDto dto)
        {
            var already = await _db.DismissedRecommendations
                .AnyAsync(d => d.UserId == dto.UserId && d.BookId == dto.BookId);
            if (!already)
            {
                _db.DismissedRecommendations.Add(new DismissedRecommendation { UserId = dto.UserId, BookId = dto.BookId });
                await _db.SaveChangesAsync();
            }
            return Ok();
        }
    }
    public record DismissDto(int UserId, int BookId);
}

// ═══════════════════════════════════════════════════════════════════════════════
// ADMIN
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public AdminController(LibraryDbContext db) => _db = db;

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers([FromQuery] string? search)
        {
            var q = _db.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(u => u.Name.Contains(search) || u.Email.Contains(search));
            return Ok(await q.Select(u => new {
                u.Id, u.Name, u.Email, Role = u.Role.ToString(), u.IsActive, u.CreatedAt
            }).ToListAsync());
        }

        [HttpPost("users/{userId}/toggle-active")]
        public async Task<IActionResult> ToggleActive(int userId, [FromQuery] int adminId)
        {
            if (userId == adminId)
                return BadRequest(new { message = "You cannot deactivate yourself." });
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return NotFound();
            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { user.Id, user.IsActive });
        }

        [HttpPost("users/{userId}/set-role")]
        public async Task<IActionResult> SetRole(int userId, SetRoleDto dto)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return NotFound();
            if (!Enum.TryParse<UserRole>(dto.Role, out var role))
                return BadRequest(new { message = "Invalid role. Use: Member, Staff, or Admin." });
            user.Role = role;
            await _db.SaveChangesAsync();
            return Ok(new { user.Id, Role = user.Role.ToString() });
        }
    }
    public record SetRoleDto(string Role);
}

// ═══════════════════════════════════════════════════════════════════════════════
// AUTHORS & CATEGORIES
// ═══════════════════════════════════════════════════════════════════════════════
namespace LibraryAPI.Controllers
{
    [ApiController, Route("api/authors")]
    public class AuthorsController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public AuthorsController(LibraryDbContext db) => _db = db;
        [HttpGet] public async Task<IActionResult> Get() => Ok(await _db.Authors.OrderBy(a => a.Name).ToListAsync());
        [HttpPost] public async Task<IActionResult> Create(AuthorDto dto)
        {
            var a = new Author { Name = dto.Name, Bio = dto.Bio };
            _db.Authors.Add(a); await _db.SaveChangesAsync(); return Ok(a);
        }
    }

    [ApiController, Route("api/categories")]
    public class CategoriesController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public CategoriesController(LibraryDbContext db) => _db = db;
        [HttpGet] public async Task<IActionResult> Get() => Ok(await _db.Categories.OrderBy(c => c.Name).ToListAsync());
        [HttpPost] public async Task<IActionResult> Create(CategoryDto dto)
        {
            var c = new Category { Name = dto.Name };
            _db.Categories.Add(c); await _db.SaveChangesAsync(); return Ok(c);
        }
    }

    public record AuthorDto(string Name, string? Bio);
    public record CategoryDto(string Name);
}