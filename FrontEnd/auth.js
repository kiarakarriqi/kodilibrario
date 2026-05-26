var API = 'http://localhost:5273/api';

var loginForm = document.getElementById('login-form');
if (loginForm) {
  loginForm.addEventListener('submit', async function(e) {
    e.preventDefault();
    var email = document.getElementById('username').value.trim();
    var password = document.getElementById('password').value.trim();
    var errEl = document.getElementById('error-msg');
    errEl.classList.remove('show');
    try {
      var res = await fetch(API + '/auth/login', {
        method: 'POST', headers: {'Content-Type':'application/json'},
        body: JSON.stringify({ email: email, password: password })
      });
      if (!res.ok) {
        var d = await res.json();
        errEl.textContent = d.message || 'Invalid credentials!';
        errEl.classList.add('show'); return;
      }
      var user = await res.json();
      sessionStorage.setItem('userId', String(user.userId));
      sessionStorage.setItem('userName', user.name);
      sessionStorage.setItem('userRole', user.role);
      if (user.role === 'Admin' || user.role === 'Staff') {
        window.location.href = 'staff-dashboard.html';
      } else {
        window.location.href = 'index.html';
      }
    } catch(err) {
      errEl.textContent = 'Server error. Make sure the API is running!';
      errEl.classList.add('show');
    }
  });
}

var registerForm = document.getElementById('register-form');
if (registerForm) {
  registerForm.addEventListener('submit', async function(e) {
    e.preventDefault();
    var firstName = (document.getElementById('reg-firstname').value || '').trim();
    var lastName  = (document.getElementById('reg-lastname').value || '').trim();
    var email     = document.getElementById('reg-email').value.trim();
    var password  = document.getElementById('reg-password').value.trim();
    var errEl     = document.getElementById('error-msg');
    errEl.classList.remove('show');
    if (!firstName || !lastName || !email || !password) {
      errEl.textContent = 'Please fill in first name, last name, email and password.';
      errEl.classList.add('show'); return;
    }
    if (firstName.length < 2 || lastName.length < 2) {
      errEl.textContent = 'First and last name must be at least 2 characters each.';
      errEl.classList.add('show'); return;
    }
    if (password.length < 6) {
      errEl.textContent = 'Password must be at least 6 characters.';
      errEl.classList.add('show'); return;
    }
    var name = firstName + ' ' + lastName;
    try {
      var res = await fetch(API + '/auth/register', {
        method: 'POST', headers: {'Content-Type':'application/json'},
        body: JSON.stringify({ name: name, email: email, password: password })
      });
      if (!res.ok) {
        var d = await res.json();
        errEl.textContent = d.message || 'Registration failed!';
        errEl.classList.add('show'); return;
      }
      var user = await res.json();
      sessionStorage.setItem('userId', String(user.userId));
      sessionStorage.setItem('userName', user.name);
      sessionStorage.setItem('userRole', user.role);
      window.location.href = 'index.html';
    } catch(err) {
      errEl.textContent = 'Server error. Make sure the API is running!';
      errEl.classList.add('show');
    }
  });
}

function getCurrentUserId()   { return parseInt(sessionStorage.getItem('userId'), 10); }
function getCurrentUserName() { return sessionStorage.getItem('userName') || 'User'; }
function getCurrentUserRole() { return sessionStorage.getItem('userRole') || 'Member'; }
function isStaff()  { var r = getCurrentUserRole(); return r === 'Staff' || r === 'Admin'; }
function isAdmin()  { return getCurrentUserRole() === 'Admin'; }

function checkAuth() {
  var userId = sessionStorage.getItem('userId');
  var page = window.location.pathname;
  if (page.includes('login.html') || page.includes('register.html')) return;
  if (!userId) { window.location.href = 'login.html'; return; }
  var role = getCurrentUserRole();
  var memberPages = ['index.html','catalog.html','loans.html','wishlist.html','reservations.html','fines.html','rooms.html','events.html','stats.html','notifications.html','donations.html'];
  var isOnMemberPage = memberPages.some(function(p) { return page.includes(p); });
  if (isOnMemberPage && (role === 'Staff' || role === 'Admin')) {
    window.location.href = 'staff-dashboard.html'; return;
  }
  if (page.includes('staff-dashboard.html') && role === 'Member') {
    window.location.href = 'index.html'; return;
  }
}

function logout() { sessionStorage.clear(); window.location.href = 'login.html'; }

