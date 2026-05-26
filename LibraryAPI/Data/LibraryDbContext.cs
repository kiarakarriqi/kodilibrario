using Microsoft.EntityFrameworkCore;
using LibraryAPI.Models;

namespace LibraryAPI.Data
{
    public class LibraryDbContext : DbContext
    {
        public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Author> Authors => Set<Author>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Book> Books => Set<Book>();
        public DbSet<Loan> Loans => Set<Loan>();
        public DbSet<Reservation> Reservations => Set<Reservation>();
        public DbSet<FinePolicy> FinePolicies => Set<FinePolicy>();
        public DbSet<Fine> Fines => Set<Fine>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<Wishlist> Wishlists => Set<Wishlist>();
        public DbSet<BookReview> BookReviews => Set<BookReview>();
        public DbSet<Room> Rooms => Set<Room>();
        public DbSet<RoomBooking> RoomBookings => Set<RoomBooking>();
        public DbSet<BookDonation> BookDonations => Set<BookDonation>();
        public DbSet<LibraryEvent> Events => Set<LibraryEvent>();
        public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
        public DbSet<DismissedRecommendation> DismissedRecommendations => Set<DismissedRecommendation>();
        public DbSet<BorrowRequest> BorrowRequests => Set<BorrowRequest>();
        public DbSet<Deposit>       Deposits        => Set<Deposit>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // Wishlist: unique per user+book
            mb.Entity<Wishlist>()
                .HasIndex(w => new { w.UserId, w.BookId }).IsUnique();

            // BookReview: unique per user+book
            mb.Entity<BookReview>()
                .HasIndex(r => new { r.UserId, r.BookId }).IsUnique();

            // EventRegistration: unique per event+user
            mb.Entity<EventRegistration>()
                .HasIndex(e => new { e.EventId, e.UserId }).IsUnique();

            // DismissedRecommendation: unique per user+book
            mb.Entity<DismissedRecommendation>()
                .HasIndex(d => new { d.UserId, d.BookId }).IsUnique();

            // Fine: one-to-one with Loan
            mb.Entity<Fine>()
                .HasOne(f => f.Loan)
                .WithOne(l => l.Fine)
                .HasForeignKey<Fine>(f => f.LoanId);

            // BookReview → Loan (one-to-one)
            mb.Entity<BookReview>()
                .HasOne(r => r.Loan)
                .WithOne(l => l.Review)
                .HasForeignKey<BookReview>(r => r.LoanId);

            // Avoid multiple cascade paths on Fine
            mb.Entity<Fine>()
                .HasOne(f => f.ConfirmedByStaff)
                .WithMany().HasForeignKey(f => f.ConfirmedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);

            mb.Entity<Fine>()
                .HasOne(f => f.WaivedByAdmin)
                .WithMany().HasForeignKey(f => f.WaivedByAdminId)
                .OnDelete(DeleteBehavior.SetNull);

            // Book → DonatedBy
            mb.Entity<Book>()
                .HasOne(b => b.DonatedBy)
                .WithMany().HasForeignKey(b => b.DonatedById)
                .OnDelete(DeleteBehavior.SetNull);

            // BookDonation → ReviewedByAdmin
            mb.Entity<BookDonation>()
                .HasOne(d => d.ReviewedByAdmin)
                .WithMany().HasForeignKey(d => d.ReviewedByAdminId)
                .OnDelete(DeleteBehavior.SetNull);

            // BookReview → ModeratedByStaff
            mb.Entity<BookReview>()
                .HasOne(r => r.ModeratedByStaff)
                .WithMany().HasForeignKey(r => r.ModeratedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);

            // FinePolicy → UpdatedByAdmin
            mb.Entity<FinePolicy>()
                .HasOne(p => p.UpdatedByAdmin)
                .WithMany().HasForeignKey(p => p.UpdatedByAdminId)
                .OnDelete(DeleteBehavior.SetNull);

            // LibraryEvent → CreatedByStaff (restrict to avoid cascade conflict)
            mb.Entity<LibraryEvent>()
                .HasOne(e => e.CreatedByStaff)
                .WithMany().HasForeignKey(e => e.CreatedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            // Seed FinePolicy default
            mb.Entity<FinePolicy>().HasData(new FinePolicy
            {
                Id = 1,
                DailyRate = 0.50m,
                GracePeriodDays = 1,
                BorrowBlockThreshold = 5.00m,
                UpdatedAt = new DateTime(2026, 1, 1)
            });
        }
    }
}
