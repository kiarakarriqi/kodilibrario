using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryAPI.Models
{
    // ─── ENUMS ────────────────────────────────────────────────────────────────
    public enum UserRole { Member, Staff, Admin }

    // ── Statuset e reja ──────────────────────────────────────────────────────
    public enum BorrowRequestStatus { Pending, Approved, Rejected, Cancelled, Expired }
    public enum DepositStatus       { Held, Returned, Forfeited }

    // ── BorrowRequest — Member kërkon librin online, Staff konfirmon fizikisht ──
    public class BorrowRequest
    {
        public int                  Id                  { get; set; }
        public int                  UserId              { get; set; }
        public int                  BookId              { get; set; }
        public int                  Days                { get; set; } = 14;
        public string?              Notes               { get; set; }
        public BorrowRequestStatus  Status              { get; set; } = BorrowRequestStatus.Pending;
        public DateTime             RequestedAt         { get; set; } = DateTime.UtcNow;
        public DateTime             ExpiresAt           { get; set; }   // 48 ore per te ardhur
        public DateTime?            ProcessedAt         { get; set; }
        public int?                 ProcessedByStaffId  { get; set; }
        public string?              RejectionReason     { get; set; }
        public int?                 LoanId              { get; set; }   // Pasi aprovohet

        public User          User               { get; set; } = null!;
        public Book          Book               { get; set; } = null!;
        public User?         ProcessedByStaff   { get; set; }
        public Loan?         Loan               { get; set; }
    }

    // ── Deposit — Depozita cash si garanci ───────────────────────────────────
    public class Deposit
    {
        public int           Id                  { get; set; }
        public int           LoanId              { get; set; }
        public int           MemberId            { get; set; }
        public decimal       Amount              { get; set; }          // Shuma e depozites
        public DepositStatus Status              { get; set; } = DepositStatus.Held;
        public DateTime      CollectedAt         { get; set; } = DateTime.UtcNow;
        public DateTime?     ReturnedAt          { get; set; }
        public int           CollectedByStaffId  { get; set; }
        public int?          ReturnedByStaffId   { get; set; }
        public string?       Notes               { get; set; }

        public Loan  Loan               { get; set; } = null!;
        public User  Member             { get; set; } = null!;
        public User  CollectedByStaff   { get; set; } = null!;
        public User? ReturnedByStaff    { get; set; }
    }

    public enum LoanStatus { Active, Returned, Overdue }
    public enum ReservationStatus { Pending, Fulfilled, Cancelled, Expired }
    public enum FineStatus { Unpaid, Pending, Paid, Waived }
    public enum DonationStatus { Pending, Approved, Rejected }
    public enum RoomBookingStatus { Confirmed, Cancelled, Blocked }
    public enum EventStatus { Upcoming, Cancelled, Past }
    public enum BookCondition { Good, MinorWear, SignificantDamage, MajorDamage, Lost }

    // ─── USER ─────────────────────────────────────────────────────────────────
    public class User
    {
        public int Id { get; set; }
        [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
        [Required, MaxLength(200)] public string Email { get; set; } = string.Empty;
        [Required] public string PasswordHash { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Member;
        public bool IsActive { get; set; } = true;
        public DateTime? BorrowSuspendedUntil { get; set; }  // NEW: deri kur ka ndalim huazimi
        public int? ReadingGoal { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<Loan> Loans { get; set; } = new List<Loan>();
        public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
        public ICollection<Fine> Fines { get; set; } = new List<Fine>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
        public ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();
        public ICollection<BookReview> Reviews { get; set; } = new List<BookReview>();
        public ICollection<RoomBooking> RoomBookings { get; set; } = new List<RoomBooking>();
        public ICollection<EventRegistration> EventRegistrations { get; set; } = new List<EventRegistration>();
        public ICollection<BookDonation> Donations { get; set; } = new List<BookDonation>();
    }

    // ─── AUTHOR ───────────────────────────────────────────────────────────────
    public class Author
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
        public string? Bio { get; set; }
        public ICollection<Book> Books { get; set; } = new List<Book>();
    }

    // ─── CATEGORY ─────────────────────────────────────────────────────────────
    public class Category
    {
        public int Id { get; set; }
        [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
        public ICollection<Book> Books { get; set; } = new List<Book>();
    }

    // ─── BOOK ─────────────────────────────────────────────────────────────────
    public class Book
    {
        public int Id { get; set; }
        [Required, MaxLength(300)] public string Title { get; set; } = string.Empty;
        [MaxLength(20)] public string? ISBN { get; set; }
        public int? AuthorId { get; set; }
        public Author? Author { get; set; }
        public int? CategoryId { get; set; }
        public Category? Category { get; set; }
        public int TotalCopies { get; set; } = 1;
        public int AvailableCopies { get; set; } = 1;
        [Column(TypeName = "decimal(3,1)")] public decimal AverageRating { get; set; } = 0;
        public int ReviewCount { get; set; } = 0;
        public int? DonatedById { get; set; }
        public User? DonatedBy { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<Loan> Loans { get; set; } = new List<Loan>();
        public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
        public ICollection<BookReview> Reviews { get; set; } = new List<BookReview>();
        public ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();
    }

    // ─── LOAN ─────────────────────────────────────────────────────────────────
    public class Loan
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int BookId { get; set; }
        public Book Book { get; set; } = null!;
        public DateTime BorrowDate { get; set; } = DateTime.UtcNow;
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public LoanStatus Status { get; set; } = LoanStatus.Active;
        public int RenewalCount { get; set; } = 0;
        public DateTime? LastRenewedAt { get; set; }

        // Condition tracking on return
        public BookCondition? ReturnCondition { get; set; }
        [Column(TypeName = "decimal(10,2)")] public decimal DamageFee { get; set; } = 0;
        [MaxLength(500)] public string? ConditionNotes { get; set; }

        public Fine? Fine { get; set; }
        public BookReview? Review { get; set; }
    }

    // ─── RESERVATION ──────────────────────────────────────────────────────────
    public class Reservation
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int BookId { get; set; }
        public Book Book { get; set; } = null!;
        public DateTime ReservedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
        public int QueuePosition { get; set; } = 1;
    }

    // ─── FINE POLICY ──────────────────────────────────────────────────────────
    public class FinePolicy
    {
        public int Id { get; set; }
        [Column(TypeName = "decimal(10,2)")] public decimal DailyRate { get; set; } = 0.50m;
        public int GracePeriodDays { get; set; } = 1;
        [Column(TypeName = "decimal(10,2)")] public decimal BorrowBlockThreshold { get; set; } = 5.00m;
        public int SuspensionDays { get; set; } = 30;          // NEW: ditë suspended para ban
        public bool AutoBanAfterSuspension { get; set; } = true; // NEW: a kalon në ban automatikisht

        // Damage fees
        [Column(TypeName = "decimal(10,2)")] public decimal MinorWearFee { get; set; } = 2.00m;
        [Column(TypeName = "decimal(10,2)")] public decimal SignificantDamageFee { get; set; } = 5.00m;
        [Column(TypeName = "decimal(10,2)")] public decimal MajorDamageFee { get; set; } = 10.00m;
        [Column(TypeName = "decimal(10,2)")] public decimal LostBookFee { get; set; } = 20.00m;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public int? UpdatedByAdminId { get; set; }
        public User? UpdatedByAdmin { get; set; }
    }

    // ─── FINE ─────────────────────────────────────────────────────────────────
    public class Fine
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public Loan Loan { get; set; } = null!;
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        [Column(TypeName = "decimal(10,2)")] public decimal AmountDue { get; set; }
        [Column(TypeName = "decimal(10,2)")] public decimal AmountPaid { get; set; } = 0;
        public FineStatus Status { get; set; } = FineStatus.Unpaid;
        public DateTime? PaidAt { get; set; }
        public int? ConfirmedByStaffId { get; set; }
        public User? ConfirmedByStaff { get; set; }
        public DateTime? WaivedAt { get; set; }
        public int? WaivedByAdminId { get; set; }
        public User? WaivedByAdmin { get; set; }
        [MaxLength(500)] public string? WaiverReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ─── NOTIFICATION ─────────────────────────────────────────────────────────
    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        [Required] public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ─── WISHLIST ─────────────────────────────────────────────────────────────
    public class Wishlist
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int BookId { get; set; }
        public Book Book { get; set; } = null!;
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }

    // ─── BOOK REVIEW ──────────────────────────────────────────────────────────
    public class BookReview
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public Loan Loan { get; set; } = null!;
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int BookId { get; set; }
        public Book Book { get; set; } = null!;
        [Range(1, 5)] public int Rating { get; set; }
        [MaxLength(500)] public string? ReviewText { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; } = false;
        public int? ModeratedByStaffId { get; set; }
        public User? ModeratedByStaff { get; set; }
    }

    // ─── ROOM ─────────────────────────────────────────────────────────────────
    public class Room
    {
        public int Id { get; set; }
        [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
        [MaxLength(50)] public string? Floor { get; set; }
        public int Capacity { get; set; } = 1;
        public string? Description { get; set; }
        public string? Amenities { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<RoomBooking> Bookings { get; set; } = new List<RoomBooking>();
    }

    // ─── ROOM BOOKING ─────────────────────────────────────────────────────────
    public class RoomBooking
    {
        public int Id { get; set; }
        public int RoomId { get; set; }
        public Room Room { get; set; } = null!;
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public DateTime BookingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public RoomBookingStatus Status { get; set; } = RoomBookingStatus.Confirmed;
        [MaxLength(300)] public string? BlockedReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ─── BOOK DONATION ────────────────────────────────────────────────────────
    public class BookDonation
    {
        public int Id { get; set; }
        public int DonorId { get; set; }
        public User Donor { get; set; } = null!;
        [Required, MaxLength(300)] public string Title { get; set; } = string.Empty;
        [MaxLength(200)] public string? Author { get; set; }
        [MaxLength(20)] public string? ISBN { get; set; }
        [MaxLength(50)] public string Condition { get; set; } = "Good";
        [MaxLength(500)] public string? Note { get; set; }
        public DonationStatus Status { get; set; } = DonationStatus.Pending;
        public int? ReviewedByAdminId { get; set; }
        public User? ReviewedByAdmin { get; set; }
        [MaxLength(500)] public string? RejectionReason { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }
    }

    // ─── EVENT ────────────────────────────────────────────────────────────────
    public class LibraryEvent
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime EventDate { get; set; }
        [MaxLength(200)] public string? Location { get; set; }
        public int Capacity { get; set; } = 30;
        public int? LinkedBookId { get; set; }
        public Book? LinkedBook { get; set; }
        public EventStatus Status { get; set; } = EventStatus.Upcoming;
        public int CreatedByStaffId { get; set; }
        public User CreatedByStaff { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<EventRegistration> Registrations { get; set; } = new List<EventRegistration>();
    }

    // ─── EVENT REGISTRATION ───────────────────────────────────────────────────
    public class EventRegistration
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public LibraryEvent Event { get; set; } = null!;
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public bool IsWaitlisted { get; set; } = false;
        public int WaitlistPosition { get; set; } = 0;
    }

    // ─── DISMISSED RECOMMENDATIONS ────────────────────────────────────────────
    public class DismissedRecommendation
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int BookId { get; set; }
        public Book Book { get; set; } = null!;
        public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
    }
}