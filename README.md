# My Library - Book Management System

## 🚀 Technologies
- **Backend:** C# ASP.NET Core Web API, Entity Framework Core, SQLite
- **Frontend:** HTML, CSS, JavaScript

# KOMANDA PER TE HAPUR PROJEKTIN

# 1. Nis API (hap terminal dhe bej):
cd ~/Desktop/LibraryProject/LibraryAPI && dotnet run

# 2. Hap browser (terminal tjeter ose pas API start):
open http://localhost:5273/login.html

# Ose direkt tek browser shkruaj:
http://localhost:5273/login.html


# KOMANDA PER CONSOLE (Safari/Chrome F12)

# Shiko userId e user te logged in:
sessionStorage.getItem('userId')

# Shiko te gjitha librat per user 1 (return as array):
fetch('http://localhost:5273/api/Books/1').then(r => r.json()).then(d => console.log(d))

# Shiko te gjitha librat per user 1 (return as JSON string):
fetch('http://localhost:5273/api/Books/1').then(r => r.json()).then(d => console.log(JSON.stringify(d)))

# Shiko total numri librave nga te gjitha usera (pop-up alert):
fetch('http://localhost:5273/api/Books').then(function(r){ return r.json() }).then(function(d){ alert('Total: ' + d.length + ' books') })

# Shiko te gjitha librat me userId (pop-up alert me list):
fetch('http://localhost:5273/api/Books').then(function(r){ return r.json() }).then(function(d){ var msg = ''; d.forEach(function(b){ msg += 'UserId: ' + b.userId + ' - ' + b.title + '\n' }); alert(msg) })


# KOMANDA PER DATABASE (Terminal)


# Shiko te gjitha Users (nga LibraryAPI folder):
sqlite3 library.db "SELECT * FROM Users;"

# Shiko te gjitha Users (nga cilado folder):
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db "SELECT * FROM Users;"

# Shiko te gjitha Users (me header dhe column format):
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db -header -column "SELECT * FROM Users;"

# Shiko te gjitha Books:
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db "SELECT * FROM Books;"

# Shiko te gjitha Books (me header dhe column format):
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db -header -column "SELECT * FROM Books;"

# Add single book
curl -X POST http://localhost:5273/api/Books \
  -H "Content-Type: application/json" \
  -d '{"title":"1984","author":"George Orwell","status":"Reading","userId":1}'

# Delete all books for user 1
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db \
  "DELETE FROM Books WHERE UserId = 1;"

# Delete ALL books 
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db \
  "DELETE FROM Books;"

# Search by title (contains "Harry")
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db -header -column "
SELECT * FROM Books WHERE Title LIKE '%Harry%';
"

# Search by author
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db -header -column "
SELECT * FROM Books WHERE Author LIKE '%Orwell%';
"

# Find all "Reading" books
sqlite3 ~/Desktop/LibraryProject/LibraryAPI/library.db -header -column "
SELECT u.Name as User, b.Title, b.Author
FROM Books b
JOIN Users u ON b.UserId = u.Id
WHERE b.Status = 'Reading';

# API ENDPOINTS (Browser apo Swagger)

# Shiko librat per user 1:
http://localhost:5273/api/Books/1

# Shiko te gjitha librat nga te gjitha usera:
http://localhost:5273/api/Books

# Swagger API documentation:
http://localhost:5273/swagger


# PERSHKRIME

# sessionStorage.getItem('userId') 
# → Return userId e user te logged in (1, 2, 3...)

# fetch api/Books/1 
# → Return te gjitha librat per user me userId=1

# alert('Total: ...')
# → Pop-up qe shon total number te librave

# alert msg with userId + title
# → Pop-up qe shon listen e librave me userId dhe title

# sqlite3 ... "SELECT * FROM Users"
# → Shiaj te gjitha users tek database (Id, Email, Password hash, Name)

# sqlite3 ... "SELECT * FROM Books"
# → Shiaj te gjitha librat tek database (Id, Title, Author, Status, UserId)

# -header -column
# → Format output me headers dhe columns aligned (me lehte per te lexuar)