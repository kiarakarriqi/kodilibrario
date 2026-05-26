using LibraryAPI.Data;
using LibraryAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "library.db");
builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddCors(options =>
    options.AddPolicy("AllowFrontend", p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.WriteIndented = true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<LibraryAPI.Services.OverdueProcessorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryAPI.Services.OverdueProcessorService>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    db.Database.Migrate();

    if (!db.Users.Any())
    {
        db.Users.AddRange(
            new User { Name="Administrator", Email="admin@librario.com", PasswordHash=BCrypt.Net.BCrypt.HashPassword("Admin123!"), Role=UserRole.Admin, IsActive=true },
            new User { Name="Library Staff", Email="staff@librario.com", PasswordHash=BCrypt.Net.BCrypt.HashPassword("Staff123!"), Role=UserRole.Staff, IsActive=true },
            new User { Name="Test Member",   Email="member@librario.com", PasswordHash=BCrypt.Net.BCrypt.HashPassword("Member123!"), Role=UserRole.Member, IsActive=true }
        );
        db.Categories.AddRange(
            new Category{Name="Fiction"}, new Category{Name="Non-Fiction"},
            new Category{Name="Science"}, new Category{Name="History"},
            new Category{Name="Technology"}, new Category{Name="Biography"}
        );
        db.Authors.AddRange(
            new Author{Name="George Orwell", Bio="English novelist and critic."},
            new Author{Name="J.K. Rowling", Bio="British author of Harry Potter."},
            new Author{Name="Yuval Noah Harari", Bio="Israeli historian."},
            new Author{Name="F. Scott Fitzgerald", Bio="American novelist."},
            new Author{Name="Agatha Christie", Bio="English mystery writer."}
        );
        db.FinePolicies.Add(new FinePolicy{DailyRate=0.50m,GracePeriodDays=1,BorrowBlockThreshold=5.00m,UpdatedAt=DateTime.UtcNow});
        db.Rooms.AddRange(
            new Room{Name="Room A — Quiet Study",  Floor="1st Floor", Capacity=4, Amenities="Whiteboard, Wi-Fi, Power outlets",   IsActive=true, Description="Silent study room for individual or small group work."},
            new Room{Name="Room B — Group Study",  Floor="1st Floor", Capacity=8, Amenities="Large whiteboard, Projector, Wi-Fi", IsActive=true, Description="Larger room for group meetings and presentations."},
            new Room{Name="Room C — Computer Lab", Floor="2nd Floor", Capacity=6, Amenities="6 PCs, Printer, Wi-Fi",              IsActive=true, Description="Equipped with desktop computers for research."},
            new Room{Name="Room D — Reading Nook", Floor="2nd Floor", Capacity=2, Amenities="Comfortable seating, Wi-Fi",         IsActive=true, Description="Cosy space for reading or tutoring."}
        );
        db.SaveChanges();

        var fiction=db.Categories.First(c=>c.Name=="Fiction");
        var nonfiction=db.Categories.First(c=>c.Name=="Non-Fiction");
        var orwell=db.Authors.First(a=>a.Name=="George Orwell");
        var rowling=db.Authors.First(a=>a.Name=="J.K. Rowling");
        var harari=db.Authors.First(a=>a.Name=="Yuval Noah Harari");
        var fitz=db.Authors.First(a=>a.Name=="F. Scott Fitzgerald");
        var christie=db.Authors.First(a=>a.Name=="Agatha Christie");

        db.Books.AddRange(
            new Book{Title="1984",                                     ISBN="9780451524935",AuthorId=orwell.Id,  CategoryId=fiction.Id,   TotalCopies=3,AvailableCopies=3},
            new Book{Title="Animal Farm",                              ISBN="9780451526342",AuthorId=orwell.Id,  CategoryId=fiction.Id,   TotalCopies=2,AvailableCopies=2},
            new Book{Title="Harry Potter and the Philosopher's Stone", ISBN="9780747532699",AuthorId=rowling.Id, CategoryId=fiction.Id,   TotalCopies=4,AvailableCopies=4},
            new Book{Title="Harry Potter and the Chamber of Secrets",  ISBN="9780439064873",AuthorId=rowling.Id, CategoryId=fiction.Id,   TotalCopies=3,AvailableCopies=3},
            new Book{Title="Sapiens: A Brief History of Humankind",    ISBN="9780062316097",AuthorId=harari.Id,  CategoryId=nonfiction.Id,TotalCopies=2,AvailableCopies=2},
            new Book{Title="Homo Deus",                                ISBN="9780062464316",AuthorId=harari.Id,  CategoryId=nonfiction.Id,TotalCopies=2,AvailableCopies=2},
            new Book{Title="The Great Gatsby",                         ISBN="9780743273565",AuthorId=fitz.Id,    CategoryId=fiction.Id,   TotalCopies=3,AvailableCopies=3},
            new Book{Title="Murder on the Orient Express",             ISBN="9780062693662",AuthorId=christie.Id,CategoryId=fiction.Id,   TotalCopies=2,AvailableCopies=2},
            new Book{Title="And Then There Were None",                 ISBN="9780062073488",AuthorId=christie.Id,CategoryId=fiction.Id,   TotalCopies=2,AvailableCopies=2}
        );
        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseCors("AllowFrontend");
var frontendPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "FrontEnd"));
if (Directory.Exists(frontendPath))
    app.UseStaticFiles(new StaticFileOptions{FileProvider=new PhysicalFileProvider(frontendPath),RequestPath=""});
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", context => { context.Response.Redirect("/login.html"); return Task.CompletedTask; });
app.Run();