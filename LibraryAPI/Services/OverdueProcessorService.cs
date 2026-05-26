using LibraryAPI.Data;
using LibraryAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryAPI.Services
{
    /// <summary>
    /// Background service that runs every 6 hours:
    /// 1. Marks active loans as Overdue when DueDate + grace days has passed
    /// 2. Creates Fine if missing, updates AmountDue daily for unpaid fines
    /// 3. Suspends borrow privileges when fine consumes the deposit
    /// 4. Bans account after SuspensionDays have passed without return
    /// </summary>
    public class OverdueProcessorService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<OverdueProcessorService> _logger;
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);

        public OverdueProcessorService(IServiceProvider services, ILogger<OverdueProcessorService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOverdueLoansAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OverdueProcessorService failed during execution");
                }

                try
                {
                    await Task.Delay(RunInterval, stoppingToken);
                }
                catch (TaskCanceledException) { break; }
            }
        }

        /// <summary>
        /// Public so it can be called manually from a controller (e.g. GET /fines).
        /// </summary>
        public async Task ProcessOverdueLoansAsync(CancellationToken ct = default)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

            var policy = await db.FinePolicies.FirstOrDefaultAsync(ct);
            if (policy == null)
            {
                _logger.LogWarning("No FinePolicy found. Skipping overdue processing.");
                return;
            }

            var graceDays = policy.GracePeriodDays;
            var dailyRate = policy.DailyRate;
            var suspensionDays = policy.SuspensionDays;
            var autoBan = policy.AutoBanAfterSuspension;
            var today = DateTime.UtcNow.Date;

            // Get all loans that are NOT returned and have passed due date + grace
            var candidates = await db.Loans
                .Include(l => l.Book)
                .Include(l => l.User)
                .Include(l => l.Fine)
                .Where(l => l.Status != LoanStatus.Returned)
                .Where(l => l.DueDate.Date.AddDays(graceDays) < today)
                .ToListAsync(ct);

            int finesCreated = 0, finesUpdated = 0, suspended = 0, banned = 0, statusChanged = 0;

            foreach (var loan in candidates)
            {
                var overdueDays = (today - loan.DueDate.Date).Days - graceDays;
                if (overdueDays <= 0) continue;

                // 1. Update Loan.Status to Overdue if still Active
                if (loan.Status == LoanStatus.Active)
                {
                    loan.Status = LoanStatus.Overdue;
                    statusChanged++;
                }

                var amountDue = overdueDays * dailyRate;

                // 2. Create or update Fine
                if (loan.Fine == null)
                {
                    var newFine = new Fine
                    {
                        LoanId = loan.Id,
                        UserId = loan.UserId,
                        AmountDue = amountDue,
                        Status = FineStatus.Unpaid
                    };
                    db.Fines.Add(newFine);
                    finesCreated++;

                    db.Notifications.Add(new Notification
                    {
                        UserId = loan.UserId,
                        Message = $"\"{loan.Book.Title}\" is overdue. Fine: €{amountDue:F2} ({overdueDays} day(s) late)."
                    });
                }
                else if (loan.Fine.Status == FineStatus.Unpaid && loan.Fine.AmountDue != amountDue)
                {
                    loan.Fine.AmountDue = amountDue;
                    finesUpdated++;
                }

                // 3. Check deposit consumption → suspend
                var deposit = await db.Deposits
                    .Where(d => d.LoanId == loan.Id && d.Status == DepositStatus.Held)
                    .FirstOrDefaultAsync(ct);

                if (deposit != null && amountDue >= deposit.Amount)
                {
                    // Deposit fully consumed
                    if (loan.User.BorrowSuspendedUntil == null)
                    {
                        loan.User.BorrowSuspendedUntil = today.AddDays(suspensionDays);
                        suspended++;
                        db.Notifications.Add(new Notification
                        {
                            UserId = loan.UserId,
                            Message = $"Borrowing suspended until {loan.User.BorrowSuspendedUntil:dd/MM/yyyy}. " +
                                      $"Your deposit (€{deposit.Amount:F2}) has been fully consumed by overdue fines for \"{loan.Book.Title}\". " +
                                      $"Return the book to avoid permanent account ban."
                        });
                    }

                    // 4. Suspension period expired → ban + forfeit deposit
                    if (autoBan
                        && loan.User.BorrowSuspendedUntil.HasValue
                        && loan.User.BorrowSuspendedUntil.Value.Date < today
                        && loan.User.IsActive)
                    {
                        loan.User.IsActive = false;
                        deposit.Status = DepositStatus.Forfeited;
                        deposit.ReturnedAt = DateTime.UtcNow;
                        banned++;
                        db.Notifications.Add(new Notification
                        {
                            UserId = loan.UserId,
                            Message = $"Your account has been deactivated. " +
                                      $"\"{loan.Book.Title}\" was not returned within the suspension period. " +
                                      $"Your deposit of €{deposit.Amount:F2} has been forfeited. Contact the library."
                        });
                    }
                }
            }

            if (finesCreated + finesUpdated + suspended + banned + statusChanged > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Overdue run: {Status} status→Overdue, {Created} fines created, {Updated} fines updated, {Susp} suspended, {Banned} banned",
                    statusChanged, finesCreated, finesUpdated, suspended, banned);
            }
        }
    }
}