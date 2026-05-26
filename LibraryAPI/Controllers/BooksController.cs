using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LibraryAPI.Models;
using LibraryAPI.Data;

namespace LibraryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BooksController : ControllerBase
    {
        private readonly LibraryDbContext _db;
        public BooksController(LibraryDbContext db) => _db = db;

        // GET api/books — public catalog
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] int? categoryId)
        {
            var q = _db.Books
                .Include(b => b.Author)
                .Include(b => b.Category)
                .Where(b => b.IsActive);

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(b => b.Title.Contains(search) ||
                                 (b.Author != null && b.Author.Name.Contains(search)));
            if (categoryId.HasValue)
                q = q.Where(b => b.CategoryId == categoryId);

            var books = await q.Select(b => new {
                b.Id, b.Title, b.ISBN,
                Author   = b.Author == null ? null : b.Author.Name,
                Category = b.Category == null ? null : b.Category.Name,
                b.TotalCopies, b.AvailableCopies,
                b.AverageRating, b.ReviewCount,
                DonatedBy = b.DonatedBy == null ? null : b.DonatedBy.Name
            }).ToListAsync();

            return Ok(books);
        }

        // GET api/books/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetOne(int id)
        {
            var b = await _db.Books
                .Include(b => b.Author)
                .Include(b => b.Category)
                .FirstOrDefaultAsync(b => b.Id == id && b.IsActive);
            if (b == null) return NotFound();
            return Ok(new {
                b.Id, b.Title, b.ISBN,
                author = b.Author == null ? null : new { b.Author.Id, b.Author.Name },
                category = b.Category == null ? null : new { b.Category.Id, b.Category.Name },
                b.TotalCopies, b.AvailableCopies,
                b.AverageRating, b.ReviewCount,
                donatedBy = (object)null
            });
        }

        // POST api/books — Staff/Admin only
        [HttpPost]
        public async Task<IActionResult> Create(CreateBookDto dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.ISBN) &&
                await _db.Books.AnyAsync(b => b.ISBN == dto.ISBN))
                return BadRequest(new { message = "A book with this ISBN already exists." });

            var book = new Book
            {
                Title         = dto.Title,
                ISBN          = dto.ISBN,
                AuthorId      = dto.AuthorId,
                CategoryId    = dto.CategoryId,
                TotalCopies   = dto.TotalCopies,
                AvailableCopies = dto.TotalCopies
            };
            _db.Books.Add(book);
            await _db.SaveChangesAsync();
            return Ok(book);
        }

        // PUT api/books/{id} — Staff/Admin only
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, CreateBookDto dto)
        {
            var book = await _db.Books.FindAsync(id);
            if (book == null) return NotFound();
            book.Title      = dto.Title;
            book.ISBN       = dto.ISBN;
            book.AuthorId   = dto.AuthorId;
            book.CategoryId = dto.CategoryId;
            book.TotalCopies = dto.TotalCopies;
            await _db.SaveChangesAsync();
            return Ok(book);
        }

        // DELETE api/books/{id} — Admin only (soft delete)
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var book = await _db.Books.FindAsync(id);
            if (book == null) return NotFound();
            bool hasActiveLoans = await _db.Loans.AnyAsync(l => l.BookId == id && l.Status == LoanStatus.Active);
            if (hasActiveLoans)
                return BadRequest(new { message = "Cannot delete: book has active loans." });
            book.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Book deactivated." });
        }
    }

    public record CreateBookDto(string Title, string? ISBN, int? AuthorId, int? CategoryId, int TotalCopies = 1);
}