function showToast(message, type) {
  type = type || 'success';
  var old = document.querySelector('.toast'); if (old) old.remove();
  var t = document.createElement('div');
  t.className = 'toast ' + type; t.textContent = message;
  document.body.appendChild(t);
  setTimeout(function() {
    t.style.opacity = '0'; t.style.transform = 'translateX(400px)';
    setTimeout(function() { if (t.parentElement) t.remove(); }, 300);
  }, 3000);
}

checkAuth();

// Kontrolloj nese member ka libra shume overdue (>14 dite) ose llogari te bllokuar
async function checkAccountStatus() {
  var uid = sessionStorage.getItem('userId');
  var role = sessionStorage.getItem('userRole');
  var page = window.location.pathname;
  
  // Vetem per member, vetem ne faqet kryesore
  if (!uid || role !== 'Member') return;
  if (page.includes('login.html') || page.includes('register.html')) return;
  
  // Mos kontrollo ne faqen e fines dhe loans (lejoje te shoh problemet)
  if (page.includes('fines.html') || page.includes('loans.html') || page.includes('notifications.html')) return;
  
  try {
    // Merr loans aktive
    var loans = await fetch('http://localhost:5273/api/loans/user/' + uid)
      .then(function(r){ return r.json(); }).catch(function(){ return []; });
    
    var today = new Date();
    var severeOverdue = loans.filter(function(l) {
      if (l.status !== 'Active') return false;
      var due = new Date(l.dueDate);
      var daysOverdue = Math.ceil((today - due) / 86400000);
      return daysOverdue > 14; // Me shume se 14 dite overdue
    });
    
    // Merr fines
    var fines = await fetch('http://localhost:5273/api/fines/user/' + uid)
      .then(function(r){ return r.json(); }).catch(function(){ return []; });
    
    var unpaid = fines.filter(function(f) { return f.status === 'Unpaid' || f.status === 'Pending'; });
    var totalFine = unpaid.reduce(function(sum, f) { return sum + (f.amountDue - f.amountPaid); }, 0);
    
    // Shfaq banner bllokues nese ka problem serioz
    if (severeOverdue.length > 0 || totalFine >= 5) {
      showBlockBanner(severeOverdue, totalFine);
    }
  } catch(e) {}
}

function showBlockBanner(overdueLoans, totalFine) {
  // Mos shto dy here
  if (document.getElementById('account-block-banner')) return;
  
  var msg = '';
  if (overdueLoans.length > 0) {
    var titles = overdueLoans.map(function(l){ return '"' + l.book.title + '"'; }).join(', ');
    msg += '<div style="margin-bottom:8px">📚 <strong>Overdue books (' + overdueLoans.length + '):</strong> ' + titles + '</div>';
  }
  if (totalFine >= 5) {
    msg += '<div>💶 <strong>Unpaid fines: €' + totalFine.toFixed(2) + '</strong> — above the €5.00 borrowing limit.</div>';
  }
  
  var banner = document.createElement('div');
  banner.id = 'account-block-banner';
  banner.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:99999;background:linear-gradient(135deg,#c0392b,#e74c3c);color:white;padding:0;box-shadow:0 4px 20px rgba(0,0,0,0.3)';
  banner.innerHTML = 
    '<div style="padding:16px 24px;display:flex;justify-content:space-between;align-items:center;gap:16px">' +
      '<div style="display:flex;align-items:flex-start;gap:14px">' +
        '<span style="font-size:28px;flex-shrink:0">🚫</span>' +
        '<div>' +
          '<div style="font-weight:700;font-size:15px;margin-bottom:6px">Account Restricted — Action Required</div>' +
          msg +
          '<div style="font-size:12px;opacity:0.85;margin-top:6px">Please return your overdue books and settle fines at the library desk to restore full access.</div>' +
        '</div>' +
      '</div>' +
      '<div style="display:flex;gap:8px;flex-shrink:0">' +
        '<a href="loans.html" style="background:white;color:#c0392b;padding:8px 14px;border-radius:8px;font-size:13px;font-weight:700;text-decoration:none;white-space:nowrap">View Loans</a>' +
        '<a href="fines.html" style="background:rgba(255,255,255,0.2);color:white;padding:8px 14px;border-radius:8px;font-size:13px;font-weight:600;text-decoration:none;white-space:nowrap">View Fines</a>' +
      '</div>' +
    '</div>';
  
  // Shto padding-top te body per te mos mbuluar content
  document.body.style.paddingTop = '100px';
  document.body.insertBefore(banner, document.body.firstChild);
}

// Thirr pas checkAuth
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', function(){ checkAccountStatus(); });
} else {
  setTimeout(checkAccountStatus, 500);
}

